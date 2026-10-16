using Cultivation.Domain;

namespace Cultivation.Simulation;

public static class WorldIntegrity
{
    public static void Validate(WorldState world)
    {
        if (world.SchemaVersion != 1 || world.Clock.Tick < 0 || world.Clock.SimulatedSecondsPerTick <= 0 || world.RandomState == 0)
            throw new InvalidDataException("Unsupported schema or invalid clock/random state.");
        var owners = world.Characters.Select(c => c.Id).Concat(world.Organizations.Select(o => o.Id)).ToArray();
        if (owners.Any(id => string.IsNullOrWhiteSpace(id) || id.StartsWith("$", StringComparison.Ordinal)) || owners.Distinct().Count() != owners.Length ||
            !world.Characters.Any(c => c.Id == world.ControlledCharacterId))
            throw new InvalidDataException("Invalid owners or player assignment.");
        var sequences = world.Ledger.Select(e => e.Sequence).Concat(world.Events.Select(e => e.Sequence))
            .Concat(world.Pending.Select(e => e.Sequence)).Concat(world.Scheduled.Select(e => e.Sequence)).ToArray();
        if (sequences.Any(s => s <= 0 || s >= world.NextSequence) || sequences.Distinct().Count() != sequences.Length)
            throw new InvalidDataException("Invalid sequence state.");
        if (world.ProcessedIntentIds.Distinct(StringComparer.Ordinal).Count() != world.ProcessedIntentIds.Count)
            throw new InvalidDataException("Duplicate processed IDs.");
        var balances = new Dictionary<(string, string), long>();
        foreach (var entry in world.Ledger.OrderBy(e => e.Sequence))
        {
            if (entry.Quantity <= 0 || string.IsNullOrWhiteSpace(entry.ResourceId) || entry.Tick < 0 || entry.Tick > world.Clock.Tick || entry.From == entry.To)
                throw new InvalidDataException("Invalid ledger entry.");
            bool Known(string owner) => owners.Contains(owner) || owner.StartsWith("$escrow:", StringComparison.Ordinal);
            if (!Known(entry.To) || (!Known(entry.From) && entry.From != "$genesis"))
                throw new InvalidDataException("Unknown ledger account.");
            if (entry.From == "$genesis")
            {
                if (entry.Tick != 0 || entry.Reason != "Genesis") throw new InvalidDataException("Invalid issuance.");
            }
            else
            {
                var key = (entry.From, entry.ResourceId);
                balances[key] = checked(balances.GetValueOrDefault(key) - entry.Quantity);
                if (balances[key] < 0) throw new InvalidDataException("Negative inventory.");
            }
            var recipient = (entry.To, entry.ResourceId);
            balances[recipient] = checked(balances.GetValueOrDefault(recipient) + entry.Quantity);
        }
        var tasks = world.Scheduled;
        if (tasks.Select(t => t.Intent.Id).Distinct().Count() != tasks.Count)
            throw new InvalidDataException("Duplicate scheduled action.");
        foreach (var task in tasks)
        {
            if (task.DueTick <= world.Clock.Tick || !world.ProcessedIntentIds.Contains(task.Intent.Id) ||
                !owners.Contains(task.Intent.TargetId) || task.Intent.Quantity <= 0 ||
                Inventory.Balance(world, ActionExecutor.Escrow(task.Intent.Id), task.Intent.ResourceId) != task.Intent.Quantity)
                throw new InvalidDataException("Invalid scheduled reservation.");
        }
        foreach (var pair in balances.Where(p => p.Key.Item1.StartsWith("$escrow:", StringComparison.Ordinal) && p.Value != 0))
            if (!tasks.Any(t => ActionExecutor.Escrow(t.Intent.Id) == pair.Key.Item1 && t.Intent.ResourceId == pair.Key.Item2 && t.Intent.Quantity == pair.Value))
                throw new InvalidDataException("Orphaned reservation.");
    }
}
