using Avalonia;
using System.Threading;

namespace Dicta;

sealed class Program
{
    public static Mutex? InstanceMutex { get; private set; }
    public static EventWaitHandle? ShowSignal { get; private set; }

    [STAThread]
    public static void Main(string[] args)
    {
        if (!OperatingSystem.IsWindows())
        {
            if (!InstanceBus.TakeOwnership(args))
                return;

            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, Avalonia.Controls.ShutdownMode.OnExplicitShutdown);
            return;
        }

        ShowSignal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\Dicta.Show");
        InstanceMutex = new Mutex(false, @"Local\Dicta.Mutex");

        var ownsMutex = false;
        try
        {
            ownsMutex = InstanceMutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            ownsMutex = true;
        }

        if (!ownsMutex)
        {
            ShowSignal.Set();
            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, Avalonia.Controls.ShutdownMode.OnExplicitShutdown);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
