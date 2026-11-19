using Cultivation.Domain;

namespace Cultivation.Simulation;

public static class SettlementScenario
{
    public static WorldState Create(string name = "行者", GameConfiguration? configuration = null)
    {
        var config = configuration ?? GameConfiguration.Find();
        var w = HexWorldScenario.Create(name, configuration: config);
        TimeScenario.Enable(w, config);
        w.PlayerAutoSchedule = false;
        var r = config.Read<SettlementRuntimeRules>("settlements/runtime.json");
        SettlementEconomyIntegrity.ValidateRules(r);
        var layout = config.Read<SettlementDemoLayout>("settlements/demo.json");
        var map = w.HexMap!;
        var site = map.Sites.SingleOrDefault(s => s.Id == layout.SiteId && s.Kind is SiteKind.Village or SiteKind.Town or SiteKind.City)
            ?? throw new InvalidDataException("config/settlements/demo.json: invalid SiteId");
        if (new[] { site.Anchor, layout.Forest, layout.Mountain }.Distinct().Count() != 3 ||
            new[] { layout.Forest, layout.Mountain }.Any(c => !map.Tiles.Any(t => t.Coord == c) || map.Sites.Any(s => s.Anchor == c)))
            throw new InvalidDataException("config/settlements/demo.json: invalid Forest/Mountain positions");
        string account = "village-economy", residents = "village-residents";
        w.Organizations.Add(new(account, "柳溪村公共資產", w.ControlledCharacterId));
        w.Organizations.Add(new(residents, "柳溪居民工資帳"));
        foreach (var item in config.Read<List<ItemDefinition>>("settlements/items.json"))
        {
            if (w.Items.Any(i => i.Id == item.Id)) throw new InvalidDataException("config/settlements/items.json: duplicate item " + item.Id);
            w.Items.Add(item);
        }
        foreach (var coord in new[] { site.Anchor, layout.Forest, layout.Mountain })
        {
            int n = map.Tiles.FindIndex(t => t.Coord == coord);
            map.Tiles[n] = map.Tiles[n] with { Terrain = HexTerrain.Plains, Forest = coord == layout.Forest,
                Mountain = coord == layout.Mountain, LocalOrganizationId = account };
        }
        w.SettlementRules = r;
        w.SettlementConstructionRules = config.Read<List<SettlementConstructionRule>>("settlements/construction.json");
        SettlementConstructionIntegrity.ValidateRules(w.SettlementConstructionRules);
        w.SettlementBuildingRules = SettlementIntegrity.ReadRules(config);
        var settlement = new SettlementState {
            Id = site.Id, SiteId = site.Id, OrganizationId = account, ResidentsAccountId = residents,
            Level = r.InitialLevel, Happiness = r.InitialHappiness, Population = r.InitialPopulation,
            WorkingPopulation = r.InitialPopulation, PopulationCapacity = r.HallCapacity,
            Territory = [site.Anchor, layout.Forest, layout.Mountain],
            Buildings = [new("hall", SettlementBuildingKind.TownHall, site.Anchor, 0, 0),
                new("farm", SettlementBuildingKind.Farm, site.Anchor, 0, r.EmployeeLimit),
                new("forestry", SettlementBuildingKind.Forestry, layout.Forest, 0, r.EmployeeLimit),
                new("shop", SettlementBuildingKind.Shop, site.Anchor, 0, r.EmployeeLimit)]
        };
        w.Settlements.Add(settlement);
        w.SettlementOperationRules=config.Read<SettlementOperationRules>("settlements/operations.json");
        w.SettlementTaxRules=config.Read<SettlementTaxRules>("settlements/taxes.json");
        w.SettlementDailyEconomy=config.Read<SettlementDailyEconomyRules>("settlements/daily-economy.json");
        settlement.GrowthProgress=w.SettlementDailyEconomy.PopulationGrowth?.InitialValue??0;
        settlement.Buildings=settlement.Buildings.Select(b=>b with{OwnerOrganizationId=account,Level=w.SettlementOperationRules.InitialBuildingLevel}).ToList();
        w.OrganizationSiteRules=config.Read<OrganizationSiteRules>("settlements/organization-sites.json");
        w.PopulationInvestmentRules=config.Read<PopulationInvestmentRules>("settlements/population-investment.json");
        OrganizationSiteIntegrity.ValidateRules(w.OrganizationSiteRules);
        if (r.InitialStones > 0) w.Ledger.Add(new(w.NextSequence++, 0, "settlement-genesis", "$genesis", account,
            "spirit-stone", r.InitialStones, "Genesis"));
        int player = w.Characters.FindIndex(c => c.Id == w.ControlledCharacterId);
        w.Characters[player] = w.Characters[player] with { HexPosition = site.Anchor, DemoLocationId = site.Anchor.ToString() };
        MedicineOnlyProfile.ApplyFresh(w,config);
        OrganizationLaborActions.EnableFresh(w,config);
        WorldIntegrity.Validate(w);
        return w;
    }
}
