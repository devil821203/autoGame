using Cultivation.Domain;

namespace Cultivation.Simulation;

public sealed record HexLayout(int Width, int Height, List<HexFeature> Features, List<HexTerritory> Territories);
public sealed record HexFeature(int Q, int R, HexTerrain Terrain = HexTerrain.Plains, bool Mountain = false,
    bool Forest = false, string? Settlement = null, string? PlaceId = null);
public sealed record HexTerritory(string OrganizationId, int Q, int R, int Width, int Height);

public static class HexWorldScenario
{
    public static WorldState Create(string name = "行者", ulong seed = 12345, GameConfiguration? configuration = null)
    {
        var config = configuration ?? GameConfiguration.Find();
        var w = SectEconomyScenario.Create(name, seed, config);
        var layout = config.Read<HexLayout>("map/layout.json");
        if (layout.Width is < 2 or > 128 || layout.Height is < 2 or > 128 ||
            layout.Features.Select(f => (f.Q, f.R)).Distinct().Count() != layout.Features.Count ||
            layout.Features.Any(f => f.Q < 0 || f.Q >= layout.Width || f.R < 0 || f.R >= layout.Height) ||
            layout.Territories.Any(t => t.Width < 1 || t.Height < 1 || t.Q < 0 || t.R < 0 || t.Width > layout.Width - t.Q || t.Height > layout.Height - t.R))
            throw new InvalidDataException("Invalid hex layout");
        var map = new HexMapState { Movement = config.Read<HexMovementRules>("map/movement.json"),
            BuildingDefinitions = config.Read<List<BuildingDefinition>>("buildings/definitions.json") };
        for (int r = 0; r < layout.Height; r++) for (int q = 0; q < layout.Width; q++)
        {
            var f = layout.Features.SingleOrDefault(f => f.Q == q && f.R == r) ?? new(q, r);
            var rights = layout.Territories.Where(t => q >= t.Q && q < t.Q + t.Width && r >= t.R && r < t.R + t.Height).ToArray();
            if (rights.Length > 1) throw new InvalidDataException("Overlapping organization territory");
            map.Tiles.Add(new(new(q, r), f.Terrain, f.Mountain, f.Forest, f.Settlement, f.PlaceId,
                rights.SingleOrDefault()?.OrganizationId));
        }
        w.HexMap = map;
        map.Sites = config.Read<List<SceneSite>>("scenes/sites.json");
        // Explicit new-world roles for testing construction; old worlds are untouched.
        var organizations = config.Read<List<OrganizationState>>("organizations/hex-world.json");
        if (organizations.Select(o => o.Id).Distinct().Count() != organizations.Count) throw new InvalidDataException("Duplicate hex organization");
        foreach (var organization in organizations)
        {
            w.Organizations.RemoveAll(o => o.Id == organization.Id); w.Organizations.Add(organization);
        }
        config.ApplyCharacters(w, "hex-world");
        w.Characters = w.Characters.Select(c => c with { HexPosition = HexNavigation.Resolve(map, c.DemoLocationId) }).ToList();
        foreach (var d in map.BuildingDefinitions)
            if (d.Service is { } place && HexNavigation.Resolve(map, place) is { } coord)
                map.Buildings.Add(new("initial:" + d.Id, d.Id, "sect", coord, BuildingStatus.Complete));
        WorldIntegrity.Validate(w); return w;
    }
}
