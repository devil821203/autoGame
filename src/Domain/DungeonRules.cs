namespace Cultivation.Domain;

public enum MonsterKind { None, Bandit, Wolf }
public sealed record DungeonRules(double EncounterChance = 0.2, double WolfChance = 0.5,
    double BanditStoneChance = 0.8, double BanditPillChance = 0.1, double HerbSuccessChance = 0.5)
{
    public DungeonLootTable? Loot { get; init; }
}

public sealed record DungeonDrop(string ResourceId, long MinimumQuantity, long MaximumQuantity)
{
    public string Resolve(string recoveryResourceId) => ResourceId == "$recovery" ? recoveryResourceId : ResourceId;
    [System.Text.Json.Serialization.JsonIgnore]
    public double MeanQuantity => MinimumQuantity / 2.0 + MaximumQuantity / 2.0;
}

public sealed record DungeonLootTable(DungeonDrop Wolf, DungeonDrop BanditMoney, DungeonDrop BanditRecovery)
{
    // Compatibility only: worlds created before configurable loot retain their original drops.
    public static DungeonLootTable For(DungeonRules? rules) => rules?.Loot ??
        new(new("beast-hide", 1, 1), new("spirit-stone", 1, 5), new("$recovery", 1, 1));
    public IEnumerable<DungeonDrop> ForMonster(MonsterKind kind) => kind switch
    {
        MonsterKind.Wolf => [Wolf],
        MonsterKind.Bandit => [BanditMoney, BanditRecovery],
        _ => []
    };
}
