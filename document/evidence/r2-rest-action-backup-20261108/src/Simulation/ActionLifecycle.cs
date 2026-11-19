using Cultivation.Domain;

namespace Cultivation.Simulation;

public static class ActionLifecycle
{
    public static bool Terminal(ActionPhase phase) => phase is ActionPhase.Completed or ActionPhase.Interrupted or ActionPhase.Failed or ActionPhase.Cancelled;
    internal static bool EquipmentChange(ActionKind kind)=>kind is ActionKind.EquipCraftedItem or ActionKind.UnequipCraftedItem or ActionKind.EquipWeapon or ActionKind.UnequipWeapon;
    internal static bool SectCommand(ActionKind kind)=>kind is ActionKind.AcceptSectWork or ActionKind.BuyContribution or ActionKind.RedeemContribution or ActionKind.SetSectStockReservation;
    internal static bool PartyHuntCommand(ActionKind kind)=>PartyActions.Handles(kind)||kind==ActionKind.AcceptHunt;
    // Existing operations resolved by the deferred event path; no new gameplay rules.
    internal static bool AdditionalDeferred(ActionKind kind)=>SocialActions.Handles(kind)||GovernanceActions.Handles(kind)||SettlementActions.Handles(kind)||ExternalMarketActions.Handles(kind)||OrganizationLaborActions.Handles(kind)||kind is ActionKind.InvestSettlementPopulation or ActionKind.CancelBuilding or ActionKind.Wait;
    internal static bool Supports(ActionKind kind) => AdditionalDeferred(kind) || PartyHuntCommand(kind) || SectCommand(kind) || EquipmentChange(kind) || MarketOrders.Handles(kind) || MortalTrainingActions.Handles(kind) || LifeActions.IsWork(kind) || kind is ActionKind.RequestLifeService or ActionKind.Travel or ActionKind.RepairEquipment or ActionKind.Transfer or ActionKind.Gather or ActionKind.SectGather or ActionKind.ConstructBuilding or ActionKind.LearnMethod or ActionKind.Buy or ActionKind.Sell or ActionKind.UseMedicine or ActionKind.DeliverMission or ActionKind.PublishMission or ActionKind.AcceptMission or ActionKind.CancelMission;
    public static void EnableFresh(WorldState w)
    {
        if (w.Time?.DiscreteEvents is not { WorldTime: 0 } || w.Time.ActionRuntime is not null ||
            w.Time.Charges.Count != 0 || w.Pending.Count != 0 || w.Scheduled.Count != 0 || w.MortalTrainingWork.Count != 0 || w.LifeSystem?.Work.Count > 0)
            throw new InvalidOperationException("R2 requires an explicitly enabled fresh idle seconds world; no save migration");
        w.Time.ActionRuntime = new();
    }
    internal static void Begin(WorldState w, ActionIntent i)
    {
        if (w.Time?.ActionRuntime is not { } runtime || !Supports(i.Kind) || (AdditionalDeferred(i.Kind) || PartyHuntCommand(i.Kind) || SectCommand(i.Kind) || EquipmentChange(i.Kind) || i.Kind == ActionKind.Transfer || i.Kind == ActionKind.Gather || i.Kind == ActionKind.SectGather || i.Kind == ActionKind.ConstructBuilding || i.Kind == ActionKind.LearnMethod || i.Kind == ActionKind.Buy || i.Kind == ActionKind.Sell || i.Kind == ActionKind.UseMedicine || MissionActions.IsMission(i.Kind)) && w.Time.DiscreteEvents?.IndependentWorldEvents != true) return;
        if (runtime.Actions.ContainsKey(i.Id)) throw new InvalidOperationException("Action instance already exists");
        long now = w.Time.DiscreteEvents!.WorldTime;
        var job = i.Kind == ActionKind.RequestLifeService ? PaidLifeServices.ProviderIntent(i) : i.Kind==ActionKind.RepairEquipment?i with{ActorId=i.TargetId}:i;
        long duration = MortalTrainingActions.Handles(i.Kind) ? (MethodTraining.IsMethod(i) ? w.Methods!.Catalog.TrainingRules.DurationTicks : w.MortalActionRules!.DurationTicks) * i.Quantity :
            LifeActions.IsWork(job.Kind) ? LifeActions.Cost(w, job) : TimeActions.Cost(w, i);
        if(i.Kind==ActionKind.Wait && w.Time.ActionDriven?.WaitDurationTicks is { } waitDuration)duration=waitDuration;
        long due = checked(now + checked(duration * DiscreteTime.SecondsPerStep(w)));
        if(i.Kind==ActionKind.RepairEquipment)due=checked(now+w.Time.EquipmentRepair!.Rules.DurationMinutes*60L);
        var action = new ActionInstance(i.Id, i.ActorId, job.ActorId, i.Kind, now, due,
            MortalTrainingActions.Handles(i.Kind) ? checked((int)i.Quantity) : 1)
        {ResourceId=i.ResourceId,TargetId=AdditionalDeferred(i.Kind)||PartyHuntCommand(i.Kind)||SectCommand(i.Kind)||i.Kind is ActionKind.Transfer or ActionKind.Buy or ActionKind.Sell or ActionKind.DeliverMission or ActionKind.PublishMission or ActionKind.AcceptMission or ActionKind.CancelMission?i.TargetId:null,StaminaPolicy=MortalTrainingActions.Handles(i.Kind)?ConsumptionPolicy.PerCompletedSegment:ConsumptionPolicy.PaidUpfrontNoRefund};
        runtime.Actions.Add(i.Id, action);
        action.StaminaAtStart=w.Characters.Single(a=>a.Id==job.ActorId).Stamina?.Current??0;
        w.Time.DiscreteEvents.Versions.TryAdd(i.Id, 0);
        action.Version = w.Time.DiscreteEvents.Versions[i.Id];
        foreach (var phase in new[]{ActionPhase.Proposed,ActionPhase.Validated}) Transition(w, action, phase, "Accepted by shared validator");
    }
    internal static void Start(WorldState w, string id)
    {
        if (w.Time?.ActionRuntime is not { } runtime || !runtime.Actions.TryGetValue(id, out var action)) return;
        if (action.Phase != ActionPhase.Validated) throw new InvalidOperationException("Action was not validated");
        action.ActionPointsPaid = PaidActionPoints(w,action);
        action.StaminaConsumed=Math.Max(0,action.StaminaAtStart-(w.Characters.Single(a=>a.Id==action.WorkerId).Stamina?.Current??0));
        Transition(w, action, ActionPhase.Reserved, "Existing ledger/AP reservation completed");
        Transition(w, action, ActionPhase.Running, "Resources reserved; existing resolver started");
    }
    internal static int PaidActionPoints(WorldState w,ActionInstance action)=>w.Time!.Charges.Where(c=>
        c.ActionId==action.Id&&c.ActorId==action.ActorId||action.Kind==ActionKind.RequestLifeService&&c.ActionId=="service-work:"+action.Id&&c.ActorId==action.WorkerId).Sum(c=>c.Cost);
    internal static void RequireRunning(WorldState w, string id)
    {
        if (w.Time?.ActionRuntime is { } runtime && (!runtime.Actions.TryGetValue(id, out var action) ||
            action.Phase != ActionPhase.Running || action.Version != w.Time.DiscreteEvents!.Versions[id]))
            throw new InvalidOperationException("Action is terminal, missing, or its version is invalid");
    }
    internal static void Checkpoint(WorldState w, string id, int stamina = 0)
    {
        if (w.Time?.ActionRuntime is not { } runtime) return;
        RequireRunning(w,id); var action=runtime.Actions[id];
        if (stamina < 0 || action.CompletedSegments >= action.TotalSegments) throw new InvalidOperationException("Duplicate action checkpoint");
        action.CompletedSegments++; action.StaminaConsumed=checked(action.StaminaConsumed+stamina);
    }
    internal static void Finish(WorldState w, string id, ActionPhase phase, string reason)
    {
        if (w.Time?.ActionRuntime is not { } runtime || !runtime.Actions.TryGetValue(id,out var action)) return;
        if (Terminal(action.Phase)) return;
        if (!Terminal(phase) || phase==ActionPhase.Completed && action.CompletedSegments!=action.TotalSegments)
            throw new InvalidOperationException("Invalid terminal action transition");
        ReservationService.ReleaseAll(w,id);
        foreach(var pool in runtime.Capacities.Values) pool.Leases.Remove(id);
        new EventScheduler(w.Time.DiscreteEvents!).Cancel(id);
        action.Version=w.Time.DiscreteEvents!.Versions[id];
        Transition(w,action,phase,reason);
        EventDecisions.Request(w,action.ActorId,phase==ActionPhase.Completed?DecisionTrigger.ActionCompleted:DecisionTrigger.Interrupted);
        if(action.WorkerId!=action.ActorId)EventDecisions.Request(w,action.WorkerId,phase==ActionPhase.Completed?DecisionTrigger.ActionCompleted:DecisionTrigger.Interrupted);
    }
    private static void Transition(WorldState w,ActionInstance action,ActionPhase phase,string reason)
    {
        action.Phase=phase;
        action.Transitions.Add(new(phase,w.Time!.DiscreteEvents!.WorldTime,reason));
        ActionExecutor.Emit(w,action.Id,"ActionPhaseChanged",phase+":"+reason);
    }
}
