using Cultivation.Domain;

namespace Cultivation.Simulation;

public static class EquipmentRepairActions
{
    public static bool Busy(WorldState w,string actor)=>w.Time?.EquipmentRepair?.Work.Any(t=>t.Intent.ActorId==actor||t.Intent.TargetId==actor)==true;
    public static bool Locked(WorldState w,string equipment)=>w.Time?.EquipmentRepair?.Work.Any(t=>t.Intent.ResourceId==equipment)==true;
    private static bool Equipped(CharacterState c,string id)=>c.EquippedWeaponId==id||c.CraftedWeaponId==id||c.EquippedArmorId==id;
    private static string Slot(WorldState w,string id)=>w.EquipmentDurability!.LegacyWeapons.Any(g=>g.Id==id)?"weapon":w.LifeSystem!.Equipment.Single(g=>g.Id==id).Slot;
    private static bool Battle(WorldState w,string actor)=>BoardBattleActions.Participant(w,actor)||CombatActions.Active(w,actor) is not null;
    public static string? Validate(WorldState w,ActionIntent i)
    {
        if(w.Time?.EquipmentRepair is not {} repair)return "Equipment repair not enabled";
        if(i.Quantity!=1||i.DurationTicks!=0||i.MissionId is not null)return "Invalid repair request";
        var owner=w.Characters.Single(c=>c.Id==i.ActorId);var worker=w.Characters.SingleOrDefault(c=>c.Id==i.TargetId);
        if(worker is null||worker.Health<=0||worker.SeverelyInjured||Battle(w,owner.Id)||Battle(w,worker.Id)||worker.Hostile!=owner.Hostile)return "Repair participants unavailable";
        if(worker.Id!=owner.Id&&(worker.TestProfession!=TestProfession.Smith||worker.RegionId!=owner.RegionId||worker.DemoLocationId!=owner.DemoLocationId||worker.HexPosition!=owner.HexPosition))return "Repair requires a same-location smith";
        if(PaidLifeServices.Busy(w,owner.Id)||PaidLifeServices.Busy(w,worker.Id)||w.Time.ActionRuntime!.Actions.Values.Any(a=>!ActionLifecycle.Terminal(a.Phase)&&(a.WorkerId==worker.Id||a.ActorId==worker.Id))||worker.Stamina is {Resting:true} or {Unconscious:true}||w.Time.ApRestingActors.Contains(worker.Id))return "Repair participant busy";
        bool known=w.EquipmentDurability!.LegacyWeapons.Any(g=>g.Id==i.ResourceId)||w.LifeSystem!.Equipment.Any(g=>g.Id==i.ResourceId);
        if(!known||Inventory.Balance(w,owner.Id,i.ResourceId)!=1||Locked(w,i.ResourceId)||Equipped(owner,i.ResourceId))return "Own an unequipped, unlocked equipment instance first";
        if(EquipmentCondition.Remaining(w,i.ResourceId)>=w.EquipmentDurability.Rules.Maximum)return "Equipment already at maximum durability";
        var r=repair.Rules;string material=Slot(w,i.ResourceId)=="weapon"?r.WeaponMaterial:r.ArmorMaterial;
        if(Inventory.Balance(w,owner.Id,material)<r.MaterialQuantity||worker.Id!=owner.Id&&Inventory.Balance(w,owner.Id,"spirit-stone")<r.ServiceFee)return "Missing repair material or fee";
        return null;
    }
    internal static void Start(WorldState w,ActionIntent i)
    {
        var state=w.Time!.EquipmentRepair!;var r=state.Rules;string material=Slot(w,i.ResourceId)=="weapon"?r.WeaponMaterial:r.ArmorMaterial;
        string lease="repair-equipment:"+i.ResourceId;var runtime=w.Time.ActionRuntime!;
        if(!runtime.Capacities.ContainsKey(lease))ReservationService.DefineCapacity(w,lease,1);
        runtime.Capacities[lease].Leases.Add(i.Id,1);
        ReservationService.Reserve(w,i with{ResourceId=material,Quantity=r.MaterialQuantity},i.Id+":material",i.ActorId,ActionExecutor.Escrow(i.Id),"RepairMaterialReserved");
        if(i.TargetId!=i.ActorId)ReservationService.Reserve(w,i with{ResourceId="spirit-stone",Quantity=r.ServiceFee},i.Id+":fee",i.ActorId,ActionExecutor.Escrow(i.Id),"RepairFeeReserved");
        long due=checked(w.Time.DiscreteEvents!.WorldTime+r.DurationMinutes*60L);
        state.Work.Add(new(i,due,material,lease));
        ActionExecutor.Emit(w,i.Id,"RepairStarted",$"{i.ActorId}:{i.ResourceId}:{i.TargetId}:{due}");
    }
    internal static void Complete(WorldState w,string id,bool interrupt=false)
    {
        var s=w.Time?.EquipmentRepair;var work=s?.Work.SingleOrDefault(t=>t.Intent.Id==id);if(work is null)return;
        var i=work.Intent;var owner=w.Characters.Single(a=>a.Id==i.ActorId);var worker=w.Characters.Single(a=>a.Id==i.TargetId);
        bool success=!interrupt&&owner.Health>0&&worker.Health>0&&!Battle(w,owner.Id)&&!Battle(w,worker.Id)&&
            (i.ActorId==i.TargetId||owner.RegionId==worker.RegionId&&owner.DemoLocationId==worker.DemoLocationId&&owner.HexPosition==worker.HexPosition);
        ActionLifecycle.RequireRunning(w,id);int before=EquipmentCondition.Remaining(w,i.ResourceId)!.Value;
        int after=success?Math.Min(w.EquipmentDurability!.Rules.Maximum,checked(before+s!.Rules.RestoredDurability)):before;
        if(success){
            foreach(var reservation in w.Time!.ActionRuntime!.Reservations.Values.Where(r=>r.ActionId==id).ToArray())
                ReservationService.Settle(w,reservation.Id,reservation.Remaining,true,reservation.ResourceId=="spirit-stone"?i.TargetId:"consumed","RepairSettled");
            w.EquipmentDurability!.Remaining[i.ResourceId]=after;ActionLifecycle.Checkpoint(w,id);
        }
        s!.Receipts.Add(new(id,i.ActorId,i.TargetId,i.ResourceId,w.Time!.DiscreteEvents!.WorldTime,w.NextSequence++,before,after,success,work.Material));
        s.Work.Remove(work);ActionLifecycle.Finish(w,id,success?ActionPhase.Completed:ActionPhase.Interrupted,success?"Repair completed":"Repair interrupted; refund unused resources");
        foreach(string actor in new[]{i.ActorId,i.TargetId}.Distinct()){
            int n=w.Time.Budgets.FindIndex(b=>b.ActorId==actor);if(n>=0)w.Time.Budgets[n]=w.Time.Budgets[n] with{BusyThroughTick=w.Clock.Tick};
            LifeActions.RecomputeEquipment(w,actor);
        }
        ActionExecutor.Emit(w,id,success?"EquipmentRepaired":"EquipmentRepairInterrupted",$"{i.ActorId}:{i.ResourceId}:{before}->{after}");
    }
    internal static void InterruptActor(WorldState w,string actor)
    {foreach(var job in w.Time?.EquipmentRepair?.Work.Where(t=>t.Intent.ActorId==actor||t.Intent.TargetId==actor).ToArray()??[])Complete(w,job.Intent.Id,true);}
    internal static ActionIntent? Decide(WorldState w,CharacterState actor,DecisionSource source)
    {
        if(w.Time?.EquipmentRepair is null||Busy(w,actor.Id))return null;
        var ids=w.EquipmentDurability!.LegacyWeapons.Select(g=>g.Id).Concat(w.LifeSystem!.Equipment.Select(g=>g.Id));
        foreach(string id in ids.Where(id=>Inventory.Balance(w,actor.Id,id)==1&&EquipmentCondition.Remaining(w,id)<w.EquipmentDurability.Rules.Maximum).Order(StringComparer.Ordinal)){
            if(Equipped(actor,id))return new("repair-unequip:"+actor.Id+":"+w.Clock.Tick,actor.Id,actor.Id,actor.EquippedWeaponId==id?w.Advancement!.WeaponId:id,1,0,source,
                actor.EquippedWeaponId==id?ActionKind.UnequipWeapon:ActionKind.UnequipCraftedItem,"修理前先卸下受損裝備");
            foreach(var provider in w.Characters.Where(c=>c.Id==actor.Id||c.TestProfession==TestProfession.Smith).OrderBy(c=>c.Id==actor.Id?0:1).ThenBy(c=>c.Id,StringComparer.Ordinal)){
                var intent=new ActionIntent("repair:"+actor.Id+":"+w.Clock.Tick,actor.Id,provider.Id,id,1,0,source,ActionKind.RepairEquipment,"受損裝備可修復，先評估合法自修或同地委託");
                if(ActionValidator.Validate(w,intent) is null)return intent;
            }
        }
        return null;
    }
}
