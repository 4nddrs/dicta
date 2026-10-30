using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Dicta;

static class MacPermissions
{
    private const int BlockIsGlobal = 1 << 28;
    private const int BlockHasSignature = 1 << 30;

    private static readonly MicGrantBlock MicGrant = OnMicrophoneGrant;
    private static nint _micBlock;
    private static Action<bool>? _micDone;

    public static bool Ready()
    {
        return !OperatingSystem.IsMacOS() || (MicrophoneAllowed() && AccessibilityAllowed());
    }

    public static bool MicrophoneAllowed()
    {
        return MicrophoneStatus() == 3;
    }

    public static bool AccessibilityAllowed()
    {
        if (!OperatingSystem.IsMacOS())
            return true;

        try
        {
            return AXIsProcessTrusted();
        }
        catch
        {
            return false;
        }
    }

    public static void AllowMicrophone(Action<bool> completed)
    {
        if (!OperatingSystem.IsMacOS())
        {
            completed(true);
            return;
        }

        if (MicrophoneAllowed())
        {
            completed(true);
            return;
        }

        if (RequestMicrophone(completed))
            return;

        Log.Error("Couldn't show the microphone prompt.");
        OpenPrivacy("Privacy_Microphone");
        completed(false);
    }

    public static void AllowAccessibility()
    {
        if (!OperatingSystem.IsMacOS())
            return;

        if (!AccessibilityAllowed())
            PromptAccessibility();

        OpenPrivacy("Privacy_Accessibility");
    }

    public static void PromptAccessibility()
    {
        if (!OperatingSystem.IsMacOS())
            return;

        try
        {
            var coreFoundation = dlopen("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation", 1);
            var keyCallbacks = dlsym(coreFoundation, "kCFTypeDictionaryKeyCallBacks");
            var valueCallbacks = dlsym(coreFoundation, "kCFTypeDictionaryValueCallBacks");
            var booleanTrueSymbol = dlsym(coreFoundation, "kCFBooleanTrue");
            if (coreFoundation == 0 || keyCallbacks == 0 || valueCallbacks == 0 || booleanTrueSymbol == 0)
                return;

            var booleanTrue = Marshal.ReadIntPtr(booleanTrueSymbol);
            var key = CFStringCreateWithCString(0, "AXTrustedCheckOptionPrompt", 0x08000100);
            if (key == 0)
                return;
            var keys = new[] { key };
            var values = new[] { booleanTrue };
            var keysPin = GCHandle.Alloc(keys, GCHandleType.Pinned);
            var valuesPin = GCHandle.Alloc(values, GCHandleType.Pinned);
            try
            {
                var options = CFDictionaryCreate(0, keysPin.AddrOfPinnedObject(), valuesPin.AddrOfPinnedObject(), 1, keyCallbacks, valueCallbacks);
                if (options != 0)
                {
                    AXIsProcessTrustedWithOptions(options);
                    CFRelease(options);
                }
            }
            finally
            {
                keysPin.Free();
                valuesPin.Free();
                CFRelease(key);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Couldn't ask for Accessibility access.", ex);
        }
    }

    public static void OpenPrivacy(string anchor)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "open",
                ArgumentList = { "x-apple.systempreferences:com.apple.settings.PrivacySecurity.extension?" + anchor },
            });
        }
        catch (Exception ex)
        {
            Log.Error("Couldn't open System Settings.", ex);
        }
    }

    private static int MicrophoneStatus()
    {
        if (!OperatingSystem.IsMacOS())
            return 3;

        try
        {
            var device = objc_getClass("AVCaptureDevice");
            var selector = sel_registerName("authorizationStatusForMediaType:");
            var media = AudioMediaType();
            if (device == 0 || selector == 0 || media == 0)
                return 0;
            return (int)objc_msgSend_nint(device, selector, media);
        }
        catch (Exception ex)
        {
            Log.Error("Couldn't read microphone permission.", ex);
            return 0;
        }
    }

    private static bool RequestMicrophone(Action<bool> completed)
    {
        try
        {
            var device = objc_getClass("AVCaptureDevice");
            var selector = sel_registerName("requestAccessForMediaType:completionHandler:");
            var media = AudioMediaType();
            var block = MicrophoneBlock();
            if (device == 0 || selector == 0 || media == 0 || block == 0 || _micDone != null)
                return false;

            _micDone = granted =>
            {
                if (!granted && MicrophoneStatus() == 2)
                    OpenPrivacy("Privacy_Microphone");
                completed(granted);
            };
            Log.Info("Asking macOS for microphone access.");
            objc_msgSend_void(device, selector, media, block);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Couldn't request microphone access.", ex);
            return false;
        }
    }

    private static void OnMicrophoneGrant(nint block, byte granted)
    {
        var done = _micDone;
        _micDone = null;
        try
        {
            done?.Invoke(granted != 0);
        }
        catch (Exception ex)
        {
            Log.Error("Microphone prompt failed.", ex);
        }
    }

    private static nint MicrophoneBlock()
    {
        if (_micBlock != 0)
            return _micBlock;

        var system = dlopen("/usr/lib/libSystem.B.dylib", 1);
        var isa = dlsym(system, "_NSConcreteGlobalBlock");
        if (isa == 0)
            return 0;

        var signature = Marshal.StringToHGlobalAnsi("v12@?0c8");
        var descriptor = Marshal.AllocHGlobal(24);
        Marshal.WriteIntPtr(descriptor, 0, 0);
        Marshal.WriteIntPtr(descriptor, 8, 32);
        Marshal.WriteIntPtr(descriptor, 16, signature);

        var literal = Marshal.AllocHGlobal(32);
        Marshal.WriteIntPtr(literal, 0, isa);
        Marshal.WriteInt32(literal, 8, BlockIsGlobal | BlockHasSignature);
        Marshal.WriteInt32(literal, 12, 0);
        Marshal.WriteIntPtr(literal, 16, Marshal.GetFunctionPointerForDelegate(MicGrant));
        Marshal.WriteIntPtr(literal, 24, descriptor);
        _micBlock = literal;
        return _micBlock;
    }

    private static nint AudioMediaType()
    {
        var av = dlopen("/System/Library/Frameworks/AVFoundation.framework/AVFoundation", 1);
        var symbol = dlsym(av, "AVMediaTypeAudio");
        if (symbol == 0)
            return 0;
        return Marshal.ReadIntPtr(symbol);
    }

    [DllImport("libobjc.dylib")]
    private static extern nint objc_getClass(string name);

    [DllImport("libobjc.dylib")]
    private static extern nint sel_registerName(string name);

    [DllImport("libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern nint objc_msgSend_nint(nint receiver, nint selector, nint arg);

    [DllImport("libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_void(nint receiver, nint selector, nint arg1, nint arg2);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void MicGrantBlock(nint block, byte granted);

    [DllImport("libSystem.dylib")]
    private static extern nint dlopen(string path, int mode);

    [DllImport("libSystem.dylib")]
    private static extern nint dlsym(nint handle, string symbol);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern nint CFStringCreateWithCString(nint allocator, string text, uint encoding);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern nint CFDictionaryCreate(nint allocator, nint keys, nint values, nint count, nint keyCallbacks, nint valueCallbacks);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern void CFRelease(nint value);

    [DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
    private static extern bool AXIsProcessTrusted();

    [DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
    private static extern bool AXIsProcessTrustedWithOptions(nint options);
}
