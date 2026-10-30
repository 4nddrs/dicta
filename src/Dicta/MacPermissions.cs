using System.Runtime.InteropServices;

namespace Dicta;

static class MacPermissions
{
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
    private static extern bool AXIsProcessTrustedWithOptions(nint options);
}
