using Avalonia.Threading;

namespace Dicta.Services;

sealed class DictationController : IDisposable
{
    private static readonly TimeSpan SilenceGap = TimeSpan.FromSeconds(2.1);
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan MaxDuration = TimeSpan.FromSeconds(90);
    private const float VoiceThreshold = 0.014f;

    private readonly OverlayWindow _window;
    private readonly Transcriber _transcriber = new();
    private CloudTranscriber? _cloud;
    private string? _activeModel;
    private readonly SettingsStore _settings = new();
    private readonly DispatcherTimer _clock;
    private readonly DispatcherTimer _permissionClock;
    private bool _accessibilityPrompted;
    private IAudioCapture? _mic;
    private Phase _phase = Phase.Idle;
    private int _generation;
    private nint _target;
    private DateTime _startedUtc;
    private DateTime _lastVoiceUtc;
    private bool _heard;
    private int _hotBuffers;
    private bool _hotkeyOk = true;
    private bool _started;
    private CancellationTokenSource? _insertCts;

    public DictationController(OverlayWindow window)
    {
        _window = window;
        _clock = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _clock.Tick += (_, _) => OnClock();
        _permissionClock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _permissionClock.Tick += (_, _) =>
        {
            if (_window.Mode == CardMode.Setup)
                RefreshPermissions(closeWhenReady: false);
            else
                _permissionClock.Stop();
        };
        _transcriber.Progress += OnModelProgress;
    }

    public void Start()
    {
        if (_started)
            return;
        _started = true;

        _settings.Load();
        ConfigureCloud();
        _hotkeyOk = HotkeyService.Start();
        HotkeyService.Pressed += OnHotkey;
        if (OperatingSystem.IsMacOS() && !MacPermissions.Ready())
            ShowSetup();
        else if (!_settings.WelcomeDismissed || !_hotkeyOk)
            ShowHelp();

        _ = PrepareQuietlyAsync();
    }

    public void ShowHelp()
    {
        if (_phase != Phase.Idle || _window.Mode == CardMode.Key)
            return;

        var body = "Click a text field and press the shortcut. Speak English. After a short pause, the text is typed there.";
        string? warning = null;
        if (!_hotkeyOk)
            warning = OperatingSystem.IsWindows()
                ? "Another app is using that shortcut. You can dictate from the tray icon."
                : "The shortcut isn't available. Dictate from the tray, or run dicta in a terminal.";
        else if (_cloud != null)
            warning = "Transcribing with OpenAI " + _cloud.Model + ". Open the tray menu to change or remove the key.";
        else if (!_transcriber.ModelExists)
            warning = "The first launch downloads the English model, about 140 MB. Internet is only needed that once.";
        else if (!_settings.WelcomeDismissed && UnixPaste.SetupHint() is { } hint)
            warning = hint;

        _window.ShowHelp(body, warning);
    }

    public void ShowApiKey()
    {
        if (_phase != Phase.Idle)
            return;

        var hint = _cloud != null
            ? "Dicta is using " + _cloud.Model + ". Clear the field and save to go back to the model on this computer."
            : "Dicta is using the model on this computer. Paste an OpenAI key to use OpenAI instead.";
        _window.ShowApiKey(_settings.ApiKey, hint);
    }

    private void SaveApiKey()
    {
        var key = _window.ApiKeyText.Trim();
        _settings.ApiKey = string.IsNullOrWhiteSpace(key) ? null : key;

        // A saved key starts from the preferred model: the project behind the previous key
        // may have lacked access to it and left a fallback behind.
        _settings.ModelName = null;
        _activeModel = null;
        _settings.Save();
        _window.ClearApiKeyInput();
        ConfigureCloud();

        _phase = Phase.Idle;
        _window.ShowMessage(
            "Done",
            _cloud == null
                ? "Dicta will use the model on this computer."
                : "Dicta will use OpenAI " + _cloud.Model + ".",
            null,
            stick: false);

        if (_cloud == null)
            _ = PrepareQuietlyAsync();
    }

    private void ConfigureCloud()
    {
        _cloud?.Dispose();
        _cloud = null;

        var key = _settings.ApiKey?.Trim();
        if (string.IsNullOrEmpty(key))
            return;

        _cloud = new CloudTranscriber(key, _activeModel ?? _settings.ModelName);
        Log.Info("Transcribing with OpenAI " + _cloud.Model + ".");
    }

