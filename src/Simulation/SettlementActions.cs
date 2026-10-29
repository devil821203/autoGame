using Cultivation.Domain;

namespace Cultivation.Simulation;

internal static class SettlementActions
{
    internal static bool Handles(ActionKind k) => k is ActionKind.SetSettlementEmployees or
        ActionKind.SettlementBuy or ActionKind.SettlementSell or ActionKind.SupportSettlement or ActionKind.UpgradeSettlement or ActionKind.BuildSettlement or ActionKind.CreateOrganizationSite or ActionKind.UpgradeOrganizationSite or ActionKind.UpgradeSettlementBuilding;

    internal static string? Validate(WorldState w, ActionIntent i)
    {
        if (w.SettlementRules is not { } r || w.Time is null || i.DurationTicks != 0)
            return "Settlement economy unavailable or invalid duration";
        var s = w.Settlements.SingleOrDefault(s => s.Id == i.TargetId);
        if (s is null) return "Unknown settlement";
        var actor = w.Characters.Single(c => c.Id == i.ActorId);
        var anchor = w.HexMap!.Sites.Single(t => t.Id == s.SiteId).Anchor;
        if (actor.RegionId != "valley" || HexNavigation.Position(w.HexMap, actor) != anchor)
            return "Must arrive at settlement";
        bool manager = w.Organizations.Any(o => o.Id == s.OrganizationId && o.LeaderId == actor.Id);
        if ((i.Kind==ActionKind.UpgradeSettlement||i.Kind==ActionKind.SetSettlementEmployees&&w.SettlementOperationRules is null) && !manager)
            return "Settlement management permission required";
        if (i.Kind == ActionKind.BuildSettlement) return SettlementConstruction.Validate(w, s, i);
        if (OrganizationSiteActions.Handles(i.Kind)) return OrganizationSiteActions.Validate(w,s,i);
        if(i.Kind==ActionKind.UpgradeSettlementBuilding)return SettlementOperations.ValidateUpgrade(w,s,i);
        if (i.Kind != ActionKind.SupportSettlement && i.OrganizationId is not null) return "Unexpected donor";
        if (i.Kind == ActionKind.SetSettlementEmployees)
        {
            var b = s.Buildings.SingleOrDefault(b => b.Id == i.ResourceId);
            if(b is not null&&SettlementOperations.Automatic(w,s,b))return "Public building operates automatically without employees";
            if(b is not null&&w.SettlementOperationRules is not null&&!w.Organizations.Any(o=>o.Id==SettlementOperations.Owner(w,s,b)&&o.LeaderId==actor.Id))return "Building owner permission required";
            if (b is null || !w.SettlementBuildingRules!.Single(d => d.Kind == b.Kind).RequiresEmployees ||
                b.Kind is SettlementBuildingKind.Smithy or SettlementBuildingKind.Pharmacy)
                return "Building operation not enabled";
            if (i.Quantity < 0 || i.Quantity+b.LaborCount > b.EmployeeLimit ||
                s.Buildings.Sum(b => (long)b.EmployeeCount) - b.EmployeeCount + i.Quantity > s.WorkingPopulation)
                return "Employee limit or available residents exceeded";
            return null;
        }
        if (i.Quantity <= 0) return "Quantity must be positive";
        if (i.Kind == ActionKind.UpgradeSettlement)
        {
            var level = r.Levels.SingleOrDefault(l => l.Level == s.Level + 1);
            return i.Quantity != 1 || i.ResourceId != "upgrade" || level is null ? "No upgrade available" :
                s.Population < level.RequiredPopulation ? "Population requirement unmet" :
                Inventory.Balance(w, s.OrganizationId, "spirit-stone") < level.Stones ||
                Inventory.Balance(w, s.OrganizationId, "wood") < level.Wood ? "Insufficient settlement upgrade assets" : null;
        }
        if (i.Kind == ActionKind.SupportSettlement)
        {
            if (i.OrganizationId is null || i.OrganizationId == s.OrganizationId ||
                !w.Organizations.Any(o => o.Id == i.OrganizationId && o.LeaderId == actor.Id) ||
                i.ResourceId is not ("wood" or "mineral" or "spirit-stone")) return "Invalid organization support";
            if (i.ResourceId != "spirit-stone" && r.MaterialInfluence > 0 &&
                (i.Quantity > long.MaxValue / r.MaterialInfluence ||
                 s.Influence.GetValueOrDefault(i.OrganizationId!) > long.MaxValue - i.Quantity*r.MaterialInfluence))
                return "Influence overflow";
            return Inventory.Balance(w, i.OrganizationId, i.ResourceId) < i.Quantity ? "Insufficient donor assets" : null;
        }
        var price = r.Prices.SingleOrDefault(p => p.Item == i.ResourceId);
        var shop=SettlementOperations.Shop(w,s,i);
        if (price is null || shop is null || !SettlementOperations.Automatic(w,s,shop)&&shop.EmployeeCount<=0)
            return "Shop needs employees and a supported item";
        string shopOwner=SettlementOperations.Owner(w,s,shop);
        long wages = SettlementOperations.Automatic(w,s,shop)?0:checked(shop.EmployeeCount*r.DailyWage);
        if (Inventory.Balance(w, shopOwner, "spirit-stone") < wages) return "Shop unable to pay wages";
        long unit = i.Kind == ActionKind.SettlementBuy ? price.Sell : price.Buy;
        if (i.Quantity > long.MaxValue / unit) return "Trade quantity overflow";
        bool buy = i.Kind == ActionKind.SettlementBuy;
        var seller = buy ? shopOwner : actor.Id;
        var buyer = buy ? actor.Id : shopOwner;
        return SettlementDailyEconomy.Available(w,s,seller,i.ResourceId) < i.Quantity ? "Insufficient shop or seller stock" :
            SettlementDailyEconomy.Available(w,s,buyer,"spirit-stone") < i.Quantity * unit ? "Insufficient buyer funds" : null;
    }

