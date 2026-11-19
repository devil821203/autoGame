using Cultivation.Domain;

namespace Cultivation.Simulation;

// Reservations reference the existing ledger escrow. They never maintain a second wallet.
public static class ReservationService
{
    public static string Reserve(WorldState w, ActionIntent intent, string id, string owner, string account, string reason)
    {
        var runtime=w.Time?.ActionRuntime ?? throw new InvalidOperationException("R2 not enabled");
        if (!runtime.Actions.TryGetValue(intent.Id,out var action) || ActionLifecycle.Terminal(action.Phase) || intent.Quantity<=0 ||
            string.IsNullOrWhiteSpace(id) || (account!=ActionExecutor.Escrow(intent.Id)&&account!=PaidLifeServices.Account(intent.Id)) ||
            !w.Characters.Any(a=>a.Id==owner) && !w.Organizations.Any(o=>o.Id==owner)) throw new InvalidOperationException("Invalid reservation owner/action/account");
        if(runtime.Reservations.TryGetValue(id,out var existing))
        {
            if(existing.ActionId!=intent.Id||existing.OwnerId!=owner||existing.Account!=account||existing.ResourceId!=intent.ResourceId||existing.Amount!=intent.Quantity)
                throw new InvalidOperationException("Reservation id reused with different terms");
            return id;
        }
        if(Inventory.Balance(w,owner,intent.ResourceId)<intent.Quantity)throw new InvalidOperationException("Insufficient unreserved ledger assets");
        var record=new ResourceReservation(id,intent.Id,owner,intent.ResourceId,account,intent.Quantity);
        ActionExecutor.Transfer(w,intent,owner,account,reason);
        runtime.Reservations.Add(id,record);return id;
    }
    public static void Settle(WorldState w,string id,long amount,bool consume,string destination,string reason)
    {
        var runtime=w.Time?.ActionRuntime ?? throw new InvalidOperationException("R2 not enabled");
        if(!runtime.Reservations.TryGetValue(id,out var r)||amount<=0||amount>r.Remaining||Inventory.Balance(w,r.Account,r.ResourceId)<amount)
            throw new InvalidOperationException("Reservation already settled or changed");
        if(consume && destination!="consumed" && !w.Characters.Any(a=>a.Id==destination) && !w.Organizations.Any(o=>o.Id==destination))
            throw new InvalidOperationException("Unknown reservation beneficiary");
        var intent=new ActionIntent(r.ActionId,r.OwnerId,destination,r.ResourceId,amount,0,DecisionSource.Npc);
        ActionExecutor.Transfer(w,intent,r.Account,consume?destination:r.OwnerId,reason);
        if(consume)r.Consumed=checked(r.Consumed+amount);else r.Released=checked(r.Released+amount);
    }
    internal static void ReleaseAll(WorldState w,string actionId)
    {
        foreach(var r in w.Time!.ActionRuntime!.Reservations.Values.Where(r=>r.ActionId==actionId&&r.Remaining>0).ToArray())
            Settle(w,r.Id,r.Remaining,false,r.OwnerId,"UnusedReservationReleased");
    }
    public static void DefineCapacity(WorldState w,string resourceId,int capacity)
    {
        var runtime=w.Time?.ActionRuntime ?? throw new InvalidOperationException("R2 not enabled");
        if(string.IsNullOrWhiteSpace(resourceId)||capacity<=0||runtime.Capacities.ContainsKey(resourceId))throw new InvalidOperationException("Invalid or duplicate capacity definition");
        runtime.Capacities.Add(resourceId,new(){Capacity=capacity});
    }
    public static void ReserveCapacity(WorldState w,string resourceId,string actionId,int units)
    {
        var runtime=w.Time?.ActionRuntime ?? throw new InvalidOperationException("R2 not enabled");
        if(!runtime.Actions.TryGetValue(actionId,out var action)||ActionLifecycle.Terminal(action.Phase)||
            !runtime.Capacities.TryGetValue(resourceId,out var pool)||units<=0)throw new InvalidOperationException("Unknown capacity or action");
        if(pool.Leases.TryGetValue(actionId,out int existing)){if(existing!=units)throw new InvalidOperationException("Changed capacity claim");return;}
        if(pool.Leases.Values.Sum(v=>(long)v)+units>pool.Capacity)throw new InvalidOperationException("Facility capacity already reserved");
        pool.Leases.Add(actionId,units);
    }
}
