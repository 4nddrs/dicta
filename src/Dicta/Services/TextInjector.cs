namespace Dicta.Services;

enum InjectResult
{
    Inserted,
    Copied,
    Failed,
}

static class TextInjector
{
    public static Task<InjectResult> InjectAsync(nint target, string text)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Task.Run(() =>
            {
                try
                {
                    return UnixPaste.Inject(text);
                }
                catch (Exception ex)
                {
                    Log.Error("Couldn't insert the text.", ex);
                    return InjectResult.Failed;
                }
            });
        }

        var source = new TaskCompletionSource<InjectResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                source.SetResult(Inject(target, text));
            }
            catch (Exception ex)
            {
                Log.Error("Couldn't insert the text.", ex);
                source.SetResult(InjectResult.Failed);
            }
        })
        {
            IsBackground = true,
            Name = "DictaInject",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return source.Task;
    }

    private static InjectResult Inject(nint target, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return InjectResult.Failed;

        var canPaste = target != 0
            && !Native.IsCurrentProcess(target)
            && Native.ClipboardIsTextOnly();

        if (!canPaste)
        {
            if (target != 0 && !Native.IsCurrentProcess(target) && Native.ForceForeground(target))
            {
                Thread.Sleep(40);
                Native.TypeUnicode(text);
                return InjectResult.Inserted;
            }

            return Native.WriteClipboardText(text.TrimEnd()) ? InjectResult.Copied : InjectResult.Failed;
        }

        var previous = Native.ReadClipboardText();
        if (!Native.WriteClipboardText(text))
            return InjectResult.Failed;

        Thread.Sleep(30);
        var focused = Native.ForceForeground(target);
        Thread.Sleep(70);
        focused = focused && Native.GetForegroundWindow() == target;
        if (!focused)
        {
            Log.Info("Couldn't focus the target window.");
            return InjectResult.Copied;
        }

        Native.SendCtrlV();
        Thread.Sleep(280);

        if (previous == null)
            Native.ClearClipboard();
        else
            Native.WriteClipboardText(previous);

        return InjectResult.Inserted;
    }
}
