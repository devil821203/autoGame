using System.Diagnostics;
using System.Text.Json;
using Cultivation.Domain;
using Cultivation.Simulation;

namespace Cultivation.Headless;

public static class SettlementGrowthReport
{
    public static void Run(int days, string output, bool residentOrganization=false)
    {
        if (days <= 0) throw new ArgumentOutOfRangeException(nameof(days));
        string path = Path.GetFullPath(output);
        if (File.Exists(path)) throw new IOException("Report exists; choose a new output path");
        var engine = new SimulationEngine(residentOrganization?SettlementOrganizationScenario.Create():SettlementScenario.Create());
        var initial = engine.Snapshot(); var time = initial.Time!.Rules;
        var samples = new List<object>(); var timer = Stopwatch.StartNew();
        object Sample(WorldState w) => new {
            Tick = w.Clock.Tick,
            Settlements = w.Settlements.Select(s => new {
                s.Id, s.Level, s.Population, s.PopulationCapacity, s.Happiness, s.GrowthProgress,
                Money = Inventory.Balance(w, s.OrganizationId, "spirit-stone"),
                Food = Inventory.Balance(w, s.OrganizationId, "food"),
                Wood = Inventory.Balance(w, s.OrganizationId, "wood"),
                ReservedMoney = SettlementDailyEconomy.Reserved(w, s, "spirit-stone"),
                ReservedWood = SettlementDailyEconomy.Reserved(w, s, "wood"),
                ProductionSettlements = w.SettlementDays.Count(r => r.SettlementId == s.Id),
                ConsumptionIncome = w.SettlementConsumption.Where(r => r.SettlementId == s.Id).Sum(r => r.Income),
                Buildings = s.Buildings.Select(b => new { b.Id, Kind = b.Kind.ToString(), b.Level, b.EmployeeCount, b.EmployeeLimit }).ToArray()
            }).ToArray(),
            Organizations=w.Organizations.Where(o=>w.Characters.Any(c=>c.OrganizationId==o.Id)||w.OrganizationSites.Any(s=>s.OrganizationId==o.Id)).Select(o=>new{
                o.Id,o.Name,o.LeaderId,Kind=o.Kind.ToString(),
                Money=Inventory.Balance(w,o.Id,"spirit-stone"),Wood=Inventory.Balance(w,o.Id,"wood"),Food=Inventory.Balance(w,o.Id,"food"),
                Members=w.Characters.Where(c=>c.OrganizationId==o.Id).Select(c=>new{c.Id,c.Name,c.Health,c.DemoLocationId,Position=c.Position.ToString(),PersonalMoney=Inventory.Balance(w,c.Id,"spirit-stone")}).ToArray(),
                Sites=w.OrganizationSites.Where(s=>s.OrganizationId==o.Id).Select(s=>new{s.Id,s.SettlementId,Kind=s.Kind.ToString(),s.Level,
                    Capacity=OrganizationSiteCapacity.Limit(w.OrganizationSiteRules!,s),Used=OrganizationSiteCapacity.Used(w,s)}).ToArray(),
                Buildings=w.Settlements.SelectMany(s=>s.Buildings.Where(b=>b.OwnerOrganizationId==o.Id).Select(b=>new{SettlementId=s.Id,b.Id,Kind=b.Kind.ToString(),b.Level,b.EmployeeCount})).ToArray()
            }).ToArray()
        };
        samples.Add(Sample(initial));
        for (int day = 1; day <= days; day++)
        {
            long target = checked((long)day * time.TicksPerDay);
            while (engine.Snapshot().Clock.Tick < target)
                if (engine.StepValidatedBatch(target, time.TicksPerDay, validateEveryTick: false) == 0)
                    throw new InvalidOperationException("Simulation blocked before requested day");
            var w = engine.Snapshot(); samples.Add(Sample(w));
            if (day % time.DaysPerSeason == 0 || day == days)
                Console.WriteLine($"SETTLEMENT_GROWTH day={day}/{days} tick={w.Clock.Tick} population={w.Settlements.Single().Population} level={w.Settlements.Single().Level} elapsedSeconds={timer.Elapsed.TotalSeconds:F1}");
        }
        var final = engine.Snapshot();
        var settlement = final.Settlements.Single();
        var upgrade = new ActionIntent("growth-probe-upgrade", final.ControlledCharacterId, settlement.Id, "upgrade", 1, 0, DecisionSource.Player, ActionKind.UpgradeSettlement);
        var report = new {
            Scenario = residentOrganization?"Fresh village organization demo, configured finite genesis stock and paid initial HQ, then autonomous NPCs without external commands or funding":"Fresh SettlementScenario, autonomous NPCs, no player commands or injected resources",
            OrganizationDemo=initial.SettlementOrganizationDemo,
            ExternalMarketRules=initial.ExternalMarket?.Rules,
            ExternalMarketItemTrades=final.ExternalMarket?.Receipts.GroupBy(r=>(r.OwnerId,r.ItemId,r.Kind)).Select(g=>new{
                g.Key.OwnerId,g.Key.ItemId,Kind=g.Key.Kind.ToString(),Transactions=g.Count(),Quantity=g.Sum(r=>r.Quantity),
                TotalPrice=g.Sum(r=>r.TotalPrice),AverageRealizedUnitPrice=g.Sum(r=>(decimal)r.TotalPrice)/g.Sum(r=>(decimal)r.Quantity),
                QuotedUnitPrice=final.ExternalMarket.Rules.BasePrices[ExternalMarketPricing.Definition(final,g.Key.ItemId)]*
                    (g.Key.Kind==ActionKind.ExternalMarketBuy?final.ExternalMarket.Rules.SaleMultiplier:final.ExternalMarket.Rules.PurchaseMultiplier)
            }).ToArray(),
            OrganizationResourceFlows=final.OrganizationSites.Select(s=>s.OrganizationId).Distinct().Select(owner=>new{
                OwnerId=owner,
                Resources=final.Ledger.Where(e=>(e.From==owner||e.To==owner)&&e.ResourceId!="spirit-stone").Select(e=>e.ResourceId)
                    .Distinct().OrderBy(item=>item,StringComparer.Ordinal).Select(item=>new{
                        ItemId=item,Name=final.Items.Single(i=>i.Id==item).Name,
                        OpeningStock=Inventory.Balance(initial,owner,item),ClosingStock=Inventory.Balance(final,owner,item),
                        GrossProduced=final.SettlementDays.Where(d=>d.OwnerId==owner&&d.Output==item).Sum(d=>d.GrossQuantity),
                        ProductionTax=final.SettlementDays.Where(d=>d.OwnerId==owner&&d.Output==item).Sum(d=>d.TaxQuantity),
                        NetProduced=final.SettlementDays.Where(d=>d.OwnerId==owner&&d.Output==item).Sum(d=>d.Quantity),
                        Purchased=final.ExternalMarket?.Receipts.Where(t=>t.OwnerId==owner&&t.ItemId==item&&t.Kind==ActionKind.ExternalMarketBuy).Sum(t=>t.Quantity)??0,
                        Sold=final.ExternalMarket?.Receipts.Where(t=>t.OwnerId==owner&&t.ItemId==item&&t.Kind==ActionKind.ExternalMarketSell).Sum(t=>t.Quantity)??0,
                        ConstructionAndUpgradeUsed=final.Ledger.Where(e=>e.Tick>initial.Clock.Tick&&e.From==owner&&e.ResourceId==item&&
                            e.Reason is "SettlementConstruction" or "SettlementBuildingUpgrade" or "OrganizationSitePayment").Sum(e=>e.Quantity),
                        ReceivedSinceStart=final.Ledger.Where(e=>e.Tick>initial.Clock.Tick&&e.To==owner&&e.ResourceId==item).Sum(e=>e.Quantity),
                        SpentSinceStart=final.Ledger.Where(e=>e.Tick>initial.Clock.Tick&&e.From==owner&&e.ResourceId==item).Sum(e=>e.Quantity)
                    }).ToArray()
            }).ToArray(),
            ExternalMarketTrades=final.ExternalMarket?.Receipts.GroupBy(r=>r.OwnerId).Select(g=>new{
                OwnerId=g.Key, Purchases=g.Count(r=>r.Kind==ActionKind.ExternalMarketBuy),Sales=g.Count(r=>r.Kind==ActionKind.ExternalMarketSell),
                MoneySpent=g.Where(r=>r.Kind==ActionKind.ExternalMarketBuy).Sum(r=>r.TotalPrice),MoneyReceived=g.Where(r=>r.Kind==ActionKind.ExternalMarketSell).Sum(r=>r.TotalPrice)
            }).ToArray(),
            RequestedDays = days, TicksPerDay = time.TicksPerDay, TotalTicks = final.Clock.Tick,
            Validation = "Full world integrity at every daily batch boundary; every simulation Tick and NPC decision executes normally",
            ElapsedSeconds = timer.Elapsed.TotalSeconds, NextUpgradeRejection = ActionValidator.Validate(final, upgrade),
            BuildingUpgradeCount = final.SettlementBuildingUpgrades.Count,
            OrganizationSiteUpgrades=final.OrganizationSiteReceipts.Count(r=>!r.InitialPlacement&&r.ResultLevel>final.OrganizationSiteRules!.InitialLevel),
            FinalDigest = WorldCodec.Digest(final), Samples = samples
        };
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        JsonSerializer.Serialize(stream, report, new JsonSerializerOptions { WriteIndented = true });
        Console.WriteLine("SETTLEMENT_GROWTH_REPORT " + path);
    }
}
