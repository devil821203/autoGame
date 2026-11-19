using Cultivation.Domain;

namespace Cultivation.Simulation;

public sealed record TestScenarioEntry(string Id, string Name, string Description);
public sealed record TestScenarioSettings(string DefaultId, TestScenarioEntry[] Scenarios);
public static class TestScenarioCatalog
{
    public static TestScenarioSettings Read(GameConfiguration? configuration = null)
    {
        var settings = (configuration ?? GameConfiguration.Find()).Read<TestScenarioSettings>("testing/scenarios.json");
        if (settings.Scenarios is not { Length: > 0 } || settings.Scenarios.Any(s => s is null ||
            s.Id is not ("methods" or "settlement" or "battle") || string.IsNullOrWhiteSpace(s.Name) || string.IsNullOrWhiteSpace(s.Description)) ||
            settings.Scenarios.Select(s => s.Id).Distinct().Count() != settings.Scenarios.Length ||
            !settings.Scenarios.Any(s => s.Id == settings.DefaultId))
            throw new InvalidDataException("testing/scenarios.json: invalid default/scenario IDs/names/descriptions");
        return settings;
    }
    public static WorldState Create(string id, string name = "行者", GameConfiguration? configuration = null)
    {
        var config = configuration ?? GameConfiguration.Find();
        if (!Read(config).Scenarios.Any(s => s.Id == id)) throw new ArgumentException("Unknown test scenario: " + id);
        var world = id switch {
            "methods" => MethodTrainingScenario.Create(name, config),
            "settlement" => SettlementOrganizationScenario.Create(name, config),
            "battle" => MedicineOnlyProfile.ApplyFresh(BoardBattleScenario.Create(name, config), config),
            _ => throw new ArgumentException("Unknown test scenario: " + id)
        };
        if (world.Time is { } time) {
            MinuteTimeProfile.EnableFresh(world, config);
        }
        WorldIntegrity.Validate(world); return world;
    }
    public static WorldState CreateDefault() => Create(Read().DefaultId);
}
