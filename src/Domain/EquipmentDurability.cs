namespace Cultivation.Domain;

public sealed record DurabilityRules(int Maximum,int LossPerBattle);
public sealed record EquipmentWear(string BattleId,string EquipmentId,string ActorId,long Tick,int Before,int After,string Trigger);
public sealed record LegacyWeaponInstance(string Id,string DefinitionId,string CreationId,string OwnerId,long Tick,string? BattleId);
public sealed class EquipmentDurabilityState
{
    public DurabilityRules Rules {get;set;}=null!;
    public Dictionary<string,int> Remaining {get;set;}=[];
    public List<EquipmentWear> Wear {get;set;}=[];
    public List<LegacyWeaponInstance> LegacyWeapons {get;set;}=[];
    public Dictionary<string,List<string>> BattleParticipants {get;set;}=[];
}
public static class EquipmentCondition
{
    public static int? Remaining(WorldState w,string id)=>w.EquipmentDurability is { } d?d.Remaining.GetValueOrDefault(id,d.Rules.Maximum):null;
    public static bool Intact(WorldState w,string id)=>Remaining(w,id) is not 0;
    public static string Description(WorldState w,string id)=>Remaining(w,id) is { } value?$"耐久{value}/{w.EquipmentDurability!.Rules.Maximum}"+(value==0?"（損壞，無加成）":""):"耐久未啟用";
}
