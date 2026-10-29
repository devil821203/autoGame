using Godot;

namespace Cultivation.Presentation;

// Presentation-only history: never feeds values back into the simulation.
public partial class ValueChart : Control
{
    private (long Tick, double Value)[] samples = [];
    public void SetSamples(IEnumerable<(long Tick, double Value)> values)
    {
        samples = values.ToArray();
        QueueRedraw();
    }

    public override void _Draw()
    {
        var area = new Rect2(48, 12, Math.Max(1, Size.X - 64), Math.Max(1, Size.Y - 45));
        DrawRect(area, new Color("172433"));
        if (samples.Length == 0) return;
        double min = samples.Min(s => s.Value), max = samples.Max(s => s.Value);
        if (max == min) { min -= 1; max += 1; }
        var font = ThemeDB.FallbackFont;
        DrawString(font, new Vector2(2, 24), max.ToString("0.#"), fontSize: 12);
        DrawString(font, new Vector2(2, area.End.Y), min.ToString("0.#"), fontSize: 12);
        DrawString(font, new Vector2(48, Size.Y - 5), $"Tick {samples[0].Tick} → {samples[^1].Tick}", fontSize: 12);
        long span = Math.Max(1, samples[^1].Tick - samples[0].Tick);
        var points = samples.Select(s => new Vector2(area.Position.X + (float)((s.Tick - samples[0].Tick) / (double)span) * area.Size.X,
            area.End.Y - (float)((s.Value - min) / (max - min)) * area.Size.Y)).ToArray();
        if (points.Length > 1) DrawPolyline(points, new Color("65d6ad"), 2, true);
        DrawCircle(points[^1], 3, new Color("65d6ad"));
    }
}