    private static void Move(WorldState w, string id, string from, string to, string item, long quantity, string reason)
    {
        if (quantity > 0) w.Ledger.Add(new(w.NextSequence++, w.Clock.Tick, id, from, to, item, quantity, reason));
    }

    internal static void Execute(WorldState w, ActionIntent i)
    {
        var s = w.Settlements.Single(s => s.Id == i.TargetId); var r = w.SettlementRules!;
        if (i.Kind == ActionKind.BuildSettlement) SettlementConstruction.Execute(w,s,i);
        else if (OrganizationSiteActions.Handles(i.Kind)) OrganizationSiteActions.Execute(w,s,i);
        else if(i.Kind==ActionKind.UpgradeSettlementBuilding)SettlementOperations.Upgrade(w,s,i);
        else if (i.Kind == ActionKind.SetSettlementEmployees)
        {
            int n = s.Buildings.FindIndex(b => b.Id == i.ResourceId);
            s.Buildings[n] = s.Buildings[n] with { EmployeeCount = checked((int)i.Quantity), PaidThroughTick = w.Clock.Tick };
        }
        else if (i.Kind == ActionKind.UpgradeSettlement)
        {
            var level = r.Levels.Single(l => l.Level == s.Level + 1);
            Move(w, i.Id, s.OrganizationId, "consumed", "spirit-stone", level.Stones, "SettlementUpgrade");
            Move(w, i.Id, s.OrganizationId, "consumed", "wood", level.Wood, "SettlementUpgrade");
            s.Level = level.Level; s.PopulationCapacity = checked(s.PopulationCapacity + r.HallCapacityPerLevel);
        }
        else if (i.Kind == ActionKind.SupportSettlement)
        {
            Move(w, i.Id, i.OrganizationId!, s.OrganizationId, i.ResourceId, i.Quantity, "SettlementSupport");
            if (i.ResourceId != "spirit-stone")
            {
                long points = checked(i.Quantity * r.MaterialInfluence);
                s.Influence[i.OrganizationId!] = checked(s.Influence.GetValueOrDefault(i.OrganizationId!) + points);
                w.SettlementInfluence.Add(new(i.Id, s.Id, i.OrganizationId!, i.ResourceId, i.Quantity, points));
            }
        }
        else
        {
            var price = r.Prices.Single(p => p.Item == i.ResourceId);
            bool buy = i.Kind == ActionKind.SettlementBuy;
            var shop=SettlementOperations.Shop(w,s,i)!;string shopOwner=SettlementOperations.Owner(w,s,shop);
            string seller = buy ? shopOwner : i.ActorId, buyer = buy ? i.ActorId : shopOwner;
            Move(w, i.Id, seller, buyer, i.ResourceId, i.Quantity, "SettlementShopGoods");
            Move(w, i.Id, buyer, seller, "spirit-stone", checked(i.Quantity * (buy ? price.Sell : price.Buy)), "SettlementShopPayment");
        }
        ActionExecutor.Emit(w, i.Id, "SettlementActionCompleted", $"{s.Id}:{i.Kind}:{i.ResourceId}:{i.Quantity}");
    }

    internal static void Advance(WorldState w)
    {
        if(w.SettlementOperationRules is not null){SettlementOperations.Advance(w);return;}
        if (w.SettlementRules is not { } r || w.Time is null || w.Clock.Tick == 0) return;
        foreach (var s in w.Settlements.OrderBy(s => s.Id, StringComparer.Ordinal))
        {
            if (s.LastPayrollTick >= w.Clock.Tick) continue;
            foreach (var b in s.Buildings.OrderBy(b => b.Id, StringComparer.Ordinal))
            {
                if (b.EmployeeCount == 0 || b.Kind is SettlementBuildingKind.Smithy or SettlementBuildingKind.Pharmacy) continue;
                if (w.Clock.Tick - b.PaidThroughTick < w.Time.Rules.TicksPerDay) continue;
                int index = s.Buildings.FindIndex(item => item.Id == b.Id);
                s.Buildings[index] = b with { PaidThroughTick = w.Clock.Tick };
                string id = $"settlement-day:{s.Id}:{b.Id}:{w.Clock.Tick}";
                long wages = checked(b.EmployeeCount * r.DailyWage);
                if (Inventory.Balance(w, s.OrganizationId, "spirit-stone") < wages)
                { ActionExecutor.Emit(w, id, "SettlementStopped", "Insufficient wages: " + b.Id); continue; }
                Move(w, id, s.OrganizationId, s.ResidentsAccountId, "spirit-stone", wages, "SettlementWages");
                string? output = r.Outputs.GetValueOrDefault(b.Kind);
                long quantity = output is null ? 0 : checked(b.EmployeeCount * r.DailyOutput(b.Kind));
                Move(w, id, "$settlement-production", s.OrganizationId, output ?? "", quantity, "SettlementProduced");
                w.SettlementDays.Add(new(id, s.Id, b.Id, w.Clock.Tick, b.EmployeeCount, wages, output, quantity));
                ActionExecutor.Emit(w, id, "SettlementDayCompleted", $"{b.Id}:wages={wages}:{output}={quantity}");
            }
            s.LastPayrollTick = w.Clock.Tick;
        }
    }
}
