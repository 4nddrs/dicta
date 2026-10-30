using System.Text;

namespace Dicta;

static class AudioUtil
{
    public static byte[] DownsamplePcm16(byte[] src, int srcRate, int dstRate = 16_000)
    {
        if (srcRate == dstRate)
            return src;

        var srcSamples = src.Length / 2;
        if (srcSamples <= 0)
            return Array.Empty<byte>();

        var dstSamples = (int)((long)srcSamples * dstRate / srcRate);
        if (dstSamples <= 0)
            return Array.Empty<byte>();

        var dst = new byte[dstSamples * 2];
        var step = (double)srcRate / dstRate;
        for (var i = 0; i < dstSamples; i++)
        {
            var pos = i * step;
            var i0 = (int)pos;
            var i1 = Math.Min(i0 + 1, srcSamples - 1);
            var frac = pos - i0;
            var s0 = BitConverter.ToInt16(src, i0 * 2);
            var s1 = BitConverter.ToInt16(src, i1 * 2);
            var mixed = s0 + (s1 - s0) * frac;
            var sample = (short)Math.Clamp(mixed, short.MinValue, short.MaxValue);
            BitConverter.TryWriteBytes(dst.AsSpan(i * 2), sample);
        }

        return dst;
    }

    public static MemoryStream ToWav(byte[] pcm, int sampleRate)
    {
        var stream = new MemoryStream(44 + pcm.Length);
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + pcm.Length);
            writer.Write(Encoding.ASCII.GetBytes("WAVE"));
            writer.Write(Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)1);
            writer.Write(sampleRate);
            writer.Write(sampleRate * 2);
            writer.Write((short)2);
            writer.Write((short)16);
            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(pcm.Length);
            writer.Write(pcm);
        }

        stream.Position = 0;
        return stream;
    }

    public static float Rms(ReadOnlySpan<byte> pcm)
    {
        var samples = pcm.Length / 2;
        if (samples == 0)
            return 0;

        double sum = 0;
        for (var i = 0; i < samples; i++)
        {
            var sample = BitConverter.ToInt16(pcm.Slice(i * 2, 2)) / 32768.0;
            sum += sample * sample;
        }

        return (float)Math.Sqrt(sum / samples);
    }
}
