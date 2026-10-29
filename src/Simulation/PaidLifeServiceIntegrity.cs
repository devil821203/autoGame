using Cultivation.Domain;

namespace Cultivation.Simulation;

internal static class PaidLifeServiceIntegrity
{
    internal static void Validate(WorldState w)
    {
        if (w.PaidServiceRules is not { } rules)
        {
            if (w.PaidServiceReceipts.Count != 0 || w.LifeSystem?.Work.Any(t => t.Service is not null) == true) throw new InvalidDataException("Paid services require rules");
            return;
        }
        if (w.LifeSystem is null || w.SpecialistRules is null || rules.RequestActionPoints <= 0 || !double.IsFinite(rules.MedicalUtility) || rules.MedicalUtility <= 0 ||
            w.Time is not null && rules.RequestActionPoints > w.Time.Rules.DailyActionPoints || rules.LaborFees is null || rules.MaterialPrices is null ||
            new[] { "medical", "severe-injury", "ointment", "bleeding-powder", "iron-sword", "leather-armor" }.Any(k => !rules.LaborFees.ContainsKey(k)) ||
            rules.LaborFees.Any(p => p.Value <= 0 || p.Key is not ("medical" or "severe-injury") && !w.LifeSystem.Recipes.Any(r => r.Id == p.Key)) ||
            rules.MaterialPrices.Any(p => p.Value <= 0 || !w.Items.Any(i => i.Id == p.Key)))
            throw new InvalidDataException("life/service-prices.json: invalid service fees/material prices/request AP");
        foreach (string service in rules.LaborFees.Keys)
        {
            try { _ = PaidLifeServices.Quote(w, service, ServiceMaterialSource.Provider); }
            catch (Exception e) when (e is KeyNotFoundException or OverflowException) { throw new InvalidDataException("life/service-prices.json: missing material price or quote overflow: " + service, e); }
        }
        bool Valid(PaidServiceContract c) => Enum.IsDefined(c.MaterialSource) && c.ClientId != c.ProviderId && c.StartedTick >= 0 && c.StartedTick <= w.Clock.Tick &&
            w.Characters.Any(a => a.Id == c.ClientId) && w.Characters.Any(a => a.Id == c.ProviderId && PaidLifeServices.Matches(a.TestProfession, c.ServiceId)) &&
            c.Fee == PaidLifeServices.Quote(w, c.ServiceId, c.MaterialSource);
        foreach (var task in w.LifeSystem.Work.Where(t => t.Service is not null))
        {
            var c = task.Service!;
            if (!Valid(c) || task.Intent.ActorId != c.ProviderId || Inventory.Balance(w, PaidLifeServices.Account(task.Intent.Id), "spirit-stone") != c.Fee ||
                w.PaidServiceReceipts.Any(r => r.ActionId == task.Intent.Id)) throw new InvalidDataException("Invalid active paid service reservation");
        }
        if (w.PaidServiceReceipts.Select(r => r.ActionId).Distinct().Count() != w.PaidServiceReceipts.Count) throw new InvalidDataException("Duplicate paid service settlement");
        foreach (var receipt in w.PaidServiceReceipts)
        {
            var c = receipt.Contract;
            if (!Valid(c) || receipt.Tick < c.StartedTick || receipt.Tick > w.Clock.Tick || !w.ProcessedIntentIds.Contains(receipt.ActionId) ||
                Inventory.Balance(w, PaidLifeServices.Account(receipt.ActionId), "spirit-stone") != 0 ||
                w.Ledger.Count(l => l.ActionId == receipt.ActionId && l.From == PaidLifeServices.Account(receipt.ActionId) && l.To == (receipt.Success ? c.ProviderId : c.ClientId) && l.Quantity == c.Fee && l.ResourceId == "spirit-stone") != 1 ||
                receipt.Output is not null && (!receipt.Success || w.Ledger.Count(l => l.ActionId == receipt.ActionId && l.From == c.ProviderId && l.To == c.ClientId && l.ResourceId == receipt.Output && l.Reason == "PaidServiceDelivery" && l.Quantity == 1) != 1))
                throw new InvalidDataException("Invalid paid service settlement backing");
        }
    }
}
