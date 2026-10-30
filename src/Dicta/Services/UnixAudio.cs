using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Dicta.Services;

sealed class MacMicRecorder : IAudioCapture
{
    private const int BufferBytes = 3200;

    private readonly List<byte[]> _chunks = new();
    private readonly object _gate = new();
    private AqCallback? _callback;
    private nint _queue;
    private int _sampleRate;
    private volatile bool _running;

    public event Action<float>? Level;

    public string? Start()
    {
        _callback = OnBuffer;
        foreach (var rate in new[] { 16_000, 48_000, 44_100 })
        {
            var status = TryOpen(rate);
            if (status == 0)
            {
                _sampleRate = rate;
                _running = true;
                return null;
            }

            Log.Error("Microphone open failed at " + rate + " Hz. Code " + status);
            CloseQueue();
        }

        return "Open Dicta in the menu bar, choose Permissions, and allow the microphone. Then try again.";
    }

    public void Stop()
    {
        _running = false;
        if (_queue == 0)
            return;

        AudioQueueStop(_queue, true);
    }

    public byte[] SnapshotPcm16k()
    {
        byte[] pcm;
        lock (_gate)
        {
            var length = 0;
            foreach (var chunk in _chunks)
                length += chunk.Length;
            pcm = new byte[length];
            var offset = 0;
            foreach (var chunk in _chunks)
            {
                Buffer.BlockCopy(chunk, 0, pcm, offset, chunk.Length);
                offset += chunk.Length;
            }
        }

        if (_sampleRate == 0 || pcm.Length < 2)
            return Array.Empty<byte>();

        return _sampleRate == 16_000 ? pcm : AudioUtil.DownsamplePcm16(pcm, _sampleRate);
    }

    public void Dispose()
    {
        Stop();
        CloseQueue();
    }

    private int TryOpen(int sampleRate)
    {
        var format = new AudioFormat
        {
            SampleRate = sampleRate,
            FormatId = 0x6C70636D,
            FormatFlags = 12,
            BytesPerPacket = 2,
            FramesPerPacket = 1,
            BytesPerFrame = 2,
            Channels = 1,
            BitsPerChannel = 16,
        };

        var status = AudioQueueNewInput(ref format, _callback!, 0, 0, 0, 0, out _queue);
        if (status != 0 || _queue == 0)
            return status == 0 ? -1 : status;

        for (var i = 0; i < 3; i++)
        {
            status = AudioQueueAllocateBuffer(_queue, BufferBytes, out var buffer);
            if (status != 0)
                return status;
            status = AudioQueueEnqueueBuffer(_queue, buffer, 0, 0);
            if (status != 0)
                return status;
        }

        return AudioQueueStart(_queue, 0);
    }

