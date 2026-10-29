using Cultivation.AI;
using Cultivation.Domain;

namespace Cultivation.Simulation;

internal static class AiLodScheduling
{
    internal static AiLod Level(WorldState w, CharacterState actor)
    {
        var player = w.Characters.Single(c => c.Id == w.ControlledCharacterId);
        return actor.RegionId != player.RegionId ? AiLod.Remote : !w.DemoMapEnabled || actor.DemoLocationId == player.DemoLocationId ? AiLod.Nearby : AiLod.Regional;
    }
    internal static bool Due(WorldState w, CharacterState a, IReadOnlyDictionary<string, AiUpdateState> stamps, IReadOnlyDictionary<string, long> notifications)
    {
        if (w.Performance is not { } rules) return true;
        if (!stamps.TryGetValue(a.Id, out var previous) || previous.RegionId != a.RegionId || previous.LocationId != a.DemoLocationId || previous.Lod != Level(w, a) ||
            a.Health < 30 || CombatActions.Active(w, a.Id) is not null || notifications.GetValueOrDefault(a.Id, -1) > previous.LastTick) return true;
        int interval = Level(w, a) switch { AiLod.Nearby => rules.NearbyIntervalTicks, AiLod.Regional => rules.RegionalIntervalTicks, _ => rules.RemoteIntervalTicks };
        return w.Clock.Tick - previous.LastTick >= interval;
    }
    internal static void Mark(WorldState w, CharacterState a)
    {
        if (w.Performance is null) return;
        w.AiUpdates.RemoveAll(s => s.ActorId == a.Id);
        w.AiUpdates.Add(new(a.Id, w.Clock.Tick, Level(w, a), a.RegionId, a.DemoLocationId));
    }
    internal static ScoredAction? RemoteChoice(WorldState w, CharacterState a)
    {
        if (CombatActions.Active(w, a.Id) is { } battle)
        {
            bool pill = Inventory.Balance(w, a.Id, "pill") > 0;
            var command = a.Health < 50 && pill ? ActionKind.CombatHeal : a.Health < 30 ? ActionKind.Retreat : ActionKind.Transfer;
            if (command == ActionKind.Transfer) return null;
            return new(new($"ai:{a.Id}:{w.Clock.Tick}", a.Id, battle.InitiatorId == a.Id ? battle.DefenderId : battle.InitiatorId,
                command == ActionKind.Retreat ? "combat" : "pill", 1, 0, DecisionSource.Npc, command), 1, "遠方戰鬥只依自身傷勢與可見對手選擇療傷／撤退，使用共同驗證");
        }
        // Only owned resources and the actor's state are observed; no remote market or global targets.
        if (Inventory.Balance(w, a.Id, "pill") <= 0) return null;
        var kind = a.Health < 100 ? ActionKind.Heal : a.Cultivation < a.DesiredCultivation ? ActionKind.Cultivate : ActionKind.Transfer;
        if (kind == ActionKind.Transfer) return null;
        return new(new($"ai:{a.Id}:{w.Clock.Tick}", a.Id, a.Id, "pill", 1, 0, DecisionSource.Npc, kind), 1, "遠方低頻決策只使用自身丹藥，沿用共用療傷／修煉規則");
    }
}
