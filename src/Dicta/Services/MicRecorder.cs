using System.Runtime.InteropServices;

namespace Dicta.Services;

sealed class MicRecorder : IAudioCapture
{
    private const int BufferCount = 5;
    private const int BufferMs = 50;

    private readonly List<byte[]> _chunks = new();
    private readonly object _audioGate = new();
    private Native.WaveInProc? _callback;
    private nint _handle;
    private nint[]? _headers;
    private GCHandle[]? _pins;
    private int _sampleRate;
    private volatile bool _running;

    public event Action<float>? Level;

    public string? Start()
    {
        if (!OperatingSystem.IsWindows())
            return "Dicta only runs on Windows.";

        _callback = OnWave;
        foreach (var rate in new[] { 16_000, 48_000, 44_100 })
        {
            var error = TryOpen(rate);
            if (error == null)
            {
                _sampleRate = rate;
                return null;
            }

            if (error != "format")
                return error;
        }

        return "This microphone doesn't support a compatible format.";
    }

    public byte[] SnapshotPcm16k()
    {
        byte[] pcm;
        lock (_audioGate)
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

        if ((_sampleRate == 0) || pcm.Length < 2)
            return Array.Empty<byte>();

        return _sampleRate == 16_000 ? pcm : AudioUtil.DownsamplePcm16(pcm, _sampleRate);
    }

    public void Stop()
    {
        _running = false;
        if (_handle == 0)
            return;

        Native.waveInReset(_handle);
        var headerSize = Marshal.SizeOf<Native.WaveHdr>();
        if (_headers != null)
        {
            foreach (var header in _headers)
            {
                if (header != 0)
                    Native.waveInUnprepareHeader(_handle, header, headerSize);
            }
        }

        Native.waveInClose(_handle);
        _handle = 0;
        FreeBuffers();
    }

    public void Dispose() => Stop();

    private string? TryOpen(int sampleRate)
    {
        var format = new Native.WaveFormatEx
        {
            FormatTag = 1,
            Channels = 1,
            SamplesPerSec = (uint)sampleRate,
            BitsPerSample = 16,
            BlockAlign = 2,
            AvgBytesPerSec = (uint)(sampleRate * 2),
            Size = 0,
        };

        var result = Native.waveInOpen(out var handle, Native.WaveMapper, ref format, _callback!, 0, Native.CallbackFunction);
        if (result == 32)
            return "format";
        if (result == 4)
            return "The microphone is in use by another app.";
        if (result == 6 || result == 7)
            return "No microphone found. In Windows, allow microphone access for desktop apps.";
        if (result != 0)
            return "Couldn't open the microphone (code " + result + ").";

        _handle = handle;
        try
        {
            QueueBuffers(sampleRate);
            _running = true;
            var start = Native.waveInStart(_handle);
            if (start != 0)
            {
                Stop();
                return "Couldn't start recording (code " + start + ").";
            }

            return null;
        }
        catch (Exception ex)
        {
            Log.Error("Couldn't prepare the microphone.", ex);
            Stop();
            return "Couldn't prepare the microphone.";
        }
    }

    private void QueueBuffers(int sampleRate)
    {
        var bytes = Math.Max(2, sampleRate * 2 * BufferMs / 1000);
        bytes -= bytes % 2;
        var headerSize = Marshal.SizeOf<Native.WaveHdr>();
        _headers = new nint[BufferCount];
        _pins = new GCHandle[BufferCount];

        for (var i = 0; i < BufferCount; i++)
        {
            var buffer = new byte[bytes];
            _pins[i] = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            var header = new Native.WaveHdr
            {
                Data = _pins[i].AddrOfPinnedObject(),
                BufferLength = (uint)bytes,
            };
            var pointer = Marshal.AllocHGlobal(headerSize);
            Marshal.StructureToPtr(header, pointer, false);
            var prepared = Native.waveInPrepareHeader(_handle, pointer, headerSize);
            if (prepared != 0)
                throw new InvalidOperationException("waveInPrepareHeader " + prepared);
            var added = Native.waveInAddBuffer(_handle, pointer, headerSize);
            if (added != 0)
                throw new InvalidOperationException("waveInAddBuffer " + added);
            _headers[i] = pointer;
        }
    }

    private void OnWave(nint handle, uint message, nint instance, nint headerPointer, nint reserved)
    {
        if (message != Native.WimData || headerPointer == 0)
            return;

        try
        {
            var header = Marshal.PtrToStructure<Native.WaveHdr>(headerPointer);
            var count = (int)header.BytesRecorded;
            if (count > 1 && header.Data != 0)
            {
                count -= count % 2;
                var copy = new byte[count];
                Marshal.Copy(header.Data, copy, 0, count);
                lock (_audioGate)
                    _chunks.Add(copy);
                Level?.Invoke(AudioUtil.Rms(copy));
            }
        }
        catch (Exception ex)
        {
            Log.Error("Error reading audio.", ex);
        }

        if (_running)
            Native.waveInAddBuffer(handle, headerPointer, Marshal.SizeOf<Native.WaveHdr>());
    }

    private void FreeBuffers()
    {
        if (_headers != null)
        {
            foreach (var header in _headers)
            {
                if (header != 0)
                    Marshal.FreeHGlobal(header);
            }
        }

        if (_pins != null)
        {
            foreach (var pin in _pins)
            {
                if (pin.IsAllocated)
                    pin.Free();
            }
        }

        _headers = null;
        _pins = null;
    }
}
