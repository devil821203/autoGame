using Cultivation.Domain;

namespace Cultivation.Simulation;

internal static class HexTravel
{
    internal static string? Validate(WorldState w, ActionIntent i)
    {
        var map = w.HexMap!; var actor = w.Characters.Single(c => c.Id == i.ActorId);
        if (!w.DemoMapEnabled || actor.RegionId != "valley" || i.ResourceId != "travel" || i.Quantity != 1 || i.DurationTicks != 0 || i.MissionId is not null ||
            w.Scheduled.Any(t => t.Intent.ActorId == actor.Id) || HexNavigation.Resolve(map, i.TargetId) is not { } target)
            return "旅行條件不符：請確認角色可行動且目的地有效";
        var path = HexNavigation.Path(map, HexNavigation.Position(map, actor), target);
        if (path is null || path.Count == 0) return "目的地無法抵達，或已位於該地塊";
        return HexNavigation.Duration(w, actor, i.TargetId) > long.MaxValue - w.Clock.Tick ? "旅行時間超出上限" : null;
    }
    internal static void Start(WorldState w, ActionIntent i)
    {
        var map = w.HexMap!; var actor = w.Characters.Single(c => c.Id == i.ActorId);
        var path = HexNavigation.Path(map, HexNavigation.Position(map, actor), HexNavigation.Resolve(map, i.TargetId)!.Value)!;
        map.Journeys.Add(new(actor.Id, i.Id, i.TargetId, path));
        Schedule(w, i, path[0]);
        ActionExecutor.Emit(w, i.Id, "TravelStarted", $"{actor.Id}:{actor.DemoLocationId}->{i.TargetId};hex steps={path.Count}");
    }
    private static void Schedule(WorldState w, ActionIntent i, HexCoord next) => w.Scheduled.Add(new(w.NextSequence++,
        checked(w.Clock.Tick + HexNavigation.Cost(w.HexMap!, w.HexMap!.Tiles.Single(t => t.Coord == next))), i));
    internal static bool CompleteStep(WorldState w, ActionIntent i)
    {
        var map = w.HexMap!; var trip = map.Journeys.Single(t => t.IntentId == i.Id);
        int index = w.Characters.FindIndex(c => c.Id == i.ActorId);
        var actor = w.Characters[index]; var next = trip.Remaining[0];
        var tile = map.Tiles.SingleOrDefault(t => t.Coord == next);
        if (tile is null || !HexNavigation.Passable(tile) || !HexNavigation.Position(map, actor).Neighbors().Contains(next))
        {
            map.Journeys.Remove(trip);
            var here = HexNavigation.Position(map, actor);
            w.Characters[index] = actor with { DemoLocationId = map.Tiles.Single(t => t.Coord == here).PlaceId ?? here.ToString() };
            ActionExecutor.Emit(w, i.Id, "TravelInterrupted", "Next hex is no longer passable"); return true;
        }
        w.Characters[index] = actor with { HexPosition = next };
        map.Journeys.Remove(trip);
        ActionExecutor.Emit(w, i.Id, "HexArrived", $"{actor.Id}:{next}");
        if (trip.Remaining.Count > 1)
        {
            var remaining = trip.Remaining.Skip(1).ToList(); map.Journeys.Add(trip with { Remaining = remaining });
            Schedule(w, i, remaining[0]); return false;
        }
        w.Characters[index] = w.Characters[index] with { DemoLocationId = tile.PlaceId ?? tile.Coord.ToString() };
        ActionExecutor.Emit(w, i.Id, "TravelCompleted", $"{actor.Id}:{i.TargetId}"); return true;
    }
}
