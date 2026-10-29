using Cultivation.Domain;

namespace Cultivation.Simulation;

internal static class HexBuildingActions
{
    internal static bool Handles(ActionKind kind) => kind is ActionKind.ConstructBuilding or ActionKind.CancelBuilding;
    internal static bool HasService(WorldState w, string actorId, string service)
    {
        if (w.HexMap is not { } map) return false;
        var actor = w.Characters.Single(c => c.Id == actorId);
        return map.Buildings.Any(b => b.Status == BuildingStatus.Complete && b.OrganizationId == actor.OrganizationId &&
            b.Coord == HexNavigation.Position(map, actor) && map.BuildingDefinitions.Any(d => d.Id == b.DefinitionId && d.Service == service));
    }
    private static bool Owns(WorldState w, HexBuilding b) => w.HexMap!.Tiles.Any(t => t.Coord == b.Coord && t.LocalOrganizationId == b.OrganizationId);
    internal static string? Validate(WorldState w, ActionIntent i)
    {
        var map = w.HexMap; var actor = w.Characters.Single(c => c.Id == i.ActorId);
        var org = w.Organizations.SingleOrDefault(o => o.Id == i.OrganizationId);
        if (map is null || org is null || org.LeaderId != actor.Id || actor.OrganizationId != org.Id || actor.RegionId != org.RegionId ||
            i.ResourceId != "spirit-stone" || i.DurationTicks != 0) return "須由所屬組織首領操作，並使用組織錢財";
        if (i.Kind == ActionKind.CancelBuilding)
        {
            var building = map.Buildings.SingleOrDefault(b => b.Id == i.TargetId && b.OrganizationId == org.Id);
            return i.Quantity == 1 && i.MissionId is null && building is not null && building.Status is BuildingStatus.Constructing or BuildingStatus.Suspended &&
                InventoryCapacity.CanReceive(w, org.Id, "spirit-stone", map.BuildingDefinitions.Single(d => d.Id == building.DefinitionId).Cost)
                ? null : "僅可取消尚未完工的己方建築，且組織須能收回退款";
        }
        var def = map.BuildingDefinitions.SingleOrDefault(d => d.Id == i.MissionId);
        if (def is null || def.Kind != org.Kind || i.Quantity != def.Cost || HexNavigation.Resolve(map, i.TargetId) is not { } coord ||
            !map.Tiles.Any(t => t.Coord == coord && t.LocalOrganizationId == org.Id && HexNavigation.Passable(t)) ||
            map.Buildings.Any(b => b.Coord == coord && b.Status != BuildingStatus.Cancelled) ||
            w.Scheduled.Any(t => t.Intent.ActorId == actor.Id) || def.Ticks > long.MaxValue - w.Clock.Tick ||
            Inventory.Balance(w, org.Id, "spirit-stone") < def.Cost) return "建築類型、領地或槽位不符，或角色忙碌／組織資金不足";
        return null;
    }
    internal static void Execute(WorldState w, ActionIntent i)
    {
        var map = w.HexMap!;
        if (i.Kind == ActionKind.CancelBuilding)
        {
            int index = map.Buildings.FindIndex(b => b.Id == i.TargetId);
            var b = map.Buildings[index]; var d = map.BuildingDefinitions.Single(d => d.Id == b.DefinitionId);
            var original = w.Scheduled.SingleOrDefault(t => t.Intent.Id == b.IntentId)?.Intent ??
                new ActionIntent(b.IntentId!, i.ActorId, b.Coord.ToString(), "spirit-stone", d.Cost, 0, i.Source, ActionKind.ConstructBuilding);
            ActionExecutor.Transfer(w, original, ActionExecutor.Escrow(b.IntentId!), b.OrganizationId, "BuildingRefund");
            w.Scheduled.RemoveAll(t => t.Intent.Id == b.IntentId);
            map.Buildings[index] = b with { Status = BuildingStatus.Cancelled };
            ActionExecutor.Emit(w, i.Id, "BuildingCancelled", b.Id); return;
        }
        var definition = map.BuildingDefinitions.Single(d => d.Id == i.MissionId);
        var coord = HexNavigation.Resolve(map, i.TargetId)!.Value;
        ActionExecutor.Transfer(w, i, i.OrganizationId!, ActionExecutor.Escrow(i.Id), "BuildingReserved");
        long due = checked(w.Clock.Tick + definition.Ticks);
        map.Buildings.Add(new(i.Id, definition.Id, i.OrganizationId!, coord, BuildingStatus.Constructing, due, i.Id));
        w.Scheduled.Add(new(w.NextSequence++, due, i));
        ActionExecutor.Emit(w, i.Id, "BuildingStarted", definition.Name);
    }
    internal static void Complete(WorldState w, ActionIntent i)
    {
        var map = w.HexMap!; int index = map.Buildings.FindIndex(b => b.Id == i.Id); var b = map.Buildings[index];
        if (!Owns(w, b))
        {
            map.Buildings[index] = b with { Status = BuildingStatus.Suspended };
            // Keep the reservation represented by a future task until cancellation.
            w.Scheduled.Add(new(w.NextSequence++, checked(w.Clock.Tick + 1), i));
            ActionExecutor.Emit(w, i.Id, "BuildingSuspended", "Territory unavailable"); return;
        }
        ActionExecutor.Transfer(w, i, ActionExecutor.Escrow(i.Id), "consumed", "BuildingConsumed");
        map.Buildings[index] = b with { Status = BuildingStatus.Complete };
        ActionExecutor.Emit(w, i.Id, "BuildingCompleted", b.DefinitionId);
    }
    internal static void ValidateWorld(WorldState w)
    {
        var map = w.HexMap!;
        if (map.BuildingDefinitions.Select(d => d.Id).Distinct().Count() != map.BuildingDefinitions.Count ||
            map.BuildingDefinitions.Any(d => string.IsNullOrWhiteSpace(d.Id) || string.IsNullOrWhiteSpace(d.Name) || !Enum.IsDefined(d.Kind) || d.Cost <= 0 || d.Ticks <= 0 || d.Ticks > 100000 || d.Service is not ("grove" or "alchemy")))
            throw new InvalidDataException("Invalid building definitions");
        if (map.Buildings.Select(b => b.Id).Distinct().Count() != map.Buildings.Count ||
            map.Buildings.Where(b => b.Status != BuildingStatus.Cancelled).Select(b => b.Coord).Distinct().Count() != map.Buildings.Count(b => b.Status != BuildingStatus.Cancelled))
            throw new InvalidDataException("Duplicate building or slot");
        foreach (var b in map.Buildings)
        {
            var d = map.BuildingDefinitions.SingleOrDefault(d => d.Id == b.DefinitionId);
            if (d is null || !Enum.IsDefined(b.Status) || !w.Organizations.Any(o => o.Id == b.OrganizationId && o.Kind == d.Kind) ||
                !map.Tiles.Any(t => t.Coord == b.Coord && HexNavigation.Passable(t))) throw new InvalidDataException("Invalid hex building");
            if (b.IntentId is null)
            {
                if (b.Status != BuildingStatus.Complete || b.DueTick != 0 || !Owns(w, b)) throw new InvalidDataException("Invalid initial building");
                continue;
            }
            var entries = w.Ledger.Where(e => e.ActionId == b.IntentId).ToArray();
            if (!w.ProcessedIntentIds.Contains(b.IntentId) || entries.Count(e => e.From == b.OrganizationId && e.To == ActionExecutor.Escrow(b.IntentId) && e.ResourceId == "spirit-stone" && e.Quantity == d.Cost && e.Reason == "BuildingReserved") != 1)
                throw new InvalidDataException("Missing building payment");
            long balance = Inventory.Balance(w, ActionExecutor.Escrow(b.IntentId), "spirit-stone");
            bool pending = b.Status is BuildingStatus.Constructing or BuildingStatus.Suspended;
            var tasks = w.Scheduled.Where(t => t.Intent.Id == b.IntentId).ToArray();
            if (balance != (pending ? d.Cost : 0) || tasks.Length != (pending ? 1 : 0) || pending &&
                (tasks[0].Intent.Kind != ActionKind.ConstructBuilding || tasks[0].Intent.MissionId != d.Id || tasks[0].Intent.OrganizationId != b.OrganizationId || tasks[0].Intent.Quantity != d.Cost || tasks[0].Intent.ResourceId != "spirit-stone" ||
                HexNavigation.Resolve(map, tasks[0].Intent.TargetId) != b.Coord || tasks[0].Intent.DurationTicks != 0 || b.Status == BuildingStatus.Constructing && tasks[0].DueTick != b.DueTick))
                throw new InvalidDataException("Invalid building reservation");
            if (b.Status == BuildingStatus.Complete && (b.DueTick > w.Clock.Tick || entries.Count(e => e.From == ActionExecutor.Escrow(b.IntentId) && e.To == "consumed" && e.Quantity == d.Cost && e.Reason == "BuildingConsumed") != 1) ||
                b.Status == BuildingStatus.Cancelled && entries.Count(e => e.From == ActionExecutor.Escrow(b.IntentId) && e.To == b.OrganizationId && e.Quantity == d.Cost && e.Reason == "BuildingRefund") != 1)
                throw new InvalidDataException("Invalid construction settlement");
        }
        if (w.Scheduled.Any(t => t.Intent.Kind == ActionKind.ConstructBuilding && !map.Buildings.Any(b => b.IntentId == t.Intent.Id && b.Status is BuildingStatus.Constructing or BuildingStatus.Suspended)))
            throw new InvalidDataException("Orphaned construction task");
    }
}
