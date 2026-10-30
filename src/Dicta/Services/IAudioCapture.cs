namespace Dicta.Services;

interface IAudioCapture : IDisposable
{
    event Action<float>? Level;

    string? Start();

    void Stop();

    byte[] SnapshotPcm16k();
}

static class AudioCapture
{
    public static IAudioCapture Create()
    {
        if (OperatingSystem.IsWindows())
            return new MicRecorder();
        if (OperatingSystem.IsMacOS())
            return new MacMicRecorder();
        return new LinuxMicRecorder();
    }
}
