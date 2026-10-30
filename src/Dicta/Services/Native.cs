using System.Runtime.InteropServices;
using System.Text;
using Avalonia.Controls;

namespace Dicta.Services;

static class Native
{
    public const uint WaveMapper = 0xFFFFFFFF;
    public const uint CallbackFunction = 0x00030000;
    public const uint WimData = 0x3C0;
    public const int WmHotkey = 0x0312;
    public const int WmQuit = 0x0012;
    public const uint GmemMoveable = 0x0002;
    public const uint CfUnicodeText = 13;
    public const uint InputKeyboard = 1;
    public const uint KeyeventfKeyup = 0x0002;
    public const uint KeyeventfUnicode = 0x0004;
    public const ushort VkControl = 0x11;
    public const ushort VkReturn = 0x0D;

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate void WaveInProc(nint hWaveIn, uint uMsg, nint dwInstance, nint dwParam1, nint dwParam2);

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct WaveFormatEx
    {
        public ushort FormatTag;
        public ushort Channels;
        public uint SamplesPerSec;
        public uint AvgBytesPerSec;
        public ushort BlockAlign;
        public ushort BitsPerSample;
        public ushort Size;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WaveHdr
    {
        public nint Data;
        public uint BufferLength;
        public uint BytesRecorded;
        public nint User;
        public uint Flags;
        public uint Loops;
        public nint Next;
        public nint Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Msg
    {
        public nint Hwnd;
        public uint Message;
        public nuint WParam;
        public nint LParam;
        public uint Time;
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MouseInput
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nint Extra;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KeyboardInput
    {
        public ushort Vk;
        public ushort Scan;
        public uint Flags;
        public uint Time;
        public nint Extra;
    }

    // The Win32 INPUT union is the size of MOUSEINPUT (the full struct is 40 bytes on x64).
    [StructLayout(LayoutKind.Explicit)]
    public struct InputUnion
    {
        [FieldOffset(0)] public MouseInput Mouse;
        [FieldOffset(0)] public KeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Input
    {
        public uint Type;
        public InputUnion Union;
    }

    [DllImport("winmm.dll")]
    public static extern uint waveInOpen(out nint handle, uint deviceId, ref WaveFormatEx format, WaveInProc callback, nint instance, uint flags);

    [DllImport("winmm.dll")]
    public static extern uint waveInPrepareHeader(nint handle, nint header, int size);

    [DllImport("winmm.dll")]
    public static extern uint waveInUnprepareHeader(nint handle, nint header, int size);

    [DllImport("winmm.dll")]
    public static extern uint waveInAddBuffer(nint handle, nint header, int size);

    [DllImport("winmm.dll")]
    public static extern uint waveInStart(nint handle);

    [DllImport("winmm.dll")]
    public static extern uint waveInReset(nint handle);

    [DllImport("winmm.dll")]
    public static extern uint waveInClose(nint handle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern nint CreateWindowEx(uint exStyle, string className, string windowName, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

    [DllImport("user32.dll")]
    public static extern bool DestroyWindow(nint hwnd);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern nint GetModuleHandle(string? name);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint key);

    [DllImport("user32.dll")]
    public static extern bool UnregisterHotKey(nint hwnd, int id);

    [DllImport("user32.dll")]
    public static extern int GetMessage(out Msg msg, nint hwnd, uint filterMin, uint filterMax);

    [DllImport("user32.dll")]
    public static extern bool TranslateMessage(ref Msg msg);

    [DllImport("user32.dll")]
    public static extern nint DispatchMessage(ref Msg msg);

    [DllImport("user32.dll")]
    public static extern bool PostThreadMessage(uint threadId, int msg, nuint wParam, nint lParam);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    public static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(nint hwnd);

    [DllImport("user32.dll")]
    public static extern bool BringWindowToTop(nint hwnd);

    [DllImport("user32.dll")]
    public static extern bool AttachThreadInput(uint attach, uint attachTo, bool attachFlag);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint count, Input[] inputs, int size);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    public static extern nint GetWindowLongPtr(nint hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    public static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);

    [DllImport("user32.dll")]
    public static extern bool OpenClipboard(nint owner);

    [DllImport("user32.dll")]
    public static extern bool CloseClipboard();

    [DllImport("user32.dll")]
    public static extern bool EmptyClipboard();

    [DllImport("user32.dll")]
    public static extern uint EnumClipboardFormats(uint format);

    [DllImport("user32.dll")]
    public static extern nint GetClipboardData(uint format);

    [DllImport("user32.dll")]
    public static extern nint SetClipboardData(uint format, nint memory);

    [DllImport("kernel32.dll")]
    public static extern nint GlobalAlloc(uint flags, nuint bytes);

    [DllImport("kernel32.dll")]
    public static extern nint GlobalLock(nint memory);

    [DllImport("kernel32.dll")]
    public static extern bool GlobalUnlock(nint memory);

    [DllImport("kernel32.dll")]
    public static extern nuint GlobalSize(nint memory);

    public static void MakeToolWindow(Window window)
    {
        if (!OperatingSystem.IsWindows())
            return;

        var hwnd = window.TryGetPlatformHandle()?.Handle ?? 0;
        if (hwnd == 0)
            return;

        const int gwlExStyle = -20;
        const nint wsExToolWindow = 0x00000080;
        const nint wsExAppWindow = 0x00040000;
        var style = GetWindowLongPtr(hwnd, gwlExStyle);
        style |= wsExToolWindow;
        style &= ~wsExAppWindow;
        SetWindowLongPtr(hwnd, gwlExStyle, style);
    }

    public static bool IsCurrentProcess(nint hwnd)
    {
        if (hwnd == 0)
            return false;
        GetWindowThreadProcessId(hwnd, out var pid);
        return pid == (uint)Environment.ProcessId;
    }

    public static bool ForceForeground(nint hwnd)
    {
        if (hwnd == 0)
            return false;

        var foreground = GetForegroundWindow();
        var currentThread = GetCurrentThreadId();
        var foregroundThread = GetWindowThreadProcessId(foreground, out _);
        var targetThread = GetWindowThreadProcessId(hwnd, out _);

        var attachedForeground = false;
        var attachedTarget = false;
        try
        {
            if (foregroundThread != 0 && foregroundThread != currentThread)
                attachedForeground = AttachThreadInput(currentThread, foregroundThread, true);
            if (targetThread != 0 && targetThread != currentThread && targetThread != foregroundThread)
                attachedTarget = AttachThreadInput(currentThread, targetThread, true);

            SetForegroundWindow(hwnd);
            BringWindowToTop(hwnd);
        }
        finally
        {
            if (attachedTarget)
                AttachThreadInput(currentThread, targetThread, false);
            if (attachedForeground)
                AttachThreadInput(currentThread, foregroundThread, false);
        }

        return GetForegroundWindow() == hwnd;
    }

    public static bool ClipboardIsTextOnly()
    {
        if (!TryOpenClipboard())
            return false;

        try
        {
            uint format = 0;
            while ((format = EnumClipboardFormats(format)) != 0)
            {
                // CF_TEXT, CF_OEMTEXT, CF_UNICODETEXT, and CF_LOCALE can be replaced for a moment.
                if (format is 1 or 7 or 13 or 16)
                    continue;
                return false;
            }

            return true;
        }
        finally
        {
            CloseClipboard();
        }
    }

    public static string? ReadClipboardText()
    {
        if (!TryOpenClipboard())
            return null;

        try
        {
            var handle = GetClipboardData(CfUnicodeText);
            if (handle == 0)
                return null;

            var pointer = GlobalLock(handle);
            if (pointer == 0)
                return null;

            try
            {
                var size = (int)GlobalSize(handle);
                if (size <= 1)
                    return "";
                var bytes = new byte[size];
                Marshal.Copy(pointer, bytes, 0, size);
                var text = Encoding.Unicode.GetString(bytes);
                var end = text.IndexOf('\0');
                return end >= 0 ? text[..end] : text;
            }
            finally
            {
                GlobalUnlock(handle);
            }
        }
        finally
        {
            CloseClipboard();
        }
    }

    public static bool WriteClipboardText(string text)
    {
        var bytes = Encoding.Unicode.GetBytes(text + "\0");
        if (!TryOpenClipboard())
            return false;

        try
        {
            if (!EmptyClipboard())
                return false;

            var memory = GlobalAlloc(GmemMoveable, (nuint)bytes.Length);
            if (memory == 0)
                return false;

            var pointer = GlobalLock(memory);
            if (pointer == 0)
                return false;

            Marshal.Copy(bytes, 0, pointer, bytes.Length);
            GlobalUnlock(memory);

            if (SetClipboardData(CfUnicodeText, memory) == 0)
                return false;

            return true;
        }
        finally
        {
            CloseClipboard();
        }
    }

    public static void ClearClipboard()
    {
        if (!TryOpenClipboard())
            return;
        try
        {
            EmptyClipboard();
        }
        finally
        {
            CloseClipboard();
        }
    }

    public static void SendCtrlV()
    {
        var inputs = new[]
        {
            KeyDown(VkControl),
            KeyDown(0x56),
            KeyUp(0x56),
            KeyUp(VkControl),
        };
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
    }

    public static void TypeUnicode(string text)
    {
        var batch = new List<Input>(80);
        foreach (var ch in text)
        {
            if (ch == '\r')
                continue;

            if (ch == '\n')
            {
                Flush(batch);
                var enter = new[] { KeyDown(VkReturn), KeyUp(VkReturn) };
                SendInput((uint)enter.Length, enter, Marshal.SizeOf<Input>());
                continue;
            }

            batch.Add(Unicode(ch, up: false));
            batch.Add(Unicode(ch, up: true));
            if (batch.Count >= 60)
                Flush(batch);
        }

        Flush(batch);
    }

    private static void Flush(List<Input> batch)
    {
        if (batch.Count == 0)
            return;
        SendInput((uint)batch.Count, batch.ToArray(), Marshal.SizeOf<Input>());
        batch.Clear();
    }

    private static Input KeyDown(ushort vk) => new()
    {
        Type = InputKeyboard,
        Union = new InputUnion { Keyboard = new KeyboardInput { Vk = vk } },
    };

    private static Input KeyUp(ushort vk) => new()
    {
        Type = InputKeyboard,
        Union = new InputUnion { Keyboard = new KeyboardInput { Vk = vk, Flags = KeyeventfKeyup } },
    };

    private static Input Unicode(char value, bool up) => new()
    {
        Type = InputKeyboard,
        Union = new InputUnion
        {
            Keyboard = new KeyboardInput
            {
                Scan = value,
                Flags = KeyeventfUnicode | (up ? KeyeventfKeyup : 0),
            },
        },
    };

    private static bool TryOpenClipboard()
    {
        for (var i = 0; i < 10; i++)
        {
            if (OpenClipboard(0))
                return true;
            Thread.Sleep(20);
        }

        return false;
    }
}
