using Cultivation.Domain;

namespace Cultivation.Simulation;

public sealed record MedicineOnlyPrices(long HerbPrice,long MedicinePrice,long MedicineMinimumPrice,long MedicineMaximumPrice);
public static class MedicineOnlyProfile
{
    public static WorldState ApplyFresh(WorldState w,GameConfiguration? configuration=null)
    {
        if(w.MedicineOnly)return w;
        if(w.Clock.Tick!=0||w.Pending.Count>0||w.Scheduled.Count>0||w.Ledger.Any(e=>e.Reason!="Genesis"))throw new InvalidOperationException("Medicine-only profile applies to a fresh world, never a loaded save");
        var c=configuration??GameConfiguration.Find();var p=c.Read<MedicineOnlyPrices>("life/medicine-only.json");
        if(p.HerbPrice<=0||p.MedicineMinimumPrice<=0||p.MedicinePrice<p.MedicineMinimumPrice||p.MedicineMaximumPrice<p.MedicinePrice)throw new InvalidDataException("Invalid medicine-only prices");
        w.MedicineOnly=true;w.AlchemyEnabled=false;
        w.LifeSkillRules??=c.Read<LifeSkillRules>("life/skills.json");
        w.LifeSystem??=new(){Options=c.Read<LifeOptions>("life/options.json"),Recipes=c.Read<List<LifeRecipe>>("life/recipes.json")};
        w.Characters=w.Characters.Select(a=>a with {LifeSkills=a.LifeSkills??new()}).ToList();
        w.Items.RemoveAll(i=>i.Id=="pill");
        foreach(var i in new[]{new ItemDefinition("ointment","藥膏","藥物"),new("bleeding-powder","止血散","藥物"),new("iron-material","測試鐵料","原料"),new("leather-armor","皮甲","防具")})
            if(!w.Items.Any(x=>x.Id==i.Id))w.Items.Add(i);
        w.Ledger=w.Ledger.Select(e=>e.ResourceId=="pill"?e with {ResourceId="ointment"}:e).ToList();
        w.MarketListings=w.MarketListings.Select(l=>l.ResourceId=="pill"?l with {ResourceId="ointment",BasePrice=p.MedicinePrice,MinimumPrice=p.MedicineMinimumPrice,MaximumPrice=p.MedicineMaximumPrice,StockPriceStep=1,Spread=2}:l).ToList();
        w.Rules=w.Rules with {PillPrice=p.MedicinePrice};
        if(w.TestProcurement is {} r)w.TestProcurement=c.Patch("life/procurement.json",r);
        if(w.SectDuty is {} d)w.SectDuty=d with {PillReward=p.HerbPrice};
        if(w.SectFacilities is {} f)w.SectFacilities=f with {PillExchangePrice=p.MedicinePrice};
        if(w.MissionDefaults is {} m)w.MissionDefaults=m with {UnitPrice=p.HerbPrice,MaximumBudget=checked(m.Quantity*p.HerbPrice)};
        w.Organizations=w.Organizations.Select(o=>o.Id=="alchemy"?o with {Name="青雲宗製藥坊"}:o).ToList();
        SpecialistProfile.ApplyFresh(w, c);
        w.PaidServiceRules = c.Read<PaidServiceRules>("life/service-prices.json");
        WorldIntegrity.Validate(w);return w;
    }
}
