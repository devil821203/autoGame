using Cultivation.Domain;

namespace Cultivation.Simulation;

internal static class InventoryCapacity
{
    internal static bool CanReceive(WorldState w, string owner, string item, long amount, string? excludeIntentId = null)
    {
        try
        {
            long reserved = 0;
            foreach (var task in w.Scheduled)
            {
                var i = task.Intent;
                if (i.Id == excludeIntentId) continue;
                if (w.SectFacilities is { } facilities && owner == facilities.OrganizationId &&
                    (i.Kind == ActionKind.SectGather && item == "herb" || i.Kind == ActionKind.SectBrew && item == "pill")) reserved = checked(reserved + 1);
                if (i.Kind == ActionKind.Transfer && i.TargetId == owner && i.ResourceId == item) reserved = checked(reserved + i.Quantity);
                if (i.Kind == ActionKind.Gather && i.ActorId == owner && item == "herb") reserved = checked(reserved + 1);
                if (i.Kind == ActionKind.Brew && i.ActorId == owner && item == "pill") reserved = checked(reserved + 1);
            }
            if (item == "spirit-stone")
                foreach (var mission in w.Missions.Where(m => m.OrganizationId == owner && m.Status == MissionStatus.Active))
                    reserved = checked(reserved + mission.ReservedBudget - checked(mission.Delivered * mission.UnitPrice));
            _ = checked(checked(Inventory.Balance(w, owner, item) + reserved) + amount);
            return true;
        }
        catch (OverflowException) { return false; }
    }
}
