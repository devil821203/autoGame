using Cultivation.Domain;

namespace Cultivation.Simulation;

internal static class HexIntegrity
{
    internal static void Validate(WorldState w)
    {
        var map = w.HexMap!;
        if (!w.DemoMapEnabled || map.Version != 1 || w.Organizations.Any(o => !Enum.IsDefined(o.Kind)) || map.Tiles.Count is < 1 or > 16384 ||
            map.Movement.PlainsTicks is < 1 or > 1000 || map.Movement.ForestTicks is < 1 or > 1000 || map.Movement.DesertTicks is < 1 or > 1000 ||
            map.Tiles.Select(t => t.Coord).Distinct().Count() != map.Tiles.Count ||
            map.Tiles.Where(t => t.PlaceId is not null).Select(t => t.PlaceId).Distinct().Count() != map.Tiles.Count(t => t.PlaceId is not null))
            throw new InvalidDataException("Invalid hex map or movement rules");
        foreach (var p in new[] { "town", "sect", "grove", "alchemy", "dungeon" })
            if (map.Tiles.SingleOrDefault(t => t.PlaceId == p) is not { } tile || !HexNavigation.Passable(tile)) throw new InvalidDataException("Missing or blocked hex place");
        foreach (var t in map.Tiles)
            if (!Enum.IsDefined(t.Terrain) || t.Coord.Q is < -128 or > 128 || t.Coord.R is < -128 or > 128 ||
                t.LocalOrganizationId is not null && !w.Organizations.Any(o => o.Id == t.LocalOrganizationId))
                throw new InvalidDataException("Invalid hex territory");
        if (map.Journeys.Select(t => t.ActorId).Distinct().Count() != map.Journeys.Count) throw new InvalidDataException("Duplicate hex journeys");
        foreach (var actor in w.Characters)
        {
            var position = HexNavigation.Position(map, actor);
            var tile = map.Tiles.SingleOrDefault(t => t.Coord == position);
            if (tile is null || !HexNavigation.Passable(tile)) throw new InvalidDataException("Invalid actor hex");
            if (!map.Journeys.Any(j => j.ActorId == actor.Id) && HexNavigation.Resolve(map, actor.DemoLocationId) != position)
                throw new InvalidDataException("Hex and logical location disagree");
        }
        foreach (var trip in map.Journeys)
        {
            var actor = w.Characters.SingleOrDefault(c => c.Id == trip.ActorId);
            var task = w.Scheduled.SingleOrDefault(t => t.Intent.Id == trip.IntentId && t.Intent.Kind == ActionKind.Travel);
            if (actor is null || task is null || task.Intent.ActorId != trip.ActorId || task.Intent.TargetId != trip.TargetId ||
                task.Intent.ResourceId != "travel" || task.Intent.Quantity != 1 || task.Intent.DurationTicks != 0 ||
                trip.Remaining.Count == 0 || HexNavigation.Resolve(map, trip.TargetId) != trip.Remaining[^1]) throw new InvalidDataException("Invalid hex itinerary");
            var at = HexNavigation.Position(map, actor);
            foreach (var next in trip.Remaining)
            {
                if (!at.Neighbors().Contains(next) || !map.Tiles.Any(t => t.Coord == next && HexNavigation.Passable(t))) throw new InvalidDataException("Invalid hex route");
                at = next;
            }
        }
        if (w.Scheduled.Count(t => t.Intent.Kind == ActionKind.Travel) != map.Journeys.Count) throw new InvalidDataException("Orphaned hex travel");
        HexBuildingActions.ValidateWorld(w);
        if (map.Sites.Select(s => s.Id).Distinct().Count() != map.Sites.Count) throw new InvalidDataException("Duplicate scene site");
        foreach (var site in map.Sites)
        {
            bool organization = site.Kind is SiteKind.Sect or SiteKind.Gang or SiteKind.Stronghold;
            if (string.IsNullOrWhiteSpace(site.Id) || string.IsNullOrWhiteSpace(site.Name) || string.IsNullOrWhiteSpace(site.MainBuilding) ||
                !Enum.IsDefined(site.Kind) || site.MainLevel is < 1 or > 100 || !map.Tiles.Any(t => t.Coord == site.Anchor && HexNavigation.Passable(t)) ||
                site.Slots.Count > 24 || site.Slots.Select(s => s.Id).Distinct().Count() != site.Slots.Count ||
                site.Slots.Any(s => string.IsNullOrWhiteSpace(s.Id) || s.RequiredLevel is < 1 or > 100) ||
                organization && !w.Organizations.Any(o => o.Id == site.OrganizationId && (int)o.Kind == (int)site.Kind) ||
                !organization && (site.OrganizationId is not null || !map.Tiles.Any(t => t.Coord == site.Anchor && t.Settlement is not null)))
                throw new InvalidDataException("Invalid scene site or slots");
        }
    }
}
