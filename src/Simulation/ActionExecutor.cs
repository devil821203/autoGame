using Cultivation.Domain;

namespace Cultivation.Simulation;

// Called only on the simulation's private working copy.
internal static class ActionExecutor
{
    internal static string Escrow(string intentId) => "$escrow:" + intentId;

    internal static void Transfer(WorldState world, ActionIntent intent, string from, string to, string reason)
    {
        world.Ledger.Add(new(world.NextSequence++, world.Clock.Tick, intent.Id,
            from, to, intent.ResourceId, intent.Quantity, reason));
    }

    internal static void Execute(WorldState world, ActionIntent intent)
    {
        if (intent.DurationTicks == 0)
        {
            Transfer(world, intent, intent.ActorId, intent.TargetId, "Transfer");
            Emit(world, intent.Id, "Completed", "Resource transfer completed");
            return;
        }
        Transfer(world, intent, intent.ActorId, Escrow(intent.Id), "Reserved");
        world.Scheduled.Add(new(world.NextSequence++, checked(world.Clock.Tick + intent.DurationTicks), intent));
        Emit(world, intent.Id, "Scheduled", "Resources reserved until completion");
    }

    internal static void Emit(WorldState world, string id, string kind, string detail) =>
        world.Events.Add(new(world.NextSequence++, world.Clock.Tick, id, kind, detail));
}
