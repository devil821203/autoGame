using Cultivation.Domain;

namespace Cultivation.Simulation;

internal sealed class WorldSpatialIndex(WorldState world)
{
    internal Dictionary<string, CharacterState> ById { get; } = world.Characters.ToDictionary(c => c.Id, StringComparer.Ordinal);
    internal ILookup<string, CharacterState> ByRegion { get; } = world.Characters.ToLookup(c => c.RegionId, StringComparer.Ordinal);
    internal ILookup<(string Region, string Location), CharacterState> ByLocation { get; } = world.Characters.ToLookup(c => (c.RegionId, c.DemoLocationId));
    internal HashSet<string> ScheduledActors { get; } = world.Scheduled.Select(s => s.Intent.ActorId).ToHashSet(StringComparer.Ordinal);
    internal Dictionary<string, long> LatestNotifications { get; } = Latest(world);
    private static Dictionary<string, long> Latest(WorldState w)
    {
        var ticks = w.Events.ToDictionary(e => e.Sequence, e => e.Tick);
        return w.Notifications.GroupBy(n => n.RecipientId, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Max(n => ticks.GetValueOrDefault(n.EventSequence, -1)), StringComparer.Ordinal);
    }
}
