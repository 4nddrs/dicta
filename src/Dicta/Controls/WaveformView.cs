using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Dicta.Controls;

public sealed class WaveformView : Control
{
    private const int Bars = 28;
    private readonly float[] _levels = new float[Bars];
    private readonly Pen _pen;
    private int _write;

    public WaveformView()
    {
        var brush = new SolidColorBrush(Color.Parse("#A99BFF"));
        _pen = new Pen(brush, 2, lineCap: PenLineCap.Round);
        Reset();
    }

    public void Reset()
    {
        Array.Fill(_levels, 0.08f);
        _write = 0;
        InvalidateVisual();
    }

    public void Push(float rms)
    {
        var shaped = Math.Clamp(rms * 8.5f, 0.08f, 1f);
        _levels[_write] = shaped;
        _write = (_write + 1) % Bars;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        var width = Bounds.Width;
        var height = Bounds.Height;
        if (width <= 1 || height <= 1)
            return;

        var gap = 4.0;
        var barWidth = Math.Max(2, (width - gap * (Bars - 1)) / Bars);
        var center = height / 2;
        for (var i = 0; i < Bars; i++)
        {
            var level = _levels[(_write + i) % Bars];
            var barHeight = Math.Max(4, (height - 4) * level);
            var x = i * (barWidth + gap) + barWidth / 2;
            context.DrawLine(_pen, new Point(x, center - barHeight / 2), new Point(x, center + barHeight / 2));
        }
    }
}
