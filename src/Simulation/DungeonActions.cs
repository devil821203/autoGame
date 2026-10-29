using Cultivation.Domain;

namespace Cultivation.Simulation;

internal static class DungeonActions
{
    internal static string? Validate(WorldState w, ActionIntent i)
    {
        var a = w.Characters.Single(c => c.Id == i.ActorId);
        return w.Dungeon is not null && w.DemoMapEnabled && w.CombatRules is not null && a.MonsterKind == MonsterKind.None &&
            a.RegionId == "valley" && a.Health >= w.CombatRules.MinimumEngageHealth &&
            i.TargetId == "dungeon" && i.ResourceId == "combat" && i.Quantity == 1 && i.DurationTicks == 0 && i.MissionId is null &&
            !w.Scheduled.Any(s => s.Intent.ActorId == a.Id) ? PartyActions.ValidateExploration(w, a.Id) : "Dungeon unavailable, wrong location or actor busy";
    }
    internal static void Execute(WorldState w, ActionIntent i)
    {
        var r = w.Dungeon!; var rng = new SeededRandom(w.RandomState);
        double Roll() => rng.NextUInt32() / ((double)uint.MaxValue + 1);
        if (Roll() >= r.EncounterChance)
        {
            bool found = Roll() < r.HerbSuccessChance && InventoryCapacity.CanReceive(w, i.ActorId, "herb", 1);
            if (found) w.Ledger.Add(new(w.NextSequence++, w.Clock.Tick, i.Id, "$dungeon-herb", i.ActorId, "herb", 1, "DungeonForaged"));
            w.RandomState = rng.State;
            ActionExecutor.Emit(w, i.Id, found ? "DungeonGathered" : "DungeonGatherFailed", i.ActorId);
            return;
        }
        bool wolf = Roll() < r.WolfChance;
        string monster = "dungeon-monster:" + w.NextSequence;
        w.Characters.Add(new(monster, wolf ? "狼" : "山匪") { Mindset = w.MindsetRules?.Initial, Stamina = w.StaminaRules is { } stamina ? new(stamina.Maximum) : null, MonsterKind = wolf ? MonsterKind.Wolf : MonsterKind.Bandit,
            DemoLocationId = "dungeon", Hostile = true, Health = 40, BaseAttack = 4, BaseDefense = 1,
            WealthWeight = 0, GrowthWeight = 0, SocialWeight = 0, CombatDrive = 0 });
        void Issue(string item, long amount) => w.Ledger.Add(new(w.NextSequence++, w.Clock.Tick, i.Id, "$dungeon", monster, item, amount, "DungeonSpawnLoot"));
        var loot = DungeonLootTable.For(r);
        void Drop(DungeonDrop drop)
        {
            long amount = drop.MinimumQuantity;
            if (drop.MaximumQuantity > amount) amount += (long)(rng.NextUInt32() % (ulong)(drop.MaximumQuantity - amount + 1));
            Issue(drop.Resolve(w.RecoveryResourceId), amount);
        }
        if (wolf) Drop(loot.Wolf);
        else
        {
            if (Roll() < r.BanditStoneChance) Drop(loot.BanditMoney);
            if (Roll() < r.BanditPillChance) Drop(loot.BanditRecovery);
        }
        w.RandomState = rng.State;
        CombatActions.Execute(w, new(i.Id, i.ActorId, monster, "combat", 1, 0, i.Source, ActionKind.Engage));
        if (PartyActions.For(w, i.ActorId) is { } party)
        {
            int index = w.Combats.FindIndex(c => c.Id == i.Id);
            w.Combats[index] = w.Combats[index] with { Allies = party.Members.Where(id => id != i.ActorId).ToList() };
            w.Plans.RemoveAll(p => party.Members.Contains(p.ActorId));
        }
        ActionExecutor.Emit(w, i.Id, "DungeonEncounter", $"{i.ActorId}:{monster}:{(wolf ? "Wolf" : "Bandit")}");
    }
}


