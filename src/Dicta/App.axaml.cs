using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Threading;
using Dicta.Services;

namespace Dicta;

public partial class App : Application
{
    private OverlayWindow? _window;
    private DictationController? _controller;
    private TrayIcon? _tray;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            desktop.Exit += (_, _) => ShutdownApp();

            _window = new OverlayWindow();
            _controller = new DictationController(_window);
            _window.PrimaryClicked += (_, _) => _controller.OnPrimary();
            _window.SecondaryClicked += (_, _) => _controller.OnSecondary();
            _window.Dismissed += (_, _) => _controller.OnDismiss();

            SetupTray(desktop);
            _controller.Start();
            MacPermissions.PromptAccessibility();

            if (!OperatingSystem.IsWindows())
            {
                InstanceBus.Bind(command =>
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (command == "help")
                            _controller?.ShowHelp();
                        else
                            _controller?.ToggleFromTray();
                    });
                });
            }

            if (Program.ShowSignal != null)
            {
                var signal = Program.ShowSignal;
                _ = Task.Run(() =>
                {
                    while (signal.WaitOne())
                    {
                        Dispatcher.UIThread.Post(() => _controller?.ShowHelp());
                    }
                });
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void SetupTray(IClassicDesktopStyleApplicationLifetime desktop)
    {
        WindowIcon? icon = null;
        try
        {
            icon = new WindowIcon(AssetLoader.Open(new Uri("avares://Dicta/Assets/icon.png")));
        }
        catch (Exception ex)
        {
            Log.Error("Could not load the icon.", ex);
        }

        var dictate = new NativeMenuItem("Dictate now");
        dictate.Click += (_, _) => _controller?.ToggleFromTray();

        var help = new NativeMenuItem("How it works");
        help.Click += (_, _) => _controller?.ShowHelp();

        var quit = new NativeMenuItem("Quit");
        quit.Click += (_, _) =>
        {
            _window!.AllowClose = true;
            _controller?.Dispose();
            desktop.Shutdown();
        };

        var menu = new NativeMenu();
        menu.Items.Add(dictate);
        menu.Items.Add(help);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(quit);

        _tray = new TrayIcon
        {
            Icon = icon,
            ToolTipText = "Dicta  ·  " + Shortcut.Label,
            Menu = menu,
        };
        _tray.Clicked += (_, _) => _controller?.ShowHelp();

        TrayIcon.SetIcons(this, new TrayIcons { _tray });
    }

    private void ShutdownApp()
    {
        _controller?.Dispose();
        HotkeyService.Stop();
        InstanceBus.Stop();
        _tray?.Dispose();
    }
}
