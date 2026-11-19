using Cultivation.Domain;

namespace Cultivation.Simulation;

public static class SectEconomyScenario
{
    public static WorldState Create(string name = "行者", ulong seed = 12345, GameConfiguration? configuration = null)
    {
        var config = configuration ?? GameConfiguration.Find();
        var w = PlayerDemoScenario.Create(name, seed, config);
        w.SectPayroll = config.Read<SectPayrollRules>("organizations/payroll.json");
        w.SectDuty = config.Read<SectDutyRules>("organizations/duty.json");
        w.Dungeon = config.Read<DungeonRules>("combat/dungeon.json");
        if (w.Dungeon.Loot is null) throw new InvalidDataException("combat/dungeon.json: Loot is required for new worlds");
        w.OrganizationPolicies.Add(config.Read<OrganizationPolicy>("organizations/test-policy.json"));
        w.Organizations.Add(new("test-buyer", "測試收購節點（外部無限資金）"));
        w.TestProcurement = config.Read<ProcurementRules>("economy/procurement.json");
        w.SectFacilities = config.Read<SectFacilityRules>("organizations/facilities.json");
        w.PartyRules = config.Read<PartyRules>("combat/party.json");
        w.HuntRules = config.Read<HuntRules>("organizations/hunts.json");
        w.Organizations.Add(new("alchemy", "青雲宗煉丹房"));
        w.Organizations = w.Organizations.Select(o => o.Id == "grove" ? o with { Name = "青雲宗藥園" } : o).ToList();
        config.ApplyCharacters(w, "sect-economy");
        w.Rules = config.Patch("economy/sect-production.json", w.Rules);
        w.MarketListings = config.Read<List<MarketListing>>("economy/sect-market.json");
        config.AddStock(w, "sect");
        WorldIntegrity.Validate(w);
        return w;
    }
}
