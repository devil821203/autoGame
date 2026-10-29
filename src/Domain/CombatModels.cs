namespace Cultivation.Domain;

public sealed record CombatRules(long RoundTicks = 2, int MaximumRounds = 20,
    int DamageVariance = 2, int PowerMultiplier = 2, double RetreatChance = 0.8,
    int MinimumEngageHealth = 30, long MaximumLootStones = 25, long VictoryExperience = 5);
public enum CombatStatus { Active, Victory, Draw, Retreated }
public sealed record CombatEncounter(string Id, string InitiatorId, string DefenderId,
    long StartedTick, long NextRoundTick, int Rounds = 0, CombatStatus Status = CombatStatus.Active,
    string? WinnerId = null, long? EndedTick = null, bool InitiatorBoost = false,
    bool DefenderBoost = false, long InitiatorCommandTick = -1, long DefenderCommandTick = -1,
    List<string>? Allies = null, List<string>? PreparedAllies = null, List<string>? CommandedAllies = null, long AllyCommandTick = -1,
    List<string>? Withdrawn = null);
public sealed record LootReceipt(string CombatId, long Tick, string WinnerId, string LoserId, string ResourceId, long Quantity);

public static class CombatMath
{
    public static int Damage(int attack, int defense, int multiplier = 1, int variance = 0) =>
        (int)Math.Clamp((long)attack * multiplier - defense + variance, 1, 100);
}
