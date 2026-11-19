using Cultivation.Simulation;

namespace Cultivation.Headless;

public static class AutonomousRunner
{
    public static void Step(SimulationEngine engine) => engine.Step(autonomous: true);
}
