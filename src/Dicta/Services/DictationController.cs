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
    private readonly SettingsStore _settings = new();
    private readonly DispatcherTimer _clock;
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
        _transcriber.Progress += OnModelProgress;
    }

    public void Start()
    {
        if (_started)
            return;
        _started = true;

        _settings.Load();
        _hotkeyOk = HotkeyService.Start();
        HotkeyService.Pressed += OnHotkey;
        if (!_settings.WelcomeDismissed || !_hotkeyOk)
            ShowHelp();

        _ = PrepareQuietlyAsync();
    }

    public void ShowHelp()
    {
        if (_phase != Phase.Idle)
            return;

        var body = "Click a text field and press the shortcut. Speak English. After a short pause, the text is typed there.";
        string? warning = null;
        if (!_hotkeyOk)
            warning = OperatingSystem.IsWindows()
                ? "Another app is using that shortcut. You can dictate from the tray icon."
                : "The shortcut isn't available. Dictate from the tray, or run dicta in a terminal.";
        else if (!_transcriber.ModelExists)
            warning = "The first launch downloads the English model, about 140 MB. Internet is only needed that once.";
        else if (!_settings.WelcomeDismissed && OperatingSystem.IsMacOS())
            warning = "Allow Dicta in Privacy & Security for Microphone and Accessibility.";
        else if (!_settings.WelcomeDismissed && UnixPaste.SetupHint() is { } hint)
            warning = hint;

        _window.ShowHelp(body, warning);
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
            case CardMode.Listening:
                Finish(insert: true);
                break;
            case CardMode.Message:
                _window.HideCard();
                _phase = Phase.Idle;
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
        HotkeyService.Pressed -= OnHotkey;
        _mic?.Dispose();
        _transcriber.Dispose();
    }

    private void OnHotkey()
    {
        var foreground = OperatingSystem.IsWindows() ? Native.GetForegroundWindow() : 0;
        Dispatcher.UIThread.Post(() => Toggle(foreground));
    }

    private void Toggle(nint foreground)
    {
        if (_phase == Phase.Working)
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
            var text = await _transcriber.TranscribeAsync(pcm, cancellationToken);
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
                    var paste = OperatingSystem.IsMacOS() ? "Command + V" : "Ctrl + V";
                _window.ShowMessage("Copied", text, "Couldn't focus that window. Paste with " + paste + ".", stick: true);
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
