using Cultivation.Domain;

namespace Cultivation.Simulation;

internal static class EconomyIntegrity
{
    internal static void Validate(WorldState w)
    {
        var ledgerByAction = w.Ledger.GroupBy(e => e.ActionId).ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
        bool Owner(string id) => w.Characters.Any(c => c.Id == id) || w.Organizations.Any(o => o.Id == id);
        if (w.MarketListings.Select(l => (l.MarketId, l.ResourceId)).Distinct().Count() != w.MarketListings.Count ||
            w.MarketListings.Any(l => !w.Organizations.Any(o => o.Id == l.MarketId && o.RegionId == l.RegionId) || l.ResourceId != "herb" && l.ResourceId != w.RecoveryResourceId || l.MinimumPrice <= 0 || l.MaximumPrice < l.MinimumPrice || l.BasePrice < l.MinimumPrice || l.BasePrice > l.MaximumPrice || l.TargetStock < 0 || l.StockPriceStep < 0 || l.Spread < l.StockPriceStep))
            throw new InvalidDataException("Invalid market listings.");
        if (w.Trades.Select(t => t.ActionId).Distinct().Count() != w.Trades.Count || w.Deliveries.Select(d => d.ActionId).Distinct().Count() != w.Deliveries.Count)
            throw new InvalidDataException("Duplicate economy receipt.");
        foreach (var trade in w.Trades)
        {
            if (!Owner(trade.ActorId) || !Owner(trade.MarketId) || trade.Kind is not (ActionKind.Buy or ActionKind.Sell) || trade.Quantity != 1 || trade.UnitPrice <= 0 || trade.Tick > w.Clock.Tick || trade.Tick < 0)
                throw new InvalidDataException("Invalid trade receipt.");
            string buyer = trade.Kind == ActionKind.Buy ? trade.ActorId : trade.MarketId;
            string seller = trade.Kind == ActionKind.Buy ? trade.MarketId : trade.ActorId;
            var entries = ledgerByAction.GetValueOrDefault(trade.ActionId) ?? [];
            if (!entries.Any(e => e.From == seller && e.To == buyer && e.ResourceId == trade.ResourceId && e.Quantity == trade.Quantity && e.Reason == "Trade") ||
                !entries.Any(e => e.From == buyer && e.To == seller && e.ResourceId == "spirit-stone" && e.Quantity == trade.UnitPrice && e.Reason == "Payment"))
                throw new InvalidDataException("Trade receipt does not match ledger.");
        }
        if (w.Missions.Select(m => m.Id).Distinct().Count() != w.Missions.Count) throw new InvalidDataException("Duplicate mission.");
        foreach (var mission in w.Missions)
        {
            if (string.IsNullOrWhiteSpace(mission.Id) || mission.Id.StartsWith('$') || !w.Organizations.Any(o => o.Id == mission.OrganizationId && o.RegionId == mission.RegionId) ||
                mission.Required <= 0 || mission.UnitPrice <= 0 || mission.Delivered < 0 || mission.Delivered > mission.Required || mission.DeadlineTick <= 0 || !Enum.IsDefined(mission.Status) ||
                mission.Participants.Distinct().Count() != mission.Participants.Count || mission.Participants.Any(p => !w.Characters.Any(c => c.Id == p)))
                throw new InvalidDataException("Invalid mission.");
            if (checked(mission.Required * mission.UnitPrice) != mission.ReservedBudget ||
                (mission.Status == MissionStatus.Active && (mission.DeadlineTick <= w.Clock.Tick || mission.Delivered == mission.Required)) ||
                (mission.Status == MissionStatus.Completed && mission.Delivered != mission.Required)) throw new InvalidDataException("Invalid mission lifecycle.");
            long expected = mission.Status == MissionStatus.Active ? mission.ReservedBudget - checked(mission.Delivered * mission.UnitPrice) : 0;
            if (Inventory.Balance(w, MissionActions.Account(mission.Id), "spirit-stone") != expected) throw new InvalidDataException("Invalid mission reservation.");
            if (w.Deliveries.Where(d => d.MissionId == mission.Id).Sum(d => d.Quantity) != mission.Delivered) throw new InvalidDataException("Delivery total mismatch.");
        }
        foreach (var delivery in w.Deliveries)
        {
            var mission = w.Missions.SingleOrDefault(m => m.Id == delivery.MissionId);
            var entries = ledgerByAction.GetValueOrDefault(delivery.ActionId) ?? [];
            if (mission is null || !mission.Participants.Contains(delivery.ActorId) || delivery.Quantity <= 0 || delivery.Tick < 0 || delivery.Tick >= mission.DeadlineTick || delivery.Tick > w.Clock.Tick || delivery.Payment != checked(delivery.Quantity * mission.UnitPrice) ||
                !entries.Any(e => e.From == delivery.ActorId && e.To == mission.OrganizationId && e.ResourceId == mission.ResourceId && e.Quantity == delivery.Quantity && e.Reason == "MissionDelivery") ||
                !entries.Any(e => e.From == MissionActions.Account(mission.Id) && e.To == delivery.ActorId && e.ResourceId == "spirit-stone" && e.Quantity == delivery.Payment && e.Reason == "MissionPayment"))
                throw new InvalidDataException("Invalid delivery receipt.");
        }
    }
}