    private async Task<string> TranscribeAsync(byte[] pcm, CancellationToken cancellationToken)
    {
        var cloud = _cloud;
        if (cloud != null)
        {
            try
            {
                return await cloud.TranscribeAsync(pcm, cancellationToken).ConfigureAwait(false);
            }
            catch (ModelUnavailableException ex)
            {
                var text = await TryFallbackModelsAsync(pcm, ex.Model, cancellationToken).ConfigureAwait(false);
                if (text != null)
                    return text;
                throw;
            }
            catch (CloudTranscriptionException ex) when (ex.Transient && _transcriber.ModelExists)
            {
                Log.Error("OpenAI transcription failed. Using the local model.", ex);
            }
        }

        return await _transcriber.TranscribeAsync(pcm, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// A key can belong to a project without access to the best model, and the provider rolls
    /// model access out gradually. Keep the downgrade in memory for the session: it costs one
    /// request per start instead of locking the fallback in forever.
    /// </summary>
    private async Task<string?> TryFallbackModelsAsync(byte[] pcm, string unavailable, CancellationToken cancellationToken)
    {
        var key = _settings.ApiKey?.Trim();
        if (string.IsNullOrEmpty(key))
            return null;

        foreach (var candidate in CloudTranscriber.PreferredModels)
        {
            if (string.Equals(candidate, unavailable, StringComparison.OrdinalIgnoreCase))
                continue;

            using var probe = new CloudTranscriber(key, candidate);
            try
            {
                var text = await probe.TranscribeAsync(pcm, cancellationToken).ConfigureAwait(false);
                await AdoptModelAsync(candidate).ConfigureAwait(false);
                Log.Info("OpenAI " + unavailable + " is not available. Using " + candidate + " until Dicta restarts.");
                return text;
            }
            catch (ModelUnavailableException)
            {
                // Try the next one in the list.
            }
            catch (CloudTranscriptionException)
            {
                return null;
            }
        }

        return null;
    }

    /// <summary>
    /// Picks the best model the key can actually use, once per start, so dictation never pays
    /// the cost of discovering it. A silent half second is enough to ask the provider.
    /// </summary>
    private async Task NegotiateModelAsync()
    {
        var key = _settings.ApiKey?.Trim();
        if (string.IsNullOrEmpty(key))
            return;

        var silence = new byte[16_000 / 2];
        foreach (var candidate in CloudTranscriber.PreferredModels)
        {
            using var probe = new CloudTranscriber(key, candidate);
            try
            {
                await probe.TranscribeAsync(silence, CancellationToken.None).ConfigureAwait(false);
                await AdoptModelAsync(candidate).ConfigureAwait(false);
                Log.Info("OpenAI model for this session: " + candidate + ".");
                return;
            }
            catch (ModelUnavailableException)
            {
                // The project behind this key cannot use that model. Ask for the next one.
            }
            catch (CloudTranscriptionException ex)
            {
                Log.Error("Couldn't check which OpenAI model the key can use.", ex);
                return;
            }
        }

        Log.Error("The key can't use any known OpenAI transcription model. Trying again when dictating.");
    }

    private async Task AdoptModelAsync(string model)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (_phase != Phase.Idle)
                return;

            _activeModel = model;
            ConfigureCloud();
        });
    }

    public void ShowSetup()
    {
        if (_phase != Phase.Idle)
            return;

        RefreshPermissions(closeWhenReady: false);
    }

    private void RefreshPermissions(bool closeWhenReady)
    {
        if (_phase != Phase.Idle || !Dispatcher.UIThread.CheckAccess())
            return;

        var microphone = MacPermissions.MicrophoneAllowed();
        var accessibility = MacPermissions.AccessibilityAllowed();
        _window.ShowSetup(microphone, accessibility, _accessibilityPrompted && !accessibility);
        if (!_permissionClock.IsEnabled)
            _permissionClock.Start();
        if (!closeWhenReady || !microphone || !accessibility)
            return;

        _permissionClock.Stop();
        _settings.WelcomeDismissed = true;
        _settings.Save();
        _window.HideCard();
    }

    public void AllowMicrophone()
    {
        MacPermissions.AllowMicrophone(_ =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (_window.Mode == CardMode.Setup)
                    ShowSetup();
            });
        });
    }

    public void AllowAccessibility()
    {
        if (MacPermissions.AccessibilityAllowed())
        {
            MacPermissions.OpenPrivacy("Privacy_Accessibility");
            return;
        }

        if (_accessibilityPrompted)
        {
            Log.Info("Restarting so macOS can apply Accessibility.");
            Environment.Exit(0);
            return;
        }

        _accessibilityPrompted = true;
        MacPermissions.AllowAccessibility();
        if (_window.Mode == CardMode.Setup)
            _window.ShowSetup(MacPermissions.MicrophoneAllowed(), false, restartAccessibility: true);
    }

    public void ToggleFromTray()
    {
        var foreground = OperatingSystem.IsWindows() ? Native.GetForegroundWindow() : 0;
        Toggle(foreground);
    }

    public void OnPrimary()
    {
        switch (_window.Mode)
        {
            case CardMode.Help:
                _settings.WelcomeDismissed = true;
                _settings.Save();
                _window.HideCard();
                break;
            case CardMode.Setup:
                RefreshPermissions(closeWhenReady: true);
                break;
            case CardMode.Listening:
                Finish(insert: true);
                break;
            case CardMode.Message:
                _window.HideCard();
                _phase = Phase.Idle;
                break;
            case CardMode.Key:
                SaveApiKey();
                break;
        }
    }

    public void OnSecondary()
    {
        if (_phase == Phase.Listening)
            Finish(insert: false);
        else if (_phase != Phase.Working)
            _window.HideCard();
    }

    public void OnDismiss()
    {
        if (_phase == Phase.Listening)
            Finish(insert: false);
        else if (_phase == Phase.Working)
            CancelWork();
        else
            _window.HideCard();
    }

    public void Dispose()
    {
        _generation++;
        _insertCts?.Cancel();
        _clock.Stop();
        _permissionClock.Stop();
        HotkeyService.Pressed -= OnHotkey;
        _mic?.Dispose();
        _cloud?.Dispose();
        _transcriber.Dispose();
    }

    private void OnHotkey()
    {
        var foreground = OperatingSystem.IsWindows() ? Native.GetForegroundWindow() : 0;
        Dispatcher.UIThread.Post(() => Toggle(foreground));
    }

    private void Toggle(nint foreground)
    {
        if (_phase == Phase.Working || _window.Mode == CardMode.Key)
            return;
        if (_phase == Phase.Listening)
        {
            RememberTarget(foreground);
            Finish(insert: true);
            return;
        }

        Begin(foreground);
    }

    private void Begin(nint foreground)
    {
        _generation++;
        _target = 0;
        RememberTarget(foreground);
        _heard = false;
        _hotBuffers = 0;
        _startedUtc = DateTime.UtcNow;
        _lastVoiceUtc = _startedUtc;

        _mic?.Dispose();
        _mic = AudioCapture.Create();
        _mic.Level += OnMicLevel;
        var error = _mic.Start();
        if (error != null)
        {
            Log.Error(error);
            _mic.Dispose();
            _mic = null;
            _phase = Phase.Idle;
            _window.ShowMessage("No microphone", error, "Check it, then press the shortcut again.", stick: true);
            return;
        }

        _settings.WelcomeDismissed = true;
        _settings.Save();
        _phase = Phase.Listening;
        _window.ShowListening();
        _clock.Start();
    }

    private void RememberTarget(nint hwnd)
    {
        if (hwnd != 0 && !Native.IsCurrentProcess(hwnd))
            _target = hwnd;
    }

    private void OnMicLevel(float level)
    {
        var snapshot = level;
        Dispatcher.UIThread.Post(() =>
        {
            if (_phase != Phase.Listening)
                return;

            _window.PushLevel(snapshot);
            if (snapshot >= VoiceThreshold)
            {
                _hotBuffers++;
                if (_hotBuffers >= 3)
                {
                    _heard = true;
                    _lastVoiceUtc = DateTime.UtcNow;
                }
            }
            else
            {
                _hotBuffers = 0;
            }
        });
    }

    private void OnClock()
    {
        if (_phase != Phase.Listening)
            return;

        var elapsed = DateTime.UtcNow - _startedUtc;
        _window.SetElapsed(elapsed);

        if (!_heard && elapsed >= StartTimeout)
        {
            Finish(insert: false);
            _window.ShowMessage("Didn't hear anything", "Move closer to the microphone and try again.", null, stick: false);
            return;
        }

        if (_heard && DateTime.UtcNow - _lastVoiceUtc >= SilenceGap)
            Finish(insert: true);
        else if (elapsed >= MaxDuration)
            Finish(insert: true);
    }

    private void Finish(bool insert)
    {
        if (_phase != Phase.Listening)
            return;

        var generation = _generation;
        _phase = insert ? Phase.Working : Phase.Idle;
        _clock.Stop();

        var mic = _mic;
        _mic = null;
        mic?.Stop();
        var pcm = mic?.SnapshotPcm16k() ?? Array.Empty<byte>();
        mic?.Dispose();

        var target = _target;
        var now = OperatingSystem.IsWindows() ? Native.GetForegroundWindow() : 0;
        if (now != 0 && !Native.IsCurrentProcess(now))
            target = now;

        if (!insert || pcm.Length < 8_000)
        {
            _phase = Phase.Idle;
            if (!insert)
                _window.HideCard();
            else
                _window.ShowMessage("Didn't hear anything", "That was too short.", null, stick: false);
            return;
        }

        _window.ShowWorking("Transcribing", "Typing it where the cursor is.", busy: true, percent: null);
        _insertCts?.Cancel();
        _insertCts = new CancellationTokenSource();
        _ = InsertAsync(generation, pcm, target, _insertCts.Token);
    }

    private void CancelWork()
    {
        _generation++;
        _insertCts?.Cancel();
        _phase = Phase.Idle;
        _window.HideCard();
    }

    private async Task InsertAsync(int generation, byte[] pcm, nint target, CancellationToken cancellationToken)
    {
        try
        {
            var text = await TranscribeAsync(pcm, cancellationToken);
            await Dispatcher.UIThread.InvokeAsync(() => { });
            if (generation != _generation)
                return;

            if (string.IsNullOrWhiteSpace(text))
            {
                _phase = Phase.Idle;
                _window.ShowMessage("Couldn't understand that", "Try again, a little closer to the microphone.", null, stick: false);
                return;
            }

            var typed = text.EndsWith(' ') || text.EndsWith('\n') ? text : text + " ";
            _window.HideCard();
            var result = await TextInjector.InjectAsync(target, typed);
            if (generation != _generation)
                return;

            _phase = Phase.Idle;
            switch (result)
            {
                case InjectResult.Inserted:
                    break;
                case InjectResult.Copied:
                    var hint = OperatingSystem.IsMacOS()
                        ? "The text is copied. Turn on Accessibility for Dicta, click Restart, or paste with Command + V."
                        : "Couldn't focus that window. Paste with Ctrl + V.";
                    _window.ShowMessage("Copied", text, hint, stick: true);
                    break;
                default:
                    _window.ShowMessage("Couldn't type it", text, "Copy the text and paste it into the field.", stick: true);
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (generation == _generation)
                    _phase = Phase.Idle;
            });
        }
        catch (Exception ex)
        {
            Log.Error("Transcription failed.", ex);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (generation != _generation)
                    return;
                _phase = Phase.Idle;
                _window.ShowMessage("Couldn't transcribe", Describe(ex), null, stick: true);
            });
        }
    }

    private void OnModelProgress(double value)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_phase == Phase.Working)
            {
                if (value < 0)
                    _window.ShowWorking("Preparing voice", "Loading the English model.", busy: true, percent: null);
                else if (value < 100)
                    _window.ShowWorking("Downloading model", "Only the first time. This can take a minute.", busy: false, percent: value);
            }
            else if (_window.Mode == CardMode.Help)
            {
                _window.SetProgress(value);
            }
        });
    }

    private async Task PrepareQuietlyAsync()
    {
        if (_cloud != null)
        {
            try
            {
                await NegotiateModelAsync();
            }
            catch (Exception ex)
            {
                Log.Error("Couldn't pick an OpenAI model.", ex);
            }

            return;
        }

        try
        {
            await _transcriber.PrepareAsync();
        }
        catch (Exception ex)
        {
            Log.Error("Could not prepare the model.", ex);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_phase == Phase.Idle && _window.Mode == CardMode.Help)
                    _window.ShowHelp(
                        "Click a text field and press the shortcut. Speak English.",
                        Describe(ex));
            });
        }
    }

    private static string Describe(Exception ex)
    {
        var current = ex;
        while (current.InnerException != null)
            current = current.InnerException;

        if (current is HttpRequestException)
            return "Couldn't download the English model. Check your connection and press the shortcut again.";

        return string.IsNullOrWhiteSpace(current.Message)
            ? "Something went wrong. Details are in dicta.log."
            : current.Message;
    }

    private enum Phase
    {
        Idle,
        Listening,
        Working,
    }
}
