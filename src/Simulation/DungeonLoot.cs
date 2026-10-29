using Cultivation.Domain;

namespace Cultivation.Simulation;

internal static class DungeonLoot
{
    internal static void Validate(WorldState w)
    {
        if (w.Dungeon is { } r)
            foreach (var (name, value) in new[] { ("EncounterChance", r.EncounterChance), ("WolfChance", r.WolfChance), ("BanditStoneChance", r.BanditStoneChance), ("BanditPillChance", r.BanditPillChance), ("HerbSuccessChance", r.HerbSuccessChance) })
                if (!double.IsFinite(value) || value is < 0 or > 1)
                    throw new InvalidDataException($"combat/dungeon.json: {name} must be between 0 and 1");
        if (w.Dungeon?.Loot is not { } table) return;
        foreach (var (name, drop) in new[] { ("Wolf", table.Wolf), ("BanditMoney", table.BanditMoney), ("BanditRecovery", table.BanditRecovery) })
            if (drop is null || string.IsNullOrWhiteSpace(drop.ResourceId) || drop.MinimumQuantity <= 0 || drop.MaximumQuantity < drop.MinimumQuantity ||
                drop.MaximumQuantity - drop.MinimumQuantity >= uint.MaxValue ||
                !w.Items.Any(i => i.Id == drop.Resolve(w.RecoveryResourceId)))
                throw new InvalidDataException($"combat/dungeon.json: Loot.{name} has invalid ResourceId/MinimumQuantity/MaximumQuantity");
        if (table.BanditMoney.Resolve(w.RecoveryResourceId) == table.BanditRecovery.Resolve(w.RecoveryResourceId))
            throw new InvalidDataException("combat/dungeon.json: Loot.BanditMoney/ BanditRecovery must resolve to distinct resources");
    }

    internal static DungeonDrop? Definition(WorldState w, MonsterKind kind, string item) =>
        DungeonLootTable.For(w.Dungeon).ForMonster(kind).SingleOrDefault(d => d.Resolve(w.RecoveryResourceId) == item);

    internal static string[] Items(WorldState w, CharacterState loser) => loser.MonsterKind == MonsterKind.None
        ? ["spirit-stone", "herb"]
        : DungeonLootTable.For(w.Dungeon).ForMonster(loser.MonsterKind).Select(d => d.Resolve(w.RecoveryResourceId)).ToArray();

    internal static long Cap(WorldState w, CharacterState loser, string item)
    {
        if (loser.MonsterKind != MonsterKind.None && w.Dungeon?.Loot is not null)
        {
            long maximum = Definition(w, loser.MonsterKind, item)?.MaximumQuantity ?? 0;
            return item == "spirit-stone" ? Math.Min(maximum, w.CombatRules!.MaximumLootStones) : maximum;
        }
        return item == "spirit-stone" ? w.CombatRules!.MaximumLootStones : Items(w, loser).Contains(item) ? 1 : 0;
    }
}
