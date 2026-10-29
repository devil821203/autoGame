namespace Cultivation.Domain;

public readonly record struct HexCoord(int Q, int R)
{
    public IEnumerable<HexCoord> Neighbors()
    {
        foreach (var d in new (int Q, int R)[] { (1, 0), (1, -1), (0, -1), (-1, 0), (-1, 1), (0, 1) })
            yield return new(Q + d.Q, R + d.R);
    }
    public override string ToString() => $"hex:{Q}:{R}";
}
public enum HexTerrain { Plains, Desert, Lake }
// Keep existing numeric IDs: removed country kind 0 is deliberately not reused.
public enum OrganizationKind { Sect = 1, Gang = 2, Stronghold = 3 }
public sealed record HexTile(HexCoord Coord, HexTerrain Terrain = HexTerrain.Plains, bool Mountain = false,
    bool Forest = false, string? Settlement = null, string? PlaceId = null,
    string? LocalOrganizationId = null);
public sealed record HexMovementRules(int PlainsTicks = 1, int ForestTicks = 2, int DesertTicks = 3);
public sealed class HexMapState
{
    public List<SceneSite> Sites { get; set; } = [];
    public int Version { get; set; } = 1;
    public HexMovementRules Movement { get; set; } = new();
    public List<HexTile> Tiles { get; set; } = [];
    public List<HexJourney> Journeys { get; set; } = [];
    public List<BuildingDefinition> BuildingDefinitions { get; set; } = [];
    public List<HexBuilding> Buildings { get; set; } = [];
}
public sealed record HexJourney(string ActorId, string IntentId, string TargetId, List<HexCoord> Remaining);
public sealed record BuildingDefinition(string Id, string Name, OrganizationKind Kind, long Cost, long Ticks, string? Service = null);
public enum BuildingStatus { Constructing, Complete, Cancelled, Suspended }
public sealed record HexBuilding(string Id, string DefinitionId, string OrganizationId, HexCoord Coord,
    BuildingStatus Status, long DueTick = 0, string? IntentId = null);

public static class HexNavigation
{
    public static bool Passable(HexTile tile) => !tile.Mountain && tile.Terrain != HexTerrain.Lake;
    public static int Cost(HexMapState map, HexTile tile) => tile.Forest ? map.Movement.ForestTicks :
        tile.Terrain == HexTerrain.Desert ? map.Movement.DesertTicks : map.Movement.PlainsTicks;
    public static HexCoord? Resolve(HexMapState map, string id)
    {
        var place = map.Tiles.FirstOrDefault(t => t.PlaceId == id);
        if (place is not null) return place.Coord;
        var parts = id.Split(':');
        return parts.Length == 3 && parts[0] == "hex" && int.TryParse(parts[1], out int q) && int.TryParse(parts[2], out int r)
            && map.Tiles.Any(t => t.Coord == new HexCoord(q, r)) ? new(q, r) : null;
    }
    public static HexCoord Position(HexMapState map, CharacterState actor) => actor.HexPosition ?? Resolve(map, actor.DemoLocationId)
        ?? throw new InvalidDataException("Missing hex position");
    // Stable Dijkstra search: deterministic ties; all costs are positive integer Ticks.
    public static List<HexCoord>? Path(HexMapState map, HexCoord from, HexCoord to)
    {
        var tiles = map.Tiles.ToDictionary(t => t.Coord);
        if (!tiles.ContainsKey(from) || !tiles.TryGetValue(to, out var goal) || !Passable(goal)) return null;
        var frontier = new PriorityQueue<HexCoord, (long Cost, int Q, int R)>();
        var costs = new Dictionary<HexCoord, long> { [from] = 0 };
        var previous = new Dictionary<HexCoord, HexCoord>();
        frontier.Enqueue(from, (0, from.Q, from.R));
        while (frontier.TryDequeue(out var at, out var priority))
        {
            if (priority.Cost != costs[at]) continue;
            if (at == to)
            {
                var path = new List<HexCoord>();
                while (at != from) { path.Add(at); at = previous[at]; }
                path.Reverse(); return path;
            }
            foreach (var next in at.Neighbors())
            {
                if (!tiles.TryGetValue(next, out var tile) || !Passable(tile)) continue;
                long cost = checked(costs[at] + Cost(map, tile));
                if (costs.TryGetValue(next, out long old) && old <= cost) continue;
                costs[next] = cost; previous[next] = at; frontier.Enqueue(next, (cost, next.Q, next.R));
            }
        }
        return null;
    }
    public static long Duration(WorldState world, CharacterState actor, string target)
    {
        if (world.HexMap is not { } map) return DemoMap.TravelTicks(actor.DemoLocationId, target, world.MapRules);
        if (Resolve(map, target) is not { } to || Path(map, Position(map, actor), to) is not { } path) return long.MaxValue;
        return path.Sum(c => (long)Cost(map, map.Tiles.Single(t => t.Coord == c)));
    }
}
