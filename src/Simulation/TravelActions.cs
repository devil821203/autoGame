using Cultivation.Domain;

namespace Cultivation.Simulation;

internal static class TravelActions
{
    internal static string? RequiredLocation(ActionKind kind) => kind switch
    {
        ActionKind.Gather => "grove",
        ActionKind.Buy or ActionKind.Sell => "town",
        ActionKind.ExploreDungeon => "dungeon",
        ActionKind.AcceptHunt => "sect",
        ActionKind.SectGather => "grove",
        ActionKind.SectBrew => "alchemy",
        ActionKind.AcceptSectWork or ActionKind.BuyContribution or ActionKind.RedeemContribution or ActionKind.SetSectStockReservation => "sect",
        ActionKind.PublishMission or ActionKind.AcceptMission or ActionKind.DeliverMission or ActionKind.CancelMission => "sect",
        _ => null
    };
    internal static string? Validate(WorldState w, ActionIntent i)
    {
        if (w.HexMap is not null) return HexTravel.Validate(w, i);
        var actor = w.Characters.Single(c => c.Id == i.ActorId);
        if (!w.DemoMapEnabled || actor.RegionId != "valley" || !DemoMap.Contains(i.TargetId) || i.TargetId == "dungeon" && w.Dungeon is null || i.TargetId == "alchemy" && w.SectFacilities is null ||
            i.TargetId == actor.DemoLocationId || i.ResourceId != "travel" || i.Quantity != 1 || i.DurationTicks != 0 || i.MissionId is not null ||
            w.Scheduled.Any(t => t.Intent.ActorId == actor.Id)) return "Invalid or unavailable demo travel";
        return DemoMap.TravelTicks(actor.DemoLocationId, i.TargetId, w.MapRules) > long.MaxValue - w.Clock.Tick ? "Travel time overflow" : null;
    }
    internal static void Start(WorldState w, ActionIntent i)
    {
        if (w.HexMap is not null) { HexTravel.Start(w, i); return; }
        var actor = w.Characters.Single(c => c.Id == i.ActorId);
        w.Scheduled.Add(new(w.NextSequence++, w.Clock.Tick + DemoMap.TravelTicks(actor.DemoLocationId, i.TargetId, w.MapRules), i));
        ActionExecutor.Emit(w, i.Id, "TravelStarted", $"{actor.Id}:{actor.DemoLocationId}->{i.TargetId}");
    }
    internal static void Complete(WorldState w, ActionIntent i)
    {
        int index = w.Characters.FindIndex(c => c.Id == i.ActorId);
        w.Characters[index] = w.Characters[index] with { DemoLocationId = i.TargetId };
        ActionExecutor.Emit(w, i.Id, "TravelCompleted", $"{i.ActorId}:{i.TargetId}");
    }
    internal static void ValidateWorld(WorldState w)
    {
        if (w.HexMap is not null) { HexIntegrity.Validate(w); return; }
        if (w.Characters.Any(c => c.HexPosition is not null) || w.Scheduled.Any(t => t.Intent.Kind == ActionKind.ConstructBuilding))
            throw new InvalidDataException("Hex state without hex map");
        if (w.DemoMapEnabled && w.Characters.Any(c => !DemoMap.Contains(c.DemoLocationId))) throw new InvalidDataException("Invalid demo location");
        foreach (var task in w.Scheduled.Where(s => s.Intent.Kind == ActionKind.Travel))
            if (!w.DemoMapEnabled || !DemoMap.Contains(task.Intent.TargetId) || task.Intent.ResourceId != "travel" || task.Intent.Quantity != 1 ||
                task.Intent.DurationTicks != 0 || task.Intent.MissionId is not null || !w.Characters.Any(c => c.Id == task.Intent.ActorId && c.DemoLocationId != task.Intent.TargetId))
                throw new InvalidDataException("Invalid demo travel schedule");
    }
}
