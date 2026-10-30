using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Dicta.Services;

namespace Dicta;

public enum CardMode
{
    Hidden,
    Help,
    Setup,
    Listening,
    Working,
    Message,
}

public partial class OverlayWindow : Window
{
    private readonly DispatcherTimer _pulse;
    private CancellationTokenSource? _hide;
    private double _pulsePhase;
    private bool _placing;

    public OverlayWindow()
    {
        InitializeComponent();
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Background = Brushes.Transparent;
        PrimaryButton.Click += (_, _) => PrimaryClicked?.Invoke(this, EventArgs.Empty);
        SecondaryButton.Click += (_, _) => SecondaryClicked?.Invoke(this, EventArgs.Empty);
        MicButton.Click += (_, _) => MicrophoneClicked?.Invoke(this, EventArgs.Empty);
        AccessButton.Click += (_, _) => AccessibilityClicked?.Invoke(this, EventArgs.Empty);
        SizeChanged += (_, _) => Place();

        _pulse = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(70) };
        _pulse.Tick += (_, _) => Pulse();
        _pulse.Start();
    }

    public CardMode Mode { get; private set; } = CardMode.Hidden;

    public bool AllowClose { get; set; }

    public event EventHandler? PrimaryClicked;
    public event EventHandler? SecondaryClicked;
    public event EventHandler? MicrophoneClicked;
    public event EventHandler? AccessibilityClicked;
    public event EventHandler? Dismissed;

    public void ShowHelp(string body, string? warning)
    {
        CancelHide();
        Mode = CardMode.Help;
        TitleText.Text = "Dicta is ready";
        SubtitleText.Text = "Running in the tray";
        BadgeText.Text = "EN";
        StatusDot.Fill = BrushOf("#6E62F6");
        StatusDot.Opacity = 1;
        BodyText.Text = body;
        BodyText.IsVisible = true;
        SubtitleText.IsVisible = true;
        HintText.Text = warning ?? "Hiding this card leaves Dicta running.";
        HintText.IsVisible = true;
        Keys.IsVisible = true;
        SetupList.IsVisible = false;
        Wave.IsVisible = false;
        ButtonRow.IsVisible = true;
        PrimaryButton.Content = "Got it";
        PrimaryButton.IsVisible = true;
        SecondaryButton.IsVisible = false;
        SetProgress(100);
        Present();
    }

    public void ShowSetup(bool microphoneAllowed, bool accessibilityAllowed, bool restartAccessibility = false)
    {
        var already = Mode == CardMode.Setup;
        CancelHide();
        Mode = CardMode.Setup;
        TitleText.Text = "Allow Dicta";
        SubtitleText.Text = "Two switches in System Settings";
        SubtitleText.IsVisible = true;
        BadgeText.Text = "EN";
        StatusDot.Fill = BrushOf("#6E62F6");
        StatusDot.Opacity = 1;
        BodyText.IsVisible = false;
        HintText.Text = microphoneAllowed && accessibilityAllowed
            ? "Both are allowed. Click Done."
            : restartAccessibility
                ? "Turn Dicta on in Accessibility, then click Restart."
                : "Turn Dicta on in Accessibility so the text can be pasted. Then click Restart.";
        HintText.IsVisible = true;
        Keys.IsVisible = false;
        Wave.IsVisible = false;
        Progress.IsVisible = false;
        SetupList.IsVisible = true;
        SetPermissionRow(MicState, MicButton, microphoneAllowed);
        SetPermissionRow(AccessState, AccessButton, accessibilityAllowed);
        if (!accessibilityAllowed && restartAccessibility)
            AccessButton.Content = "Restart";
        ButtonRow.IsVisible = true;
        PrimaryButton.Content = "Done";
        PrimaryButton.IsVisible = true;
        SecondaryButton.Content = "Not now";
        SecondaryButton.IsVisible = true;
        if (!already)
            Present();
    }

    public void ShowListening()
    {
        CancelHide();
        Mode = CardMode.Listening;
        TitleText.Text = "Listening";
        SubtitleText.Text = "Speak English";
        BadgeText.Text = "0:00";
        StatusDot.Fill = BrushOf("#FF5A6A");
        BodyText.Text = "";
        BodyText.IsVisible = false;
        SubtitleText.IsVisible = false;
        HintText.IsVisible = false;
        Keys.IsVisible = false;
        SetupList.IsVisible = false;
        Wave.IsVisible = true;
        Wave.Reset();
        ButtonRow.IsVisible = true;
        PrimaryButton.Content = "Insert";
        PrimaryButton.IsVisible = true;
        SecondaryButton.Content = "Cancel";
        SecondaryButton.IsVisible = true;
        Progress.IsVisible = false;
        Present();
    }

    public void PushLevel(float rms) => Wave.Push(rms);

    public void SetElapsed(TimeSpan elapsed)
    {
        if (Mode != CardMode.Listening)
            return;
        BadgeText.Text = $"{(int)elapsed.TotalMinutes}:{elapsed.Seconds:00}";
    }

    public void ShowWorking(string title, string subtitle, bool busy, double? percent)
    {
        var same = Mode == CardMode.Working && TitleText.Text == title && SubtitleText.Text == subtitle;
        CancelHide();
        Mode = CardMode.Working;
        TitleText.Text = title;
        SubtitleText.Text = subtitle;
        SubtitleText.IsVisible = true;
        BadgeText.Text = "EN";
        StatusDot.Fill = BrushOf("#8B7CFF");
        StatusDot.Opacity = 1;
        BodyText.IsVisible = false;
        HintText.IsVisible = false;
        Keys.IsVisible = false;
        SetupList.IsVisible = false;
        Wave.IsVisible = false;
        ButtonRow.IsVisible = false;
        Progress.IsVisible = true;
        Progress.IsIndeterminate = busy || percent == null;
        if (percent != null)
            Progress.Value = percent.Value;
        if (!same)
            Present();
    }

    public void ShowMessage(string title, string body, string? hint, bool stick)
    {
        CancelHide();
        Mode = CardMode.Message;
        TitleText.Text = title;
        SubtitleText.Text = "";
        SubtitleText.IsVisible = false;
        BadgeText.Text = "EN";
        StatusDot.Fill = BrushOf(title == "Done" ? "#3DDC97" : "#FF5A6A");
        StatusDot.Opacity = 1;
        BodyText.Text = body;
        BodyText.IsVisible = !string.IsNullOrWhiteSpace(body);
        HintText.Text = hint ?? "";
        HintText.IsVisible = !string.IsNullOrWhiteSpace(hint);
        Keys.IsVisible = false;
        SetupList.IsVisible = false;
        Wave.IsVisible = false;
        Progress.IsVisible = false;
        ButtonRow.IsVisible = stick;
        PrimaryButton.Content = "Close";
        PrimaryButton.IsVisible = stick;
        SecondaryButton.IsVisible = false;
        Present();
        if (!stick)
            ArmHide(title == "Done" ? TimeSpan.FromMilliseconds(1400) : TimeSpan.FromMilliseconds(2600));
    }

    public void SetProgress(double value)
    {
        if (Mode != CardMode.Help && Mode != CardMode.Working)
            return;

        if (value >= 100)
        {
            Progress.IsVisible = false;
            return;
        }

        Progress.IsVisible = true;
        Progress.IsIndeterminate = value < 0;
        if (value >= 0)
            Progress.Value = value;
    }

    public void HideCard()
    {
        CancelHide();
        Mode = CardMode.Hidden;
        if (IsVisible)
            Hide();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        Native.MakeToolWindow(this);
        Place();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key != Key.Escape)
            return;
        Dismissed?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!AllowClose)
        {
            e.Cancel = true;
            HideCard();
            Dismissed?.Invoke(this, EventArgs.Empty);
            return;
        }

        base.OnClosing(e);
    }

    private void Present()
    {
        ShowActivated = false;
        if (!IsVisible)
            Show();
        Dispatcher.UIThread.Post(Place, DispatcherPriority.Background);
    }

    private void Place()
    {
        if (_placing || !IsVisible)
            return;

        var screen = Screens?.ScreenFromWindow(this) ?? Screens?.Primary;
        if (screen == null || Bounds.Width < 10 || Bounds.Height < 10)
            return;

        _placing = true;
        var area = screen.WorkingArea;
        var scale = screen.Scaling;
        var width = (int)Math.Ceiling(Bounds.Width * scale);
        var height = (int)Math.Ceiling(Bounds.Height * scale);
        Position = new PixelPoint(
            area.X + Math.Max(0, (area.Width - width) / 2),
            area.Y + area.Height - height - (int)(18 * scale));
        _placing = false;
    }

    private void Pulse()
    {
        if (Mode != CardMode.Listening)
            return;

        _pulsePhase += 0.22;
        StatusDot.Opacity = 0.35 + 0.65 * (0.5 + 0.5 * Math.Sin(_pulsePhase));
    }

    private void ArmHide(TimeSpan delay)
    {
        _hide?.Cancel();
        var source = new CancellationTokenSource();
        _hide = source;
        _ = Task.Delay(delay, source.Token).ContinueWith(task =>
        {
            if (task.IsCanceled)
                return;
            Dispatcher.UIThread.Post(() =>
            {
                if (!source.IsCancellationRequested && Mode == CardMode.Message)
                    HideCard();
            });
        }, TaskScheduler.Default);
    }

    private void CancelHide()
    {
        _hide?.Cancel();
        _hide = null;
    }

    private static void SetPermissionRow(TextBlock state, Button button, bool allowed)
    {
        state.Text = allowed ? "Allowed" : "Needed";
        state.Foreground = BrushOf(allowed ? "#3DDC97" : "#FF8A96");
        button.Content = allowed ? "Open" : "Allow";
    }

    private static SolidColorBrush BrushOf(string hex) => new(Color.Parse(hex));
}
