using Cultivation.Domain;

namespace Cultivation.AI;

public sealed record LifeObservation(CharacterState Actor,long Tick,DecisionSource Source,
    IReadOnlyDictionary<string,long> Stock,IReadOnlyList<CharacterState> PresentPatients,
    IReadOnlyList<LifeRecipe> Recipes,IReadOnlyList<CraftedEquipment> OwnedEquipment);
public static class LifeUtility
{
    public static IEnumerable<ScoredAction> Evaluate(LifeObservation o)
    {
        var a=o.Actor;
        ScoredAction Offer(ActionKind kind,string target,string item,double score,string why)=>new(
            new($"life-ai:{a.Id}:{o.Tick}:{kind}:{item}",a.Id,target,item,1,0,o.Source,kind),score,why);
        if(a.SeverelyInjured)yield return Offer(ActionKind.TreatSevereInjury,a.Id,"severe-injury",100,"自身重傷，優先評估醫術療程與材料");
        foreach(var p in o.PresentPatients.Where(p=>p.Id==a.Id||!p.Hostile&&p.OrganizationId==a.OrganizationId))
        {
            if(p.SeverelyInjured)yield return Offer(ActionKind.TreatSevereInjury,p.Id,"severe-injury",20*a.SocialWeight,"依同地可見重傷需求協助治療");
            else if(p.Health is >0 and <80)yield return Offer(ActionKind.MedicalTreatment,p.Id,"herb",(100-p.Health)*a.SocialWeight/5,"依同地可見缺血與社交偏好提供醫療");
        }
        if(a.Health is >0 and <70&&o.Stock.GetValueOrDefault("ointment")>0)yield return Offer(ActionKind.UseMedicine,a.Id,"ointment",(100-a.Health)/3.0,"自用現有藥膏恢復生命");
        foreach(var g in o.OwnedEquipment)
            if(g.Slot=="weapon"&&a.EquippedWeaponId is null&&a.CraftedWeaponId is null||g.Slot=="armor"&&a.EquippedArmorId is null)
                yield return Offer(ActionKind.EquipCraftedItem,a.Id,g.Id,8*a.CombatDrive,"穿用已持有裝備提升安全，不消耗新材料");
        foreach(var r in o.Recipes)
        {
            bool need=r.Slot=="weapon"?a.EquippedWeaponId is null&&a.CraftedWeaponId is null&&!o.OwnedEquipment.Any(g=>g.Slot=="weapon"):
                r.Slot=="armor"?a.EquippedArmorId is null&&!o.OwnedEquipment.Any(g=>g.Slot=="armor"):o.Stock.GetValueOrDefault(r.Output)<(r.Output=="ointment"?2:1);
            if(!need||r.Inputs.Any(p=>o.Stock.GetValueOrDefault(p.Key)<p.Value))continue;
            double benefit=r.Slot is not null?(4*a.CombatDrive+a.GrowthWeight):(2*a.Caution+a.GrowthWeight);
            yield return Offer(ActionKind.CraftLifeItem,a.Id,r.Id,benefit/r.Ticks,"依自身備藥／裝備缺口、材料、風險及工期評估生活製作");
        }
    }
}
