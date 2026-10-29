namespace Cultivation.Simulation;

// Wall time belongs to the host, never to domain rules. Excess time remains queued.
public sealed class TickPump(SimulationEngine engine, TimeSpan interval, bool autonomous = false)
{
    private long accumulated;
    private readonly long intervalTicks = interval.Ticks > 0 ? interval.Ticks : throw new ArgumentOutOfRangeException(nameof(interval));

    public int Advance(TimeSpan elapsed, int maxSteps = 32)
    {
        if (elapsed.Ticks < 0 || maxSteps <= 0) throw new ArgumentOutOfRangeException(nameof(elapsed));
        accumulated = checked(accumulated + elapsed.Ticks);
        int count = 0;
        while (accumulated >= intervalTicks && count < maxSteps)
        {
            engine.Step(autonomous);
            accumulated -= intervalTicks;
            count++;
        }
        return count;
    }
}
