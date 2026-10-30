using SharpHook;
using SharpHook.Native;

namespace Dicta.Services;

static class UnixHotkey
{
    private static SimpleGlobalHook? _hook;
    private static bool _ctrl;
    private static bool _shift;
    private static long _lastMs;

    public static bool Start()
    {
        try
        {
            _hook = new SimpleGlobalHook(GlobalHookType.Keyboard, runAsyncOnBackgroundThread: true);
            _hook.KeyPressed += OnPressed;
            _hook.KeyReleased += OnReleased;
            _hook.RunAsync();
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Couldn't listen for the shortcut.", ex);
            return false;
        }
    }

    public static void Stop()
    {
        var hook = _hook;
        _hook = null;
        if (hook == null)
            return;

        try
        {
            hook.Dispose();
        }
        catch (Exception ex)
        {
            Log.Error("Couldn't stop the shortcut.", ex);
        }
    }

    private static void OnPressed(object? sender, KeyboardHookEventArgs e)
    {
        var key = e.Data.KeyCode;
        if (IsCtrl(key))
            _ctrl = true;
        else if (IsShift(key))
            _shift = true;
        else if (key == KeyCode.VcSpace && _ctrl && _shift)
        {
            e.SuppressEvent = true;
            var now = Environment.TickCount64;
            if (now - _lastMs < 400)
                return;
            _lastMs = now;
            HotkeyService.Raise();
        }
    }

    private static void OnReleased(object? sender, KeyboardHookEventArgs e)
    {
        var key = e.Data.KeyCode;
        if (IsCtrl(key))
            _ctrl = false;
        else if (IsShift(key))
            _shift = false;
    }

    private static bool IsCtrl(KeyCode key) => key is KeyCode.VcLeftControl or KeyCode.VcRightControl;

    private static bool IsShift(KeyCode key) => key is KeyCode.VcLeftShift or KeyCode.VcRightShift;
}
