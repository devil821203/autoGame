namespace Cultivation.Domain;

public sealed record ItemDefinition(string Id, string Name, string Category);
public sealed record AspirationDefinition(Aspiration Id, string Name, double WealthMultiplier = 1,
    double GrowthMultiplier = 1, double AlchemyExperienceMultiplier = 0, double CombatMultiplier = 1);
public sealed record DemoWorldRules(int SecondsPerTick = 60, long TownTravelTicks = 3, long OtherTravelTicks = 5);
public sealed record MissionTemplate(long Quantity = 20, long UnitPrice = 120, long DeadlineDays = 7, long MaximumBudget = 2400);