    private void OnBuffer(nint user, nint queue, nint buffer, nint startTime, uint packets, nint descriptions)
    {
        try
        {
            if (buffer != 0 && _running)
            {
                var size = Marshal.ReadInt32(buffer, 16);
                var data = Marshal.ReadIntPtr(buffer, 8);
                if (size > 0 && data != 0)
                {
                    var chunk = new byte[size];
                    Marshal.Copy(data, chunk, 0, size);
                    lock (_gate)
                        _chunks.Add(chunk);
                    Level?.Invoke(Rms(chunk));
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("Microphone callback failed.", ex);
        }

        if (_running && queue != 0 && buffer != 0)
            AudioQueueEnqueueBuffer(queue, buffer, 0, 0);
    }

    private void CloseQueue()
    {
        if (_queue == 0)
            return;

        AudioQueueDispose(_queue, true);
        _queue = 0;
    }

    private static float Rms(byte[] pcm)
    {
        var samples = pcm.Length / 2;
        if (samples == 0)
            return 0;

        double sum = 0;
        for (var i = 0; i < samples; i++)
        {
            var sample = (short)(pcm[i * 2] | (pcm[i * 2 + 1] << 8));
            var value = sample / 32768.0;
            sum += value * value;
        }

        return (float)Math.Sqrt(sum / samples);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AudioFormat
    {
        public double SampleRate;
        public uint FormatId;
        public uint FormatFlags;
        public uint BytesPerPacket;
        public uint FramesPerPacket;
        public uint BytesPerFrame;
        public uint Channels;
        public uint BitsPerChannel;
        public uint Reserved;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void AqCallback(nint user, nint queue, nint buffer, nint startTime, uint packets, nint descriptions);

    [DllImport("/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox")]
    private static extern int AudioQueueNewInput(ref AudioFormat format, AqCallback callback, nint user, nint runLoop, nint mode, uint flags, out nint queue);

    [DllImport("/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox")]
    private static extern int AudioQueueAllocateBuffer(nint queue, uint size, out nint buffer);

    [DllImport("/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox")]
    private static extern int AudioQueueEnqueueBuffer(nint queue, nint buffer, uint descriptions, nint descriptionPointer);

    [DllImport("/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox")]
    private static extern int AudioQueueStart(nint queue, nint startTime);

    [DllImport("/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox")]
    private static extern int AudioQueueStop(nint queue, bool immediate);

    [DllImport("/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox")]
    private static extern int AudioQueueDispose(nint queue, bool immediate);
}

sealed class LinuxMicRecorder : IAudioCapture
{
    private readonly List<byte[]> _chunks = new();
    private readonly object _gate = new();
    private Process? _process;
    private Thread? _reader;
    private volatile bool _running;

    public event Action<float>? Level;

    public string? Start()
    {
        if (TryLaunch("parec", "--raw --rate=16000 --channels=1 --format=s16le --latency-msec=40"))
            return null;
        if (TryLaunch("arecord", "-q -f S16_LE -r 16000 -c 1 -t raw"))
            return null;
        return "No microphone recorder found. Install pulseaudio-utils (parec) or alsa-utils (arecord).";
    }

    public void Stop()
    {
        _running = false;
        try
        {
            if (_process is { HasExited: false })
                _process.Kill(entireProcessTree: true);
        }
        catch (Exception ex)
        {
            Log.Error("Couldn't stop the microphone.", ex);
        }

        _reader?.Join(500);
    }

    public byte[] SnapshotPcm16k()
    {
        lock (_gate)
        {
            var length = 0;
            foreach (var chunk in _chunks)
                length += chunk.Length;
            var pcm = new byte[length];
            var offset = 0;
            foreach (var chunk in _chunks)
            {
                Buffer.BlockCopy(chunk, 0, pcm, offset, chunk.Length);
                offset += chunk.Length;
            }

            return pcm;
        }
    }

    public void Dispose()
    {
        Stop();
        _process?.Dispose();
        _process = null;
    }

    private bool TryLaunch(string file, string args)
    {
        Process process;
        try
        {
            process = new Process
            {
                StartInfo = new ProcessStartInfo(file, args)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                },
            };
            if (!process.Start())
                return false;
        }
        catch (Exception ex)
        {
            Log.Error("Couldn't start " + file + ".", ex);
            return false;
        }

        _ = process.StandardError.ReadToEndAsync();
        Thread.Sleep(350);
        if (process.HasExited)
        {
            process.Dispose();
            return false;
        }

        _process = process;
        _running = true;
        _reader = new Thread(ReadLoop) { IsBackground = true, Name = "DictaMic" };
        _reader.Start();
        return true;
    }

    private void ReadLoop()
    {
        var stream = _process?.StandardOutput.BaseStream;
        if (stream == null)
            return;

        var buffer = new byte[3200];
        try
        {
            while (_running)
            {
                var read = stream.Read(buffer, 0, buffer.Length);
                if (read <= 0)
                    break;

                var chunk = new byte[read];
                Buffer.BlockCopy(buffer, 0, chunk, 0, read);
                lock (_gate)
                    _chunks.Add(chunk);
                Level?.Invoke(Rms(chunk));
            }
        }
        catch (Exception ex)
        {
            if (_running)
                Log.Error("Microphone read stopped.", ex);
        }
    }

    private static float Rms(byte[] pcm)
    {
        var samples = pcm.Length / 2;
        if (samples == 0)
            return 0;

        double sum = 0;
        for (var i = 0; i < samples; i++)
        {
            var sample = (short)(pcm[i * 2] | (pcm[i * 2 + 1] << 8));
            var value = sample / 32768.0;
            sum += value * value;
        }

        return (float)Math.Sqrt(sum / samples);
    }
}
