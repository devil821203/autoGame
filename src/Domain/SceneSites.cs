namespace Cultivation.Domain;

public enum SiteKind { Sect = 1, Gang = 2, Stronghold = 3, Village = 4, Town = 5, City = 6 }
public sealed record SiteSlot(string Id, int RequiredLevel);
// Persistent scene identity and layout metadata. No duplicate inventories or residents.
public sealed record SceneSite(string Id, string Name, SiteKind Kind, HexCoord Anchor,
    string MainBuilding, int MainLevel, List<SiteSlot> Slots, string? OrganizationId = null);

public static class SceneSites
{
    public static IEnumerable<SceneSite> At(WorldState w, HexCoord coord) => w.HexMap?.Sites.Where(s => s.Anchor == coord) ?? [];
    public static bool CanEnter(WorldState w, SceneSite site)
    {
        if (w.HexMap is not { } map || map.Sites.SingleOrDefault(s => s.Id == site.Id) is not { } actual) return false;
        var player = w.Characters.Single(c => c.Id == w.ControlledCharacterId);
        return player.RegionId == "valley" && HexNavigation.Position(map, player) == actual.Anchor &&
            !w.Scheduled.Any(t => t.Intent.ActorId == player.Id && t.Intent.Kind == ActionKind.Travel);
    }
}
