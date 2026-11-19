using Cultivation.Domain;

namespace Cultivation.Simulation;

internal static class EquipmentRepairIntegrity
{
    internal static void Rules(RepairRules r)
    {
        if(r.DurationMinutes<=0||r.DurationMinutes%10!=0||r.ActionPoints<=0||r.ActionPoints>30||r.RestoredDurability<=0||r.SuccessChance!=1||r.ServiceFee<=0||
            r.MaterialQuantity<=0||r.WeaponMaterial!="iron-material"||r.ArmorMaterial!="beast-hide"||r.DemoActors.Length==0||r.DemoActors.Distinct().Count()!=r.DemoActors.Length||r.DemoMaterialQuantity<=0||r.DemoWearBattles<0)
            throw new InvalidDataException("config/items/repair.json: invalid duration/AP/restoration/material/fee/demo rules; first version requires SuccessChance=1");
    }
    internal static void Validate(WorldState w)
    {
        if(w.Time?.EquipmentRepair is not {} state)return;
        Rules(state.Rules);var runtime=w.Time.ActionRuntime??throw new InvalidDataException("Repair requires lifecycle");
        long now=w.Time.DiscreteEvents!.WorldTime;
        if(state.Work.Select(t=>t.Intent.ResourceId).Distinct().Count()!=state.Work.Count||state.Receipts.Select(t=>t.ActionId).Distinct().Count()!=state.Receipts.Count)
            throw new InvalidDataException("Duplicate repair work or settlement");
        foreach(var t in state.Work){
            var i=t.Intent;
            if(!runtime.Actions.TryGetValue(i.Id,out var a)||a.Phase!=ActionPhase.Running||a.Kind!=ActionKind.RepairEquipment||a.ActorId!=i.ActorId||a.WorkerId!=i.TargetId||a.ResourceId!=i.ResourceId||
                t.DueAt!=a.DueAt||t.DueAt!=a.StartedAt+state.Rules.DurationMinutes*60L||t.DueAt<now||Inventory.Balance(w,i.ActorId,i.ResourceId)!=1||
                !runtime.Capacities.TryGetValue(t.EquipmentLease,out var pool)||pool.Leases.GetValueOrDefault(i.Id)!=1||
                w.Time.DiscreteEvents.Pending.Count(e=>e.Kind==SimulationEventKind.EquipmentRepair&&e.CorrelationId==i.Id&&e.DueAt==t.DueAt&&e.Version==a.Version)!=1)
                throw new InvalidDataException("Repair work/reservation/event mismatch");
            CheckReservations(w,i.Id,i.ActorId,i.TargetId,t.Material,false,false);
        }
        foreach(var receipt in state.Receipts){
            if(!runtime.Actions.TryGetValue(receipt.ActionId,out var a)||a.Kind!=ActionKind.RepairEquipment||a.ActorId!=receipt.OwnerId||a.WorkerId!=receipt.WorkerId||a.ResourceId!=receipt.EquipmentId||
                (a.Phase==ActionPhase.Completed)!=receipt.Completed||!ActionLifecycle.Terminal(a.Phase)||receipt.AtSeconds<a.StartedAt||receipt.AtSeconds>now||receipt.Sequence<=0||receipt.Sequence>=w.NextSequence||
                receipt.Completed&&receipt.AtSeconds!=a.DueAt||receipt.Before<0||receipt.Before>=w.EquipmentDurability!.Rules.Maximum||
                receipt.After!=(receipt.Completed?Math.Min(w.EquipmentDurability!.Rules.Maximum,receipt.Before+state.Rules.RestoredDurability):receipt.Before)||
                !w.EquipmentDurability!.Remaining.ContainsKey(receipt.EquipmentId))throw new InvalidDataException("Unbacked repair receipt");
            CheckReservations(w,receipt.ActionId,receipt.OwnerId,receipt.WorkerId,receipt.Material,true,receipt.Completed);
        }
        if(runtime.Actions.Values.Any(a=>a.Kind==ActionKind.RepairEquipment&&!state.Work.Any(t=>t.Intent.Id==a.Id)&&!state.Receipts.Any(r=>r.ActionId==a.Id)))throw new InvalidDataException("Repair action missing work/receipt");
    }
    private static void CheckReservations(WorldState w,string id,string owner,string worker,string material,bool terminal,bool success)
    {
        var r=w.Time!.EquipmentRepair!.Rules;var reservations=w.Time.ActionRuntime!.Reservations.Values.Where(v=>v.ActionId==id).ToArray();
        var expected=new Dictionary<string,long>{{material,r.MaterialQuantity}};if(owner!=worker)expected.Add("spirit-stone",r.ServiceFee);
        if(reservations.Length!=expected.Count||reservations.Any(v=>v.OwnerId!=owner||!expected.TryGetValue(v.ResourceId,out long amount)||v.Amount!=amount||
            (terminal?(success?v.Consumed!=amount||v.Released!=0:v.Released!=amount||v.Consumed!=0):v.Remaining!=amount)))throw new InvalidDataException("Repair escrow settlement mismatch");
    }
}
