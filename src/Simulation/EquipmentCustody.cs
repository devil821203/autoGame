using System;
using System.Linq;
using Cultivation.Domain;

namespace Cultivation.Simulation;

// Running transfer escrow remains a legitimate holder until the item is delivered.
internal static class EquipmentCustody
{
    internal static long Total(WorldState world, string resourceId)
    {
        long held = checked(world.Characters.Sum(c => Inventory.Balance(world, c.Id, resourceId))
            + world.Organizations.Sum(o => Inventory.Balance(world, o.Id, resourceId)));
        var runtime = world.Time?.ActionRuntime;
        if (runtime is null) return held;

        // Multiple reservations can share an escrow account; count each account only once.
        var accounts = runtime.Reservations.Values
            .Where(r => r.ResourceId == resourceId && r.Remaining > 0
                && runtime.Actions.TryGetValue(r.ActionId, out var action)
                && action.Kind == ActionKind.Transfer && action.Phase == ActionPhase.Running)
            .Select(r => r.Account)
            .Distinct(StringComparer.Ordinal);
        return checked(held + accounts.Sum(account => Inventory.Balance(world, account, resourceId)));
    }
}