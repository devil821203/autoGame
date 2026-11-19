using Cultivation.Domain;

namespace Cultivation.Simulation;

internal static class RemoteCombat
{
    internal static bool IsFar(WorldState w, CombatEncounter b)
    {
        if (w.Performance is null || b.Allies is { Count: > 0 }) return false;
        var region = w.Characters.Single(c => c.Id == w.ControlledCharacterId).RegionId;
        return w.Characters.Single(c => c.Id == b.InitiatorId).RegionId != region && w.Characters.Single(c => c.Id == b.DefenderId).RegionId != region;
    }
    internal static bool Deferred(WorldState w, CombatEncounter b) => IsFar(w, b) && w.Clock.Tick % w.Performance!.RemoteCombatBatchTicks != 0;
    internal static bool ValidWaitingRound(WorldState w, CombatEncounter b) => w.Performance is { } rules &&
        w.Clock.Tick % rules.RemoteCombatBatchTicks != 0 && b.NextRoundTick > w.Clock.Tick - w.Clock.Tick % rules.RemoteCombatBatchTicks;
}
