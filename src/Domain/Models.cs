namespace Cultivation.Domain;

public enum DecisionSource { Player, Npc }
public sealed record CharacterState(string Id, string Name);
public sealed record OrganizationState(string Id, string Name);
public readonly record struct SimulationTick(long Value);
public sealed record GameClock(long Tick, int SimulatedSecondsPerTick);
public sealed record ActionIntent(string Id, string ActorId, string TargetId,
    string ResourceId, long Quantity, long DurationTicks, DecisionSource Source);
public sealed record QueuedIntent(long Sequence, ActionIntent Intent);
public sealed record ScheduledTransfer(long Sequence, long DueTick, ActionIntent Intent);
public sealed record LedgerEntry(long Sequence, long Tick, string ActionId,
    string From, string To, string ResourceId, long Quantity, string Reason);
public sealed record WorldEvent(long Sequence, long Tick, string ActionId, string Kind, string Detail);
public sealed record ActionResult(string IntentId, bool Accepted, string Reason);

// Detached persistence DTO. Simulation never returns its live instance.
public sealed class WorldState
{
    public int SchemaVersion { get; set; } = 1;
    public GameClock Clock { get; set; } = new(0, 60);
    public ulong RandomState { get; set; } = 1;
    public long NextSequence { get; set; } = 1;
    public string ControlledCharacterId { get; set; } = "player";
    public List<CharacterState> Characters { get; set; } = [];
    public List<OrganizationState> Organizations { get; set; } = [];
    public List<LedgerEntry> Ledger { get; set; } = [];
    public List<WorldEvent> Events { get; set; } = [];
    public List<QueuedIntent> Pending { get; set; } = [];
    public List<ScheduledTransfer> Scheduled { get; set; } = [];
    public List<string> ProcessedIntentIds { get; set; } = [];
}

public static class Inventory
{
    // The ledger is the only source of balances in phase 0.
    public static long Balance(WorldState world, string owner, string resource)
    {
        long balance = 0;
        foreach (var entry in world.Ledger.Where(e => e.ResourceId == resource))
        {
            if (entry.To == owner) balance = checked(balance + entry.Quantity);
            if (entry.From == owner) balance = checked(balance - entry.Quantity);
        }
        return balance;
    }
}
