using Cultivation.Domain;

namespace Cultivation.Simulation;

public static class ActionValidator
{
    public static string? Validate(WorldState world, ActionIntent intent)
    {
        if (string.IsNullOrWhiteSpace(intent.Id) || intent.Id.StartsWith("$", StringComparison.Ordinal)) return "Invalid intent ID";
        if (!Enum.IsDefined(intent.Source)) return "Invalid decision source";
        if (!world.Characters.Any(c => c.Id == intent.ActorId)) return "Unknown actor";
        if (intent.Source == DecisionSource.Player && world.ControlledCharacterId != intent.ActorId) return "Actor is not player controlled";
        if (intent.Source == DecisionSource.Npc && world.ControlledCharacterId == intent.ActorId) return "Actor is not AI controlled";
        if (intent.TargetId == intent.ActorId) return "Self transfer is not allowed";
        if (!world.Characters.Any(c => c.Id == intent.TargetId) && !world.Organizations.Any(o => o.Id == intent.TargetId)) return "Unknown recipient";
        if (string.IsNullOrWhiteSpace(intent.ResourceId) || intent.Quantity <= 0) return "Invalid resource or quantity";
        if (intent.DurationTicks < 0 || intent.DurationTicks > long.MaxValue - world.Clock.Tick) return "Invalid duration";
        if (Inventory.Balance(world, intent.ActorId, intent.ResourceId) < intent.Quantity) return "Insufficient resources";
        try
        {
            long incoming = 0;
            foreach (var task in world.Scheduled.Where(t => t.Intent.TargetId == intent.TargetId && t.Intent.ResourceId == intent.ResourceId))
                incoming = checked(incoming + task.Intent.Quantity);
            _ = checked(checked(Inventory.Balance(world, intent.TargetId, intent.ResourceId) + incoming) + intent.Quantity);
        }
        catch (OverflowException) { return "Recipient capacity exceeded"; }
        return null;
    }
}
