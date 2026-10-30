using System.Runtime.InteropServices;
using SharpHook;
using SharpHook.Native;

namespace Dicta.Services;

static class UnixHotkey
{
    private const uint EventClassKeyboard = 0x6B657962;
    private const uint EventHotKeyPressed = 5;
    private const uint ControlAndShift = 4096 | 512;
    private const uint SpaceKey = 49;
    private const string Toolbox = "/System/Library/Frameworks/Carbon.framework/Frameworks/HIToolbox.framework/HIToolbox";

    private static SimpleGlobalHook? _hook;
    private static bool _ctrl;
    private static bool _shift;
    private static long _lastMs;
    private static nint _carbonHotKey;
    private static nint _carbonHandler;
    private static CarbonHotKeyProc? _carbonProc;

    public static bool Start()
    {
        if (OperatingSystem.IsMacOS() && StartCarbon())
            return true;

        return StartHook();
    }

    public static void Stop()
    {
        StopCarbon();
        StopHook();
    }

    private static bool StartCarbon()
    {
        try
        {
            _carbonProc = OnCarbonHotKey;
            var spec = new EventTypeSpec { EventClass = EventClassKeyboard, EventKind = EventHotKeyPressed };
            var installed = InstallEventHandler(GetApplicationEventTarget(), Marshal.GetFunctionPointerForDelegate(_carbonProc), 1, ref spec, 0, out _carbonHandler);
            if (installed != 0)
            {
                Log.Error("Couldn't install the shortcut handler. Code " + installed);
                return false;
            }

            var id = new EventHotKeyId { Signature = 0x44637461, Id = 1 };
            var registered = RegisterEventHotKey(SpaceKey, ControlAndShift, id, GetApplicationEventTarget(), 0, out _carbonHotKey);
            if (registered != 0 || _carbonHotKey == 0)
            {
                Log.Error("Couldn't register Ctrl + Shift + Space. Code " + registered);
                StopCarbon();
                return false;
            }

            Log.Info("Shortcut registered.");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Couldn't register the shortcut.", ex);
            StopCarbon();
            return false;
        }
    }

    private static void StopCarbon()
    {
        if (_carbonHotKey != 0)
        {
            UnregisterEventHotKey(_carbonHotKey);
            _carbonHotKey = 0;
        }

        if (_carbonHandler != 0)
        {
            RemoveEventHandler(_carbonHandler);
            _carbonHandler = 0;
        }
    }

    private static int OnCarbonHotKey(nint next, nint evt, nint user)
    {
        Raise();
        return 0;
    }

    private static bool StartHook()
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

    private static void StopHook()
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
            Raise();
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

    private static void Raise()
    {
        var now = Environment.TickCount64;
        if (now - _lastMs < 400)
            return;
        _lastMs = now;
        HotkeyService.Raise();
    }

    private static bool IsCtrl(KeyCode key) => key is KeyCode.VcLeftControl or KeyCode.VcRightControl;

    private static bool IsShift(KeyCode key) => key is KeyCode.VcLeftShift or KeyCode.VcRightShift;

    [StructLayout(LayoutKind.Sequential)]
    private struct EventTypeSpec
    {
        public uint EventClass;
        public uint EventKind;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct EventHotKeyId
    {
        public uint Signature;
        public uint Id;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int CarbonHotKeyProc(nint next, nint evt, nint user);

    [DllImport(Toolbox)]
    private static extern nint GetApplicationEventTarget();

    [DllImport(Toolbox)]
    private static extern int InstallEventHandler(nint target, nint handler, uint count, ref EventTypeSpec types, nint user, out nint installed);

    [DllImport(Toolbox)]
    private static extern int RemoveEventHandler(nint handler);

    [DllImport(Toolbox)]
    private static extern int RegisterEventHotKey(uint keyCode, uint modifiers, EventHotKeyId id, nint target, uint options, out nint hotKey);

    [DllImport(Toolbox)]
    private static extern int UnregisterEventHotKey(nint hotKey);
}
