using Cultivation.Domain;

namespace Cultivation.Simulation;

public static class SettlementDailyEconomy
{
    public static long Reserved(WorldState w, SettlementState s, string item)
    {
        if (w.SettlementDailyEconomy is null) return 0;
        return Reserved(w, s.Level, item);
    }
    private static long Reserved(WorldState w, int level, string item)
    {
        var next = w.SettlementRules!.Levels.SingleOrDefault(l => l.Level == level + 1);
        return item switch { "wood" => next?.Wood ?? 0, "spirit-stone" => next?.Stones ?? 0, _ => 0 };
    }
    public static long Available(WorldState w, SettlementState s, string owner, string item) =>
        Math.Max(0, Inventory.Balance(w, owner, item) - (owner == s.OrganizationId ? Reserved(w, s, item) : 0));

    internal static void Advance(WorldState w, SettlementState s)
    {
        if (w.SettlementDailyEconomy is not { } rules) return;
        foreach (var rule in rules.Consumption)
        {
            string id = $"settlement-consumption:{s.Id}:{w.Clock.Tick}:{rule.ResourceId}";
            if (w.SettlementConsumption.Any(r => r.Id == id)) continue;
            long before = Inventory.Balance(w, s.OrganizationId, rule.ResourceId);
            long reserve = Reserved(w, s, rule.ResourceId);
            long amount = Math.Min(checked(s.Population * rule.MaximumUnits), Math.Max(0, before - reserve));
            long income = checked(amount * rule.MoneyPerUnit);
            if (amount > 0)
            {
                w.Ledger.Add(new(w.NextSequence++, w.Clock.Tick, id, s.OrganizationId, "consumed", rule.ResourceId, amount, "SettlementDailyConsumption"));
                w.Ledger.Add(new(w.NextSequence++, w.Clock.Tick, id, "$settlement-residents", s.OrganizationId, "spirit-stone", income, "SettlementConsumptionIncome"));
            }
            w.SettlementConsumption.Add(new(id, s.Id, w.Clock.Tick, s.Population, s.Level, rule.ResourceId, before, reserve, amount, income));
            ActionExecutor.Emit(w, id, "SettlementConsumptionCompleted", $"{s.Id}:{rule.ResourceId}={amount}:income={income}:reserved={reserve}");
        }
        SettlementPopulationGrowth.Advance(w,s);
    }

    internal static void Validate(WorldState w)
    {
        if (w.SettlementDailyEconomy is not { } rules)
        {
            if (w.SettlementConsumption.Count > 0) throw new InvalidDataException("Settlement consumption receipts without rules");
            return;
        }
        if (w.SettlementOperationRules?.FixedDailySettlement != true || w.Time is null || rules.Consumption is null ||
            rules.Consumption.Count != 2 || rules.Consumption.Any(r => r is null || r.ResourceId is not ("food" or "wood") ||
                r.UnitsPerResident <= 0 || r.MaximumUnits < r.UnitsPerResident || r.MoneyPerUnit <= 0 || r.MaximumUnits > long.MaxValue / int.MaxValue ||
                r.MoneyPerUnit > long.MaxValue / (int.MaxValue * r.MaximumUnits)) ||
            rules.Consumption.Select(r => r.ResourceId).Distinct().Count() != 2)
            throw new InvalidDataException("settlements/daily-economy.json: invalid Consumption ResourceId/UnitsPerResident/MoneyPerUnit or fixed daily settlement unavailable");
        if (w.SettlementConsumption.Select(r => r.Id).Distinct().Count() != w.SettlementConsumption.Count ||
            w.SettlementConsumption.Select(r => (r.SettlementId, r.Tick, r.ResourceId)).Distinct().Count() != w.SettlementConsumption.Count)
            throw new InvalidDataException("Duplicate settlement daily consumption");
        var ledger = w.Ledger.ToLookup(e => e.ActionId);
        var receipts = w.SettlementConsumption.ToDictionary(r => r.Id);
        foreach (var r in w.SettlementConsumption)
        {
            var s = w.Settlements.SingleOrDefault(s => s.Id == r.SettlementId);
            var rule = rules.Consumption.SingleOrDefault(c => c.ResourceId == r.ResourceId);
            if (s is null || rule is null || r.Tick <= 0 || r.Tick > w.Clock.Tick || r.Tick % w.Time.Rules.TicksPerDay != 0 ||
                r.Population < 0 || r.SettlementLevel < w.SettlementRules!.InitialLevel || r.SettlementLevel > s.Level || r.StockBefore < 0 ||
                r.ReservedQuantity != Reserved(w, r.SettlementLevel, r.ResourceId) ||
                r.Quantity != Math.Min(checked(r.Population * rule.MaximumUnits), Math.Max(0, r.StockBefore - r.ReservedQuantity)) ||
                r.Income != checked(r.Quantity * rule.MoneyPerUnit))
                throw new InvalidDataException("Invalid settlement daily consumption receipt");
            var entries = ledger[r.Id].ToList();
            if (r.Quantity == 0 ? entries.Count != 0 : entries.Count != 2 ||
                !entries.Any(e => e.Tick == r.Tick && e.From == s.OrganizationId && e.To == "consumed" && e.ResourceId == r.ResourceId && e.Quantity == r.Quantity && e.Reason == "SettlementDailyConsumption") ||
                !entries.Any(e => e.Tick == r.Tick && e.From == "$settlement-residents" && e.To == s.OrganizationId && e.ResourceId == "spirit-stone" && e.Quantity == r.Income && e.Reason == "SettlementConsumptionIncome"))
                throw new InvalidDataException("Invalid settlement consumption payment backing");
        }
        var publicAccounts = w.Settlements.Select(s => s.OrganizationId).ToHashSet(StringComparer.Ordinal);
        var balances = new Dictionary<(string Owner, string Item), long>();
        foreach (var e in w.Ledger.OrderBy(e => e.Sequence))
        {
            if (e.Reason == "SettlementDailyConsumption")
            {
                if (!receipts.TryGetValue(e.ActionId, out var receipt) || receipt.StockBefore != balances.GetValueOrDefault((e.From, e.ResourceId)))
                    throw new InvalidDataException("Settlement consumption stock history mismatch");
            }
            if (publicAccounts.Contains(e.From)) balances[(e.From, e.ResourceId)] = checked(balances.GetValueOrDefault((e.From, e.ResourceId)) - e.Quantity);
            if (publicAccounts.Contains(e.To)) balances[(e.To, e.ResourceId)] = checked(balances.GetValueOrDefault((e.To, e.ResourceId)) + e.Quantity);
        }
    }
}
