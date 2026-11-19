namespace Cultivation.Domain;

public sealed record RepairRules(int DurationMinutes,int ActionPoints,int RestoredDurability,double SuccessChance,long ServiceFee,
    string WeaponMaterial,string ArmorMaterial,long MaterialQuantity,string[] DemoActors,long DemoMaterialQuantity,int DemoWearBattles);
public sealed record RepairWork(ActionIntent Intent,long DueAt,string Material,string EquipmentLease);
public sealed record RepairReceipt(string ActionId,string OwnerId,string WorkerId,string EquipmentId,long AtSeconds,long Sequence,
    int Before,int After,bool Completed,string Material);
public sealed class EquipmentRepairState
{
    public RepairRules Rules {get;set;}=null!;
    public List<RepairWork> Work {get;set;}=[];
    public List<RepairReceipt> Receipts {get;set;}=[];
}
