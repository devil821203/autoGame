using Cultivation.Domain;

namespace Cultivation.Simulation;

internal static class ActionRuntimeIntegrity
{
    internal static void Validate(WorldState w)
    {
        if(w.Time?.ActionRuntime is not {} runtime)return;
        if(w.Time.DiscreteEvents is not {} events||runtime.FormatVersion!=1||runtime.Actions is null||runtime.Reservations is null||runtime.Capacities is null)
            throw new InvalidDataException("Invalid R2 runtime/version; seconds mode required");
        if(runtime.Actions.Values.Any(a=>a is null)||runtime.Reservations.Values.Any(r=>r is null)||runtime.Capacities.Values.Any(p=>p is null))
            throw new InvalidDataException("Null entry in R2 action/reservation/capacity snapshot");
        bool Owner(string id)=>w.Characters.Any(a=>a.Id==id)||w.Organizations.Any(o=>o.Id==id);
        foreach(var pair in runtime.Actions)
        {
            var a=pair.Value;
            if(pair.Key!=a.Id||string.IsNullOrWhiteSpace(a.Id)||string.IsNullOrWhiteSpace(a.ResourceId)||!Owner(a.ActorId)||!w.Characters.Any(c=>c.Id==a.WorkerId)||
                !ActionLifecycle.Supports(a.Kind)||!w.ProcessedIntentIds.Contains(a.Id)||!Enum.IsDefined(a.Phase)||
                a.Phase is ActionPhase.Proposed or ActionPhase.Validated or ActionPhase.Reserved||a.StartedAt<0||a.StartedAt>events.WorldTime||a.DueAt<a.StartedAt||
                a.TotalSegments<=0||a.CompletedSegments<0||a.CompletedSegments>a.TotalSegments||a.StaminaConsumed<0||a.StaminaAtStart<0||
                a.ApPolicy!=ConsumptionPolicy.PaidUpfrontNoRefund||a.StaminaPolicy!=(MortalTrainingActions.Handles(a.Kind)?ConsumptionPolicy.PerCompletedSegment:ConsumptionPolicy.PaidUpfrontNoRefund)||
                !w.Time.Charges.Any(c=>c.ActionId==a.Id&&c.ActorId==a.ActorId&&c.Cost>0)||
                a.ActionPointsPaid!=ActionLifecycle.PaidActionPoints(w,a)||
                !events.Versions.TryGetValue(a.Id,out long version)||version!=a.Version||a.Transitions is null||
                a.Transitions.Any(t=>t is null)||a.Transitions.Count!=(ActionLifecycle.Terminal(a.Phase)?5:4)||
                !a.Transitions.Take(4).Select(t=>t.Phase).SequenceEqual(new[]{ActionPhase.Proposed,ActionPhase.Validated,ActionPhase.Reserved,ActionPhase.Running})||
                a.Transitions.Last().Phase!=a.Phase||a.Transitions.Any(t=>t.AtSeconds<a.StartedAt||t.AtSeconds>events.WorldTime||string.IsNullOrWhiteSpace(t.Reason))||
                a.Transitions.Zip(a.Transitions.Skip(1)).Any(p=>p.First.AtSeconds>p.Second.AtSeconds)||
                a.Phase==ActionPhase.Completed&&a.CompletedSegments!=a.TotalSegments)
                throw new InvalidDataException("Invalid R2 action state/progress/AP/version/history: "+pair.Key);
            if (a.Kind == ActionKind.Gather)
            {
                var workItem = w.Scheduled.SingleOrDefault(t => t.Intent.Id == a.Id);
                if (!runtime.Reservations.TryGetValue(a.Id + ":gather-resource", out var gather) ||
                    gather.ActionId != a.Id || gather.ResourceId != a.ResourceId || gather.Account != ActionExecutor.Escrow(a.Id) ||
                    a.Phase == ActionPhase.Running && (workItem is null || workItem.Intent.ActorId != a.ActorId || gather.Remaining != gather.Amount) ||
                    a.Phase == ActionPhase.Completed && gather.Consumed != gather.Amount)
                    throw new InvalidDataException("R2 gather reservation mismatch: " + a.Id);
            }
            if (a.Kind == ActionKind.SectGather)
            {
                var task = w.Scheduled.SingleOrDefault(t => t.Intent.Id == a.Id);
                if (!runtime.Reservations.TryGetValue(a.Id + ":sect-gather-resource", out var herbs) ||
                    herbs.ActionId != a.Id || herbs.OwnerId != "grove" || herbs.ResourceId != a.ResourceId ||
                    herbs.Account != ActionExecutor.Escrow(a.Id) ||
                    a.Phase == ActionPhase.Running && (task is null || task.Intent.ActorId != a.ActorId || herbs.Remaining != herbs.Amount) ||
                    a.Phase == ActionPhase.Completed && herbs.Consumed != herbs.Amount)
                    throw new InvalidDataException("R2 sect gather reservation mismatch: " + a.Id);
            }
            if (a.Kind == ActionKind.ConstructBuilding)
            {
                if (!runtime.Reservations.TryGetValue(a.Id + ":building-funds", out var funds) ||
                    funds.ActionId != a.Id || funds.ResourceId != a.ResourceId || funds.Account != ActionExecutor.Escrow(a.Id) ||
                    a.Phase == ActionPhase.Running && funds.Remaining != funds.Amount ||
                    a.Phase == ActionPhase.Completed && funds.Consumed != funds.Amount ||
                    a.Phase == ActionPhase.Cancelled && funds.Released != funds.Amount)
                    throw new InvalidDataException("R2 construction reservation mismatch: " + a.Id);
            }
            if (a.Kind == ActionKind.LearnMethod)
            {
                var task = w.Scheduled.SingleOrDefault(t => t.Intent.Id == a.Id);
                if (!runtime.Reservations.TryGetValue(a.Id + ":advancement-resource", out var funds) || funds.ActionId != a.Id || funds.OwnerId != a.ActorId || funds.ResourceId != a.ResourceId || funds.Account != ActionExecutor.Escrow(a.Id) ||
                    a.Phase == ActionPhase.Running && (task is null || task.Intent.ActorId != a.ActorId || task.Intent.Kind != a.Kind || task.Intent.Quantity != funds.Amount || task.DueAtSeconds != a.DueAt || funds.Remaining != funds.Amount) ||
                    a.Phase == ActionPhase.Completed && funds.Consumed != funds.Amount)
                    throw new InvalidDataException("R2 learning reservation mismatch: " + a.Id);
            }
            if (ActionLifecycle.EquipmentChange(a.Kind))
            {
                var deferred=w.Time.Deferred.SingleOrDefault(t=>t.Intent.Id==a.Id);
                string eventKind=a.Kind is ActionKind.EquipCraftedItem or ActionKind.UnequipCraftedItem?"LifeEquipmentChanged":"Completed";
                if(a.Phase is not (ActionPhase.Running or ActionPhase.Completed or ActionPhase.Failed) ||
                    a.Phase==ActionPhase.Running && (deferred is null || deferred.Intent.Kind!=a.Kind || deferred.Intent.ActorId!=a.ActorId || deferred.Intent.TargetId!=a.ActorId || deferred.Intent.ResourceId!=a.ResourceId || deferred.DueAtSeconds!=a.DueAt) ||
                    a.Phase==ActionPhase.Completed && w.Events.Count(e=>e.ActionId==a.Id&&e.Kind==eventKind)!=1 ||
                    a.Phase==ActionPhase.Failed && w.Events.Any(e=>e.ActionId==a.Id&&e.Kind==eventKind))
                    throw new InvalidDataException("R2 equipment change mismatch: "+a.Id);
            }
            if (a.Kind is ActionKind.PublishMission or ActionKind.AcceptMission or ActionKind.CancelMission)
            {
                var deferred=w.Time.Deferred.SingleOrDefault(t=>t.Intent.Id==a.Id);
                string eventKind=a.Kind switch {ActionKind.PublishMission=>"MissionPublished",ActionKind.AcceptMission=>"MissionAccepted",_=>"MissionCancelled"};
                if(a.TargetId is null || !w.Organizations.Any(o=>o.Id==a.TargetId) || a.Phase is not (ActionPhase.Running or ActionPhase.Completed or ActionPhase.Failed) ||
                    a.Phase==ActionPhase.Running && (deferred is null || deferred.Intent.Kind!=a.Kind || deferred.Intent.ActorId!=a.ActorId || deferred.Intent.TargetId!=a.TargetId || deferred.Intent.ResourceId!=a.ResourceId || deferred.DueAtSeconds!=a.DueAt) ||
                    a.Phase==ActionPhase.Completed && w.Events.Count(e=>e.ActionId==a.Id&&e.Kind==eventKind)!=1 ||
                    a.Phase==ActionPhase.Failed && w.Events.Any(e=>e.ActionId==a.Id&&e.Kind==eventKind))
                    throw new InvalidDataException("R2 mission command mismatch: "+a.Id);
            }
            if (a.Kind == ActionKind.DeliverMission)
            {
                var deferred=w.Time.Deferred.SingleOrDefault(t=>t.Intent.Id==a.Id);
                if(a.TargetId is null || !w.Organizations.Any(o=>o.Id==a.TargetId) || a.Phase is not (ActionPhase.Running or ActionPhase.Completed or ActionPhase.Failed) ||
                    a.Phase==ActionPhase.Running && (deferred is null || deferred.Intent.Kind!=a.Kind || deferred.Intent.ActorId!=a.ActorId || deferred.Intent.TargetId!=a.TargetId || deferred.Intent.ResourceId!=a.ResourceId || deferred.DueAtSeconds!=a.DueAt) ||
                    a.Phase==ActionPhase.Completed && (w.Deliveries.Count(d=>d.ActionId==a.Id)!=1 || w.Ledger.Count(e=>e.ActionId==a.Id&&e.From==a.ActorId&&e.To==a.TargetId&&e.ResourceId==a.ResourceId&&e.Reason=="MissionDelivery")!=1 || w.Ledger.Count(e=>e.ActionId==a.Id&&e.To==a.ActorId&&e.Reason=="MissionPayment")!=1) ||
                    a.Phase==ActionPhase.Failed && (w.Deliveries.Any(d=>d.ActionId==a.Id) || w.Ledger.Any(e=>e.ActionId==a.Id&&e.Reason is "MissionDelivery" or "MissionPayment")))
                    throw new InvalidDataException("R2 mission delivery mismatch: "+a.Id);
            }
            if (ActionLifecycle.PartyHuntCommand(a.Kind))
            {
                var deferred=w.Time.Deferred.SingleOrDefault(t=>t.Intent.Id==a.Id);
                string eventKind=a.Kind==ActionKind.AcceptHunt?"HuntAccepted":a.Kind==ActionKind.WaitParty?"PartyWaiting":"Party"+a.Kind;
                if(string.IsNullOrWhiteSpace(a.TargetId) || a.Phase is not (ActionPhase.Running or ActionPhase.Completed or ActionPhase.Failed) ||
                    a.Phase==ActionPhase.Running && (deferred is null || deferred.Intent.Kind!=a.Kind || deferred.Intent.ActorId!=a.ActorId || deferred.Intent.TargetId!=a.TargetId || deferred.Intent.ResourceId!=a.ResourceId || deferred.DueAtSeconds!=a.DueAt) ||
                    a.Phase==ActionPhase.Completed && w.Events.Count(e=>e.ActionId==a.Id&&e.Kind==eventKind)!=1 ||
                    a.Phase==ActionPhase.Failed && w.Events.Any(e=>e.ActionId==a.Id&&e.Kind==eventKind))
                    throw new InvalidDataException("R2 party/hunt command mismatch: "+a.Id);
            }
            if (ActionLifecycle.SectCommand(a.Kind))
            {
                var deferred=w.Time.Deferred.SingleOrDefault(t=>t.Intent.Id==a.Id);
                string eventKind=a.Kind switch {ActionKind.AcceptSectWork=>"SectWorkAccepted",ActionKind.SetSectStockReservation=>"SectStockReservationChanged",_=>"ContributionExchange"};
                if(a.TargetId is null || !w.Organizations.Any(o=>o.Id==a.TargetId) || a.Phase is not (ActionPhase.Running or ActionPhase.Completed or ActionPhase.Failed) ||
                    a.Phase==ActionPhase.Running && (deferred is null || deferred.Intent.Kind!=a.Kind || deferred.Intent.ActorId!=a.ActorId || deferred.Intent.TargetId!=a.TargetId || deferred.Intent.ResourceId!=a.ResourceId || deferred.DueAtSeconds!=a.DueAt) ||
                    a.Phase==ActionPhase.Completed && w.Events.Count(e=>e.ActionId==a.Id&&e.Kind==eventKind)!=1 ||
                    a.Phase==ActionPhase.Failed && w.Events.Any(e=>e.ActionId==a.Id&&e.Kind==eventKind) ||
                    a.Phase==ActionPhase.Completed && a.Kind==ActionKind.BuyContribution && (w.Ledger.Count(e=>e.ActionId==a.Id&&e.Reason=="ContributionPurchasePayment")!=1||w.Ledger.Count(e=>e.ActionId==a.Id&&e.Reason=="ContributionPurchased")!=1) ||
                    a.Phase==ActionPhase.Completed && a.Kind==ActionKind.RedeemContribution && (w.Ledger.Count(e=>e.ActionId==a.Id&&e.Reason=="ContributionSpent")!=1||w.Ledger.Count(e=>e.ActionId==a.Id&&e.Reason=="ContributionRedemption")!=1))
                    throw new InvalidDataException("R2 sect command mismatch: "+a.Id);
            }
            if (a.Kind == ActionKind.UseMedicine)
            {
                var deferred=w.Time.Deferred.SingleOrDefault(t=>t.Intent.Id==a.Id);
                if(a.Phase is not (ActionPhase.Running or ActionPhase.Completed or ActionPhase.Failed) ||
                    a.Phase==ActionPhase.Running && (deferred is null || deferred.Intent.Kind!=a.Kind || deferred.Intent.ActorId!=a.ActorId || deferred.Intent.TargetId!=a.ActorId || deferred.Intent.ResourceId!=a.ResourceId || deferred.DueAtSeconds!=a.DueAt) ||
                    a.Phase==ActionPhase.Completed && w.Ledger.Count(e=>e.ActionId==a.Id&&e.From==a.ActorId&&e.To=="consumed"&&e.ResourceId==a.ResourceId&&e.Quantity==1&&e.Reason=="MedicineConsumed")!=1 ||
                    a.Phase==ActionPhase.Failed && w.Ledger.Any(e=>e.ActionId==a.Id&&e.Reason=="MedicineConsumed"))
                    throw new InvalidDataException("R2 deferred medicine mismatch: "+a.Id);
            }
            if (a.Kind is ActionKind.Buy or ActionKind.Sell)
            {
                var deferred=w.Time.Deferred.SingleOrDefault(t=>t.Intent.Id==a.Id);
                if(a.TargetId!="market" || a.Phase is not (ActionPhase.Running or ActionPhase.Completed or ActionPhase.Failed) ||
                    a.Phase==ActionPhase.Running && (deferred is null || deferred.Intent.Kind!=a.Kind || deferred.Intent.ActorId!=a.ActorId || deferred.Intent.TargetId!=a.TargetId || deferred.Intent.ResourceId!=a.ResourceId || deferred.DueAtSeconds!=a.DueAt) ||
                    a.Phase==ActionPhase.Completed && w.Trades.Count(t=>t.ActionId==a.Id&&t.ActorId==a.ActorId&&t.MarketId==a.TargetId&&t.Kind==a.Kind&&t.ResourceId==a.ResourceId)!=1 ||
                    a.Phase==ActionPhase.Failed && w.Trades.Any(t=>t.ActionId==a.Id))
                    throw new InvalidDataException("R2 deferred trade mismatch: "+a.Id);
            }
            if (a.Kind == ActionKind.Transfer)
            {
                var scheduled = w.Scheduled.FirstOrDefault(t => t.Intent.Id == a.Id);
                var deferred = w.Time.Deferred.SingleOrDefault(t => t.Intent.Id == a.Id);
                bool isDeferred = !runtime.Reservations.ContainsKey(a.Id + ":transfer-resource");
                if (isDeferred)
                {
                    if (a.TargetId is null || !Owner(a.TargetId) || a.TargetId == a.ActorId || a.Phase is not (ActionPhase.Running or ActionPhase.Completed or ActionPhase.Failed) ||
                        a.Phase == ActionPhase.Running && (deferred is null || deferred.Intent.Kind != ActionKind.Transfer || deferred.Intent.DurationTicks != 0 || deferred.Intent.ActorId != a.ActorId || deferred.Intent.TargetId != a.TargetId || deferred.Intent.ResourceId != a.ResourceId || deferred.DueAtSeconds != a.DueAt) ||
                        a.Phase == ActionPhase.Completed && w.Ledger.Count(e => e.ActionId == a.Id && e.From == a.ActorId && e.To == a.TargetId && e.ResourceId == a.ResourceId && e.Reason == "Transfer") != 1 ||
                        a.Phase == ActionPhase.Failed && w.Ledger.Any(e => e.ActionId == a.Id && e.Reason == "Transfer"))
                        throw new InvalidDataException("R2 deferred transfer mismatch: " + a.Id);
                }
                else
                {
                if (a.TargetId is null || !Owner(a.TargetId) || a.TargetId == a.ActorId ||
                    a.Phase is not (ActionPhase.Running or ActionPhase.Completed) ||
                    !runtime.Reservations.TryGetValue(a.Id + ":transfer-resource", out var transfer) ||
                    transfer.ActionId != a.Id || transfer.OwnerId != a.ActorId || transfer.ResourceId != a.ResourceId ||
                    transfer.Account != ActionExecutor.Escrow(a.Id) ||
                    w.Ledger.Count(e => e.ActionId == a.Id && e.From == a.ActorId && e.To == transfer.Account &&
                        e.ResourceId == transfer.ResourceId && e.Quantity == transfer.Amount && e.Reason == "Reserved") != 1 ||
                    a.Phase == ActionPhase.Running && (transfer.Remaining != transfer.Amount || scheduled is null ||
                        w.Scheduled.Count(t => t.Intent.Id == a.Id) != 1 || scheduled.Intent.ActorId != a.ActorId ||
                        scheduled.Intent.TargetId != a.TargetId || scheduled.Intent.ResourceId != a.ResourceId ||
                        scheduled.Intent.Quantity != transfer.Amount || scheduled.Intent.DurationTicks <= 0 || scheduled.DueAtSeconds != a.DueAt) ||
                    a.Phase == ActionPhase.Completed && (transfer.Consumed != transfer.Amount ||
                        w.Ledger.Count(e => e.ActionId == a.Id && e.From == transfer.Account && e.To == a.TargetId &&
                            e.ResourceId == transfer.ResourceId && e.Quantity == transfer.Amount && e.Reason == "Completed") != 1))
                    throw new InvalidDataException("R2 scheduled transfer reservation does not match ledger/action: " + a.Id);
                }
            }
            bool work=w.Time.EquipmentRepair?.Work.Any(t=>t.Intent.Id==a.Id)==true||w.Time.MarketOrders?.Commands.Any(t=>t.Intent.Id==a.Id)==true||w.MortalTrainingWork.Any(t=>t.Intent.Id==a.Id)||w.LifeSystem?.Work.Any(t=>t.Intent.Id==a.Id)==true||w.Time.Deferred.Any(t=>t.Intent.Id==a.Id&&t.Intent.Kind==a.Kind&&(ActionLifecycle.PartyHuntCommand(a.Kind)||ActionLifecycle.SectCommand(a.Kind)||ActionLifecycle.EquipmentChange(a.Kind)||MissionActions.IsMission(a.Kind)||a.Kind is ActionKind.Transfer or ActionKind.Buy or ActionKind.Sell or ActionKind.UseMedicine))||w.Scheduled.Any(t=>t.Intent.Id==a.Id&&(t.Intent.Kind==ActionKind.Travel||t.Intent.Kind==ActionKind.Transfer&&a.Kind==ActionKind.Transfer||t.Intent.Kind==ActionKind.LearnMethod&&a.Kind==ActionKind.LearnMethod||t.Intent.Kind==ActionKind.Gather&&a.Kind==ActionKind.Gather||t.Intent.Kind==ActionKind.SectGather&&a.Kind==ActionKind.SectGather||t.Intent.Kind==ActionKind.ConstructBuilding&&a.Kind==ActionKind.ConstructBuilding));
            if(work==ActionLifecycle.Terminal(a.Phase))throw new InvalidDataException("R2 action/work terminal mismatch: "+a.Id);
            if(ActionLifecycle.Terminal(a.Phase)&&(runtime.Reservations.Values.Any(r=>r.ActionId==a.Id&&r.Remaining!=0)||runtime.Capacities.Values.Any(p=>p.Leases.ContainsKey(a.Id))))
                throw new InvalidDataException("Terminal action retains resources/capacity");
        }
        foreach(var id in w.MortalTrainingWork.Select(t=>t.Intent.Id).Concat(w.LifeSystem?.Work.Select(t=>t.Intent.Id)??[]).Concat(w.Scheduled.Where(t=>t.Intent.Kind==ActionKind.Travel).Select(t=>t.Intent.Id)))
            if(!runtime.Actions.ContainsKey(id))throw new InvalidDataException("Live work missing R2 action instance");
        // Independent-world jobs must retain their matching live action; checking only
        // actions -> jobs lets a corrupted snapshot omit the action entirely.
        if (events.IndependentWorldEvents)
        {
            foreach (var task in w.Time.Deferred.Where(t=>t.Intent.Kind==ActionKind.Transfer&&t.Intent.DurationTicks==0))
                if (!runtime.Actions.TryGetValue(task.Intent.Id,out var action)||action.Kind!=ActionKind.Transfer||action.Phase!=ActionPhase.Running)
                    throw new InvalidDataException("Deferred transfer missing matching running R2 action: "+task.Intent.Id);
            foreach (var task in w.Time.Deferred.Where(t=>t.Intent.Kind is ActionKind.Buy or ActionKind.Sell))
                if (!runtime.Actions.TryGetValue(task.Intent.Id,out var action)||action.Kind!=task.Intent.Kind||action.Phase!=ActionPhase.Running)
                    throw new InvalidDataException("Deferred trade missing matching running R2 action: "+task.Intent.Id);
            foreach (var task in w.Time.Deferred.Where(t=>t.Intent.Kind==ActionKind.UseMedicine))
                if (!runtime.Actions.TryGetValue(task.Intent.Id,out var action)||action.Kind!=task.Intent.Kind||action.Phase!=ActionPhase.Running)
                    throw new InvalidDataException("Deferred medicine missing matching running R2 action: "+task.Intent.Id);
            foreach (var task in w.Time.Deferred.Where(t=>MissionActions.IsMission(t.Intent.Kind)))
                if (!runtime.Actions.TryGetValue(task.Intent.Id,out var action)||action.Kind!=task.Intent.Kind||action.Phase!=ActionPhase.Running)
                    throw new InvalidDataException("Deferred mission delivery missing matching running R2 action: "+task.Intent.Id);
            foreach (var task in w.Time.Deferred.Where(t=>ActionLifecycle.PartyHuntCommand(t.Intent.Kind)))
                if (!runtime.Actions.TryGetValue(task.Intent.Id,out var action)||action.Kind!=task.Intent.Kind||action.Phase!=ActionPhase.Running)
                    throw new InvalidDataException("Deferred party/hunt command missing matching running R2 action: "+task.Intent.Id);
            foreach (var task in w.Time.Deferred.Where(t=>ActionLifecycle.SectCommand(t.Intent.Kind)))
                if (!runtime.Actions.TryGetValue(task.Intent.Id,out var action)||action.Kind!=task.Intent.Kind||action.Phase!=ActionPhase.Running)
                    throw new InvalidDataException("Deferred sect command missing matching running R2 action: "+task.Intent.Id);
            foreach (var task in w.Time.Deferred.Where(t=>ActionLifecycle.EquipmentChange(t.Intent.Kind)))
                if (!runtime.Actions.TryGetValue(task.Intent.Id,out var action)||action.Kind!=task.Intent.Kind||action.Phase!=ActionPhase.Running)
                    throw new InvalidDataException("Deferred equipment change missing matching running R2 action: "+task.Intent.Id);
            foreach (var task in w.Scheduled.Where(t => t.Intent.Kind is ActionKind.Gather or ActionKind.SectGather or ActionKind.ConstructBuilding or ActionKind.LearnMethod ||
                t.Intent.Kind == ActionKind.Transfer && t.Intent.DurationTicks > 0))
            {
                if (!runtime.Actions.TryGetValue(task.Intent.Id, out var action) || action.Kind != task.Intent.Kind || action.Phase != ActionPhase.Running)
                    throw new InvalidDataException("Scheduled work missing matching running R2 action: " + task.Intent.Id);
            }
        }
        foreach(var task in w.MortalTrainingWork)
        {
            var a=runtime.Actions[task.Intent.Id];
            if(a.Kind!=task.Intent.Kind||a.ResourceId!=task.Intent.ResourceId||a.WorkerId!=task.Intent.ActorId||a.TotalSegments!=task.Intent.Quantity||a.CompletedSegments!=a.TotalSegments-task.RemainingIterations)
                throw new InvalidDataException("R2 checkpoint disagrees with live training work");
        }
        foreach(var task in w.LifeSystem?.Work??[])
        {
            var a=runtime.Actions[task.Intent.Id];
            if(a.WorkerId!=task.Intent.ActorId||a.ActorId!=(task.Service?.ClientId??task.Intent.ActorId)||
                a.Kind!=(task.Service is null?task.Intent.Kind:ActionKind.RequestLifeService)||a.ResourceId!=(task.Service?.ServiceId??task.Intent.ResourceId))
                throw new InvalidDataException("R2 owner/worker disagrees with live service work");
        }
        foreach(var receipt in w.PaidServiceReceipts)
            if(runtime.Actions.TryGetValue(receipt.ActionId,out var a)&&
                (a.ActorId!=receipt.Contract.ClientId||a.WorkerId!=receipt.Contract.ProviderId||a.ResourceId!=receipt.Contract.ServiceId||
                 (a.Phase==ActionPhase.Completed)!=receipt.Success))throw new InvalidDataException("R2 action disagrees with service settlement receipt");
        foreach(var pair in runtime.Reservations)
        {
            var r=pair.Value;
            if(pair.Key!=r.Id||string.IsNullOrWhiteSpace(r.Id)||!runtime.Actions.ContainsKey(r.ActionId)||!Owner(r.OwnerId)||
                !r.Account.StartsWith("$escrow:",StringComparison.Ordinal)||!w.Items.Any(i=>i.Id==r.ResourceId)||
                r.Amount<=0||r.Consumed<0||r.Released<0||r.Consumed>r.Amount||r.Released>r.Amount-r.Consumed||r.Policy!=ConsumptionPolicy.EscrowRefundUnused)
                throw new InvalidDataException("Invalid R2 reservation: "+pair.Key);
        }
        foreach(var group in runtime.Reservations.Values.GroupBy(r=>(r.Account,r.ResourceId)))
            if(Inventory.Balance(w,group.Key.Account,group.Key.ResourceId)!=group.Sum(r=>r.Remaining))throw new InvalidDataException("R2 reservation disagrees with authoritative ledger");
        foreach(var pair in runtime.Capacities)
            if(string.IsNullOrWhiteSpace(pair.Key)||pair.Value.Capacity<=0||pair.Value.Leases is null||pair.Value.Leases.Values.Sum(v=>(long)v)>pair.Value.Capacity||
                pair.Value.Leases.Any(l=>l.Value<=0||!runtime.Actions.TryGetValue(l.Key,out var a)||ActionLifecycle.Terminal(a.Phase)))
                throw new InvalidDataException("Invalid R2 capacity lease");
    }
}
