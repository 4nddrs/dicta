using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia.Threading;

namespace Dicta.Services;

static class UnixPaste
{
    public static InjectResult Inject(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return InjectResult.Failed;

        Thread.Sleep(OperatingSystem.IsMacOS() ? 280 : 120);
        var previous = ReadClipboard();
        if (!WriteClipboard(text))
            return InjectResult.Failed;

        if (!TryPaste())
            return InjectResult.Copied;

        Thread.Sleep(280);
        if (previous == null)
            ClearClipboard();
        else
            WriteClipboard(previous);

        return InjectResult.Inserted;
    }

    public static string? SetupHint()
    {
        if (!OperatingSystem.IsLinux())
            return null;

        var wayland = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));
        if (wayland && (!OnPath("wtype") || !OnPath("wl-copy")))
            return "Install wtype and wl-clipboard so Dicta can type. Until then, the text is copied.";
        if (!wayland && (!OnPath("xdotool") || (!OnPath("xclip") && !OnPath("xsel"))))
            return "Install xdotool and xclip so Dicta can type. Until then, the text is copied.";
        return null;
    }

    private static bool TryPaste()
    {
        if (OperatingSystem.IsMacOS())
            return PasteMac();

        var wayland = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));
        if (wayland && OnPath("wtype") && Run("wtype", "-M ctrl -k v -m ctrl"))
            return true;
        return OnPath("xdotool") && Run("xdotool", "key --clearmodifiers ctrl+v");
    }

    private static bool PasteMac()
    {
        if (AccessibilityTrusted())
        {
            PostCommandV();
            return true;
        }

        return PasteViaSystemEvents();
    }

    private static void PostCommandV()
    {
        const ushort keyV = 0x09;
        const ulong command = 0x100000;
        PostKey(keyV, true, command);
        Thread.Sleep(20);
        PostKey(keyV, false, command);
    }

    private static bool PasteViaSystemEvents()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "osascript",
                ArgumentList = { "-e", "tell application \"System Events\" to keystroke \"v\" using command down" },
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (process == null || !process.WaitForExit(20000))
                return false;
            if (process.ExitCode == 0)
                return true;

            var error = process.StandardError.ReadToEnd();
            Log.Error("Couldn't paste into the focused app. " + error.Trim());
            return false;
        }
        catch (Exception ex)
        {
            Log.Error("Couldn't paste into the focused app.", ex);
            return false;
        }
    }

    private static void PostKey(ushort key, bool down, ulong flags)
    {
        var evt = CGEventCreateKeyboardEvent(0, key, down);
        if (evt == 0)
            return;
        CGEventSetFlags(evt, flags);
        CGEventPost(0, evt);
        CFRelease(evt);
    }

    private static bool AccessibilityTrusted()
    {
        try
        {
            if (Dispatcher.UIThread.CheckAccess())
                return AXIsProcessTrusted();

            var pending = Dispatcher.UIThread.InvokeAsync(AXIsProcessTrusted);
            pending.Wait(TimeSpan.FromSeconds(1));
            return pending.Status == DispatcherOperationStatus.Completed && pending.Result;
        }
        catch
        {
            return false;
        }
    }

    private static string? ReadClipboard()
    {
        if (OperatingSystem.IsMacOS())
            return Capture("pbpaste", "");
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")) && OnPath("wl-paste"))
            return Capture("wl-paste", "--no-newline");
        if (OnPath("xclip"))
            return Capture("xclip", "-selection clipboard -o");
        if (OnPath("xsel"))
            return Capture("xsel", "--clipboard --output");
        return null;
    }

    private static bool WriteClipboard(string text)
    {
        if (OperatingSystem.IsMacOS())
            return WriteStdin("pbcopy", "", text);
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")) && OnPath("wl-copy"))
            return WriteStdin("wl-copy", "", text);
        if (OnPath("xclip"))
            return WriteStdin("xclip", "-selection clipboard", text);
        if (OnPath("xsel"))
            return WriteStdin("xsel", "--clipboard --input", text);
        return false;
    }

    private static void ClearClipboard()
    {
        if (OperatingSystem.IsMacOS())
        {
            WriteStdin("pbcopy", "", "");
            return;
        }

        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")) && OnPath("wl-copy"))
        {
            Run("wl-copy", "--clear");
            return;
        }

        WriteClipboard("");
    }

    private static string? Capture(string file, string args)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(file, args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
            });
            if (process == null)
                return null;
            var text = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(2000) || process.ExitCode != 0)
                return null;
            return text;
        }
        catch (Exception ex)
        {
            Log.Error("Couldn't read the clipboard.", ex);
            return null;
        }
    }

    private static bool WriteStdin(string file, string args, string text)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(file, args)
            {
                RedirectStandardInput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardInputEncoding = Encoding.UTF8,
            });
            if (process == null)
                return false;
            process.StandardInput.Write(text);
            process.StandardInput.Close();
            return process.WaitForExit(2000) && process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            Log.Error("Couldn't write the clipboard.", ex);
            return false;
        }
    }

    private static bool Run(string file, string args)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(file, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
            });
            if (process == null)
                return false;
            _ = process.StandardError.ReadToEndAsync();
            return process.WaitForExit(2000) && process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            Log.Error("Couldn't type the text.", ex);
            return false;
        }
    }

    private static bool OnPath(string name)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path))
            return false;

        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            if (File.Exists(Path.Combine(dir, name)))
                return true;
        }

        return false;
    }

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern nint CGEventCreateKeyboardEvent(nint source, ushort key, bool keyDown);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern void CGEventSetFlags(nint evt, ulong flags);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern void CGEventPost(uint tap, nint evt);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern void CFRelease(nint value);

    [DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool AXIsProcessTrusted();
}
