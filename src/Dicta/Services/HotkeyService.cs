namespace Dicta.Services;

static class HotkeyService
{
    private static Thread? _thread;
    private static uint _threadId;
    private static nint _hwnd;
    private static volatile bool _registered;
    private static readonly ManualResetEventSlim Ready = new(false);

    public static event Action? Pressed;

    public static void Raise() => Pressed?.Invoke();

    public static bool Start()
    {
        if (!OperatingSystem.IsWindows())
            return UnixHotkey.Start();

        _thread = new Thread(Loop)
        {
            IsBackground = true,
            Name = "DictaHotkey",
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        Ready.Wait(TimeSpan.FromSeconds(3));
        return _registered;
    }

    public static void Stop()
    {
        if (!OperatingSystem.IsWindows())
        {
            UnixHotkey.Stop();
            return;
        }

        if (_threadId != 0)
            Native.PostThreadMessage(_threadId, Native.WmQuit, 0, 0);
    }

    private static void Loop()
    {
        try
        {
            _threadId = Native.GetCurrentThreadId();
            _hwnd = Native.CreateWindowEx(0, "STATIC", "DictaHotkey", 0, 0, 0, 0, 0, 0, 0, Native.GetModuleHandle(null), 0);
            _registered = _hwnd != 0 && Native.RegisterHotKey(_hwnd, 1, Shortcut.Modifiers, Shortcut.Key);
            if (!_registered)
                Log.Error("Couldn't register the shortcut. Code " + System.Runtime.InteropServices.Marshal.GetLastWin32Error());
        }
        finally
        {
            Ready.Set();
        }

        if (_hwnd == 0)
            return;

        while (Native.GetMessage(out var message, 0, 0, 0) > 0)
        {
            if (message.Message == Native.WmHotkey)
                Pressed?.Invoke();
            Native.TranslateMessage(ref message);
            Native.DispatchMessage(ref message);
        }

        Native.UnregisterHotKey(_hwnd, 1);
        Native.DestroyWindow(_hwnd);
    }
}
