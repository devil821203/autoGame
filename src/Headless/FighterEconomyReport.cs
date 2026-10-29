using System.Text.Json;
using Cultivation.Domain;
using Cultivation.Simulation;

namespace Cultivation.Headless;

public static class FighterEconomyReport
{
    public static void Run(int ticks, ulong seed, string mode = "growth")
    {
        if (ticks < 0) throw new ArgumentOutOfRangeException(nameof(ticks));
        var initial = mode switch { "growth" => GrowthScenario.Create(seed: seed), "hex" => HexWorldScenario.Create(seed: seed),
            "sect" => SectEconomyScenario.Create(seed: seed), _ => throw new ArgumentException("Unknown report mode") };
        initial = MedicineOnlyProfile.ApplyFresh(initial);
        var fighters = initial.Characters.Where(c => c.Aspiration == Aspiration.Combat && !c.Hostile).ToArray();
        var engine = new SimulationEngine(initial);
        var samples = new List<object>();
        void Sample(WorldState world) => samples.Add(new { Tick = world.Clock.Tick,
            SectStones = Inventory.Balance(world, "sect", "spirit-stone"),
            Fighters = fighters.Select(f => { var c = world.Characters.Single(c => c.Id == f.Id); return new {
                c.Id, c.OrganizationId, c.Health, c.Cultivation, c.DemoLocationId,
                Stamina = c.Stamina?.Current, Stones = Inventory.Balance(world, c.Id, "spirit-stone"),
                Points = Inventory.Balance(world, c.Id, "contribution"), Pills = Inventory.Balance(world, c.Id, "pill"), Medicine=Inventory.Balance(world,c.Id,world.RecoveryResourceId),
                Herbs = Inventory.Balance(world, c.Id, "herb"), Hides = Inventory.Balance(world, c.Id, "beast-hide") }; }).ToArray() });
        Sample(initial);
        for (int n = 1; n <= ticks; n++) { engine.Step(true); if (n % 50 == 0 || n == ticks) Sample(engine.Snapshot()); }
        var w = engine.Snapshot(); var entries = w.Ledger.Where(l => l.Sequence >= initial.NextSequence).ToArray();
        var accounts = fighters.Select(f => {
            long start = Inventory.Balance(initial, f.Id, "spirit-stone"), end = Inventory.Balance(w, f.Id, "spirit-stone");
            var income = entries.Where(l => l.ResourceId == "spirit-stone" && l.To == f.Id).ToArray();
            var expense = entries.Where(l => l.ResourceId == "spirit-stone" && l.From == f.Id).ToArray();
            if (start + income.Sum(l => l.Quantity) - expense.Sum(l => l.Quantity) != end) throw new InvalidDataException("Stone reconciliation failed: " + f.Id);
            var actor = w.Characters.Single(c => c.Id == f.Id);
            var reserved = entries.Where(l => l.From == f.Id && l.Reason == "Reserved").Select(l => l.ActionId).ToHashSet();
            var kinds = w.Events.Where(v => v.Kind == "Scheduled").GroupBy(v => v.ActionId).ToDictionary(g => g.Key, g => g.Last().Detail.Split(':').Last());
            string Use(LedgerEntry l) => w.Events.LastOrDefault(v => v.ActionId == l.ActionId && v.Kind is "CombatHeal" or "PowerStrike")?.Kind
                ?? (kinds.TryGetValue(l.ActionId, out var kind) ? kind : "Unknown");
            return new { f.Id, f.Name, Start = start, End = end, Income = income.Sum(l => l.Quantity), Expense = expense.Sum(l => l.Quantity),
                IncomeByReason = income.GroupBy(l => new { l.Reason, l.From }).Select(g => new { g.Key.Reason, Counterparty = g.Key.From, Amount = g.Sum(l => l.Quantity) }),
                ExpenseByReason = expense.GroupBy(l => new { l.Reason, l.To }).Select(g => new { g.Key.Reason, Counterparty = g.Key.To, Amount = g.Sum(l => l.Quantity) }),
                Trades = w.Trades.Where(t => t.ActorId == f.Id),
                Loot = entries.Where(l => l.To == f.Id && l.Reason is "CombatLoot" or "PartyLoot").GroupBy(l => l.ResourceId).Select(g => new { Resource = g.Key, Quantity = g.Sum(l => l.Quantity) }),
                Consumed = entries.Where(l => reserved.Contains(l.ActionId) && l.Reason == "Consumed").GroupBy(l => l.ResourceId).Select(g => new { Resource = g.Key, Quantity = g.Sum(l => l.Quantity) }),
                PillConsumption = entries.Where(l => l.ResourceId == "pill" && l.To == "consumed" && (l.From == f.Id || reserved.Contains(l.ActionId)))
                    .GroupBy(Use).Select(g => new { Action = g.Key, Quantity = g.Sum(l => l.Quantity) }),
                PillReceipts = entries.Where(l => l.ResourceId == "pill" && l.To == f.Id).GroupBy(l => l.Reason).Select(g => new { Reason = g.Key, Quantity = g.Sum(l => l.Quantity) }),
                RemainingPills = Inventory.Balance(w, f.Id, "pill"), RemainingContribution = Inventory.Balance(w, f.Id, "contribution"),
                RemainingHides = Inventory.Balance(w, f.Id, "beast-hide"),
                Stats = CharacterProgression.Stats(actor, w.Advancement), actor.AlchemyChance, actor.CombatExperience,
                actor.OrganizationId, actor.DemoLocationId, actor.Health, actor.Cultivation,
                LastDecision = w.Events.LastOrDefault(v => v.Kind == "Decision" && v.ActionId.StartsWith("ai:" + f.Id + ":", StringComparison.Ordinal))?.Detail };
        }).ToArray();
        Console.WriteLine(JsonSerializer.Serialize(new { seed, ticks, mode, Reconciled = true, Accounts = accounts, Samples = samples,
            PayrollPaid = w.Events.Count(v => v.Kind == "PayrollPaid"), PayrollSkipped = w.Events.Count(v => v.Kind == "PayrollSkipped"),
            DutyExpulsions = w.Events.Where(v => v.Kind == "DutyExpelled"),
            Dungeon = w.Dungeon, PayrollRules = w.SectPayroll, CombatRules = w.CombatRules }, new JsonSerializerOptions { WriteIndented = true }));
    }
}


