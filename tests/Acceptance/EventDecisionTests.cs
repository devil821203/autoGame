using Cultivation.Domain;
using Cultivation.Simulation;
using Cultivation.AI;
using Cultivation.Persistence;
using System.Text.Json;

internal static class EventDecisionTests
{
    private static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    private sealed class EmptyPlanner:IActionPlanner
    {
        public int Calls;
        public List<string> Actors { get; } = [];
        public IReadOnlyList<ScoredAction> Evaluate(LocalObservation o){Calls++;Actors.Add(o.Actor.Id);return [];}
    }
    internal static WorldState Fresh(bool real=false)
    {
        var w=real?TestScenarioCatalog.Create("methods"):DemoWorld.Create();
        if(!real){var c=GameConfiguration.Find();var driven=c.Read<ActionDrivenRules>("time/action-driven.json");
            w.Time=new(){Rules=c.Read<TimeRules>("time/calendar.json") with {TicksPerDay=driven.MinutesPerDay!.Value/driven.MinutesPerActionPoint!.Value},ActionDriven=driven};}
        DiscreteTime.EnableFresh(w);ActionLifecycle.EnableFresh(w);EventDecisions.EnableFresh(w);return w;
    }
    internal static WorldState MissionWorld(bool membersOnly,bool automatic=false)
    {
        var w=LifeEventScenario.Create();
        w.SectDuty=automatic?w.SectDuty! with {MissionIntervalTicks=1}:null;w.TestProcurement=null;w.HuntRules=null;
        var events=w.Time!.DiscreteEvents!;new EventScheduler(events).Cancel("$hunt-publication");events.PeriodicDeadlines.Remove("$hunt-publication");
        WorldPeriodFixture.Reset(w);
        int policy=w.OrganizationPolicies.FindIndex(p=>p.OrganizationId=="sect");
        w.OrganizationPolicies[policy]=w.OrganizationPolicies[policy] with {MissionsMembersOnly=membersOnly};
        foreach(string id in new[]{"player","npc:00"}){
            int n=w.Characters.FindIndex(a=>a.Id==id);var a=w.Characters[n] with {OrganizationId="sect",Position=id=="player"?OrganizationPosition.Elder:OrganizationPosition.Member,DemoLocationId="sect"};
            w.Characters[n]=a with {HexPosition=RegionGeometry.Resolve(w.HexMap!,a,"sect")};
        }
        int outsider=w.Characters.FindIndex(a=>a.Id=="npc:01");w.Characters[outsider]=w.Characters[outsider] with {OrganizationId=null,Position=OrganizationPosition.None};
        MoveRemote(w,"npc:02");
        w.MemberDuties.RemoveAll(d=>w.Characters.Single(a=>a.Id==d.ActorId).OrganizationId!=d.OrganizationId);
        WorldPeriodFixture.Reset(w);w.Time.EventDecisions!.Ready.Clear();return w;
    }
    private static void MoveRemote(WorldState w,string id)
    {
        int n=w.Characters.FindIndex(a=>a.Id==id);var a=w.Characters[n] with {RegionId=w.HexMap!.Regional!.Regions.First(r=>r.Id!="valley").Id,DemoLocationId="town"};
        w.Characters[n]=a with {HexPosition=RegionGeometry.Resolve(w.HexMap!,a,"town")};
    }
    private static WorldState MissionAct(WorldState w,ActionKind kind,string actor="player",long quantity=1)
    {
        var e=new SimulationEngine(w);var intent=new ActionIntent("r3-board:"+kind+":"+w.NextSequence,actor,"sect","herb",quantity,0,
            actor=="player"?DecisionSource.Player:DecisionSource.Npc,kind,MissionId:kind==ActionKind.PublishMission?null:"r3-board",
            Offer:kind==ActionKind.PublishMission?new("r3-board",2,4,8,8):null);
        Check(ActionValidator.Validate(w,intent) is null,"Mission fixture action rejected: "+kind);e.Submit(intent);e.AdvanceTo(w.Time!.DiscreteEvents!.WorldTime+600);
        var result=e.Snapshot();Check(result.Events.Any(x=>x.ActionId==intent.Id&&x.Kind is "MissionPublished" or "MissionAccepted" or "MissionDelivered" or "MissionCancelled"),"Mission fixture did not complete: "+kind);
        return result;
    }
    private static WorldState GovernanceWorld()
    {
        var w=MissionWorld(false);int n=w.Organizations.FindIndex(o=>o.Id=="sect");string? former=w.Organizations[n].LeaderId;
        w.Organizations[n]=w.Organizations[n] with {LeaderId="player"};
        for(int i=0;i<w.Characters.Count;i++){
            var a=w.Characters[i];if(a.Id=="player")w.Characters[i]=a with {Position=OrganizationPosition.Leader};
            else if(a.Id==former)w.Characters[i]=a with {Position=OrganizationPosition.Member};
        }
        return w;
    }
    private static WorldState Govern(WorldState w,ActionKind kind,string actor="player",string target="npc:00",OrganizationPolicy? policy=null)
    {
        var i=new ActionIntent("r3-govern:"+w.NextSequence,actor,target,"organization",1,0,actor=="player"?DecisionSource.Player:DecisionSource.Npc,kind,
            OrganizationId:kind is ActionKind.SetOrganizationPolicy or ActionKind.ApplyToOrganization or ActionKind.LeaveOrganization?null:"sect",Policy:policy);
        Check(ActionValidator.Validate(w,i) is null,"Governance fixture rejected: "+kind);var e=new SimulationEngine(w);e.Submit(i);e.AdvanceTo(w.Time!.DiscreteEvents!.WorldTime+600);
        var result=e.Snapshot();Check(result.Events.Any(x=>x.ActionId==i.Id&&x.Kind=="Governance"),"Governance fixture did not complete: "+kind);return result;
    }
    private static WorldState RiskMedicineWorld()
    {
        var w=MissionWorld(false);const string actor="npc:03";int n=w.Characters.FindIndex(a=>a.Id==actor);
        var a=w.Characters[n] with {Health=20,DemoLocationId="sect"};w.Characters[n]=a with {HexPosition=RegionGeometry.Resolve(w.HexMap!,a,"sect")};
        w.Ledger.Add(new(w.NextSequence++,0,"risk-powder-fixture","$genesis",actor,"bleeding-powder",1,"Genesis"));
        var e=new SimulationEngine(w);e.Submit(new("r3-risk-powder",actor,actor,"bleeding-powder",1,0,DecisionSource.Npc,ActionKind.UseMedicine));e.AdvanceTo(600);
        w=e.Snapshot();Check(w.LifeSystem!.Effects.Any(x=>x.ActorId==actor),"Powder fixture failed");w.Time!.EventDecisions!.Ready.Clear();return w;
    }
    private static void RiskBoard(WorldState w)
    {
        w.Characters=w.Characters.Select(a=>a.Id is "player" or "npc:00"?a with {BaseAttack=1,BaseDefense=100}:a).ToList();
        w.BoardRules=new(100);w.BoardBattle=new(){Id="r3-risk-board",Fighters=[new("player",BattleSide.Left,new(0,0),10,1),new("npc:00",BattleSide.Right,new(0,0),9,1)]};
        w.Time!.EventDecisions!.Ready.Clear();
    }
    private static void RiskRound(SimulationEngine e,bool attack=true)
    {
        var b=e.Snapshot().BoardBattle!;
        foreach(string actor in new[]{"player","npc:00"})e.Submit(new($"r3-risk-turn:{b.Round}:{actor}",actor,b.Id,"board-turn",1,0,actor=="player"?DecisionSource.Player:DecisionSource.Npc,ActionKind.BoardTurn,
            BoardTurn:new(b.Id,b.Round+1,AttackTargetId:attack&&actor=="player"?"npc:00":null)));
        e.Step(false);
    }
    public static IEnumerable<(string Name,Action Body)> Cases=>new (string,Action)[]{
        ("R3 actual medicine pulse wakes only its healed owner at deadline and survives snapshot",()=>{
            var w=RiskMedicineWorld();var effect=w.LifeSystem!.Effects.Single(x=>x.ActorId=="npc:03");long due=effect.NextAtSeconds!.Value;
            int health=w.Characters.Single(a=>a.Id=="npc:03").Health;var e=new SimulationEngine(w);e.AdvanceTo(due-1);
            Check(!e.Snapshot().Time!.EventDecisions!.Ready.ContainsKey("npc:03"),"Medicine woke owner before pulse");
            var copy=new SimulationEngine(WorldCodec.Clone(e.Snapshot()));e.AdvanceTo(due);copy.AdvanceTo(due);w=e.Snapshot();
            Check(w.Characters.Single(a=>a.Id=="npc:03").Health>health&&w.Time!.EventDecisions!.Ready.TryGetValue("npc:03",out var request)&&request.Reasons.Contains(DecisionTrigger.StateChanged),"Actual heal missed owner wake");
            Check(w.Time!.EventDecisions!.Ready.Values.Where(r=>r.Reasons.Contains(DecisionTrigger.StateChanged)).All(r=>r.ActorId=="npc:03"),"Private medicine state leaked");
            Check(WorldCodec.Digest(w)==WorldCodec.Digest(copy.Snapshot()),"Medicine state continuation differs");
        }),
        ("R3 medicine without health change never wakes its owner or revives incapacitated actors",()=>{
            foreach(int health in new[]{0,100}){
                var w=RiskMedicineWorld();int n=w.Characters.FindIndex(a=>a.Id=="npc:03");w.Characters[n]=w.Characters[n] with {Health=health};
                long due=w.LifeSystem!.Effects.Single(x=>x.ActorId=="npc:03").NextAtSeconds!.Value;var e=new SimulationEngine(w);e.AdvanceTo(due);w=e.Snapshot();
                Check(w.Characters[n].Health==health&&!w.Time!.EventDecisions!.Ready.ContainsKey("npc:03"),"Zero heal created state notification or revival");WorldIntegrity.Validate(w);
            }
        }),
        ("R3 battle damage is targeted and retained while the world planner stays asleep",()=>{
            var w=MissionWorld(false);RiskBoard(w);var p=new EmptyPlanner();var e=new SimulationEngine(w,p);RiskRound(e,false);
            Check(e.Snapshot().Time!.EventDecisions!.Ready.Count==0,"Waiting battle round emitted risk notification");
            RiskRound(e);w=e.Snapshot();
            Check(w.Characters.Single(a=>a.Id=="npc:00").Health<100&&w.Time!.EventDecisions!.Ready.TryGetValue("npc:00",out var request)&&request.Reasons.Contains(DecisionTrigger.StateChanged),"Damage missed affected combatant");
            Check(w.Time!.EventDecisions!.Ready.Keys.All(id=>id is "player" or "npc:00"),"Combat risk leaked to nonparticipants");
            Check(e.AdvanceTo(w.Time.DiscreteEvents!.WorldTime+600,true).Reason==AdvanceStopReason.PlayerBattle&&p.Calls==0,"World planner ran during frozen player battle");
            var copy=new SimulationEngine(WorldCodec.Clone(e.Snapshot()),new EmptyPlanner());RiskRound(e);RiskRound(copy);
            Check(WorldCodec.Digest(e.Snapshot())==WorldCodec.Digest(copy.Snapshot()),"Battle risk continuation differs");
        }),
        ("R3 durability loss wakes its owner once per battle without repeated wear notifications",()=>{
            var e=new SimulationEngine(MissionWorld(false));e.Submit(new("r3-risk-craft","player","player","iron-sword",1,0,DecisionSource.Player,ActionKind.CraftLifeItem));e.AdvanceTo(0);
            var w=e.Snapshot();e.AdvanceTo(w.Time!.ActionRuntime!.Actions["r3-risk-craft"].DueAt);w=e.Snapshot();string gear=w.LifeSystem!.Equipment.Single(x=>x.ActionId=="r3-risk-craft").Id;
            e.Submit(new("r3-risk-equip","player","player",gear,1,0,DecisionSource.Player,ActionKind.EquipCraftedItem));e.AdvanceTo(w.Time!.DiscreteEvents!.WorldTime);w=e.Snapshot();
            e.AdvanceTo(w.Time!.ActionRuntime!.Actions["r3-risk-equip"].DueAt);w=e.Snapshot();Check(w.Characters.Single(a=>a.Id=="player").CraftedWeaponId==gear,"Risk gear not equipped");
            int before=EquipmentCondition.Remaining(w,gear)??throw new Exception("Missing risk gear durability");RiskBoard(w);e=new(w,new EmptyPlanner());RiskRound(e);w=e.Snapshot();
            Check(EquipmentCondition.Remaining(w,gear)==before-w.EquipmentDurability!.Rules.LossPerBattle&&w.Time!.EventDecisions!.Ready.TryGetValue("player",out var owner)&&owner.Reasons.Contains(DecisionTrigger.StateChanged),"Durability loss missed owner");
            w.Time!.EventDecisions!.Ready.Clear();e=new(w,new EmptyPlanner());RiskRound(e);w=e.Snapshot();
            Check(EquipmentCondition.Remaining(w,gear)==before-w.EquipmentDurability!.Rules.LossPerBattle&&!w.Time!.EventDecisions!.Ready.ContainsKey("player"),"Repeated hit wore or notified unchanged gear");WorldIntegrity.Validate(w);
        }),
        ("R3 appointments and expulsion wake only the changed member and survive snapshot",()=>{
            var w=GovernanceWorld();
            foreach(var kind in new[]{ActionKind.PromoteMember,ActionKind.DemoteMember,ActionKind.ExpelMember}){
                w.Time!.EventDecisions!.Ready.Clear();var copy=WorldCodec.Clone(w);w=Govern(w,kind);copy=Govern(copy,kind);
                Check(w.Time!.EventDecisions!.Ready.TryGetValue("npc:00",out var request)&&request.Reasons.Contains(DecisionTrigger.StateChanged),"Changed member missed governance update");
                Check(w.Time.EventDecisions.Ready.Keys.All(id=>id is "player" or "npc:00"),"Private appointment leaked to unrelated actors");
                var member=w.Characters.Single(a=>a.Id=="npc:00");Check(member.Position==(kind==ActionKind.PromoteMember?OrganizationPosition.Elder:kind==ActionKind.DemoteMember?OrganizationPosition.Member:OrganizationPosition.None),"Appointment status differs");
                Check(WorldCodec.Digest(w)==WorldCodec.Digest(copy),"Governance continuation differs");WorldIntegrity.Validate(w);
            }
        }),
        ("R3 voluntary membership changes notify the member and organization leader",()=>{
            var w=GovernanceWorld();w.OrganizationPolicies=w.OrganizationPolicies.Select(p=>p.OrganizationId=="sect"?p with {OpenApplications=true}:p).ToList();
            foreach(var kind in new[]{ActionKind.ApplyToOrganization,ActionKind.LeaveOrganization}){
                w.Time!.EventDecisions!.Ready.Clear();w=Govern(w,kind,"npc:01","sect");var ready=w.Time!.EventDecisions!.Ready;
                Check(ready.TryGetValue("npc:01",out var member)&&member.Reasons.Contains(DecisionTrigger.StateChanged)&&ready.TryGetValue("player",out var leader)&&leader.Reasons.Contains(DecisionTrigger.KnownNotification),"Membership participants missed update");
                Check(ready.Keys.All(id=>id is "npc:01" or "player"),"Membership change broadcast to unrelated actors");WorldIntegrity.Validate(w);
            }
        }),
        ("R3 changed public policy wakes local observers while identical policy does not",()=>{
            var w=GovernanceWorld();var policy=w.OrganizationPolicies.Single(p=>p.OrganizationId=="sect") with {OpenApplications=true};
            w=Govern(w,ActionKind.SetOrganizationPolicy,target:"sect",policy:policy);var ready=w.Time!.EventDecisions!.Ready;
            Check(ready.TryGetValue("npc:00",out var member)&&member.Reasons.Contains(DecisionTrigger.KnownNotification)&&ready.TryGetValue("npc:01",out var outsider)&&outsider.Reasons.Contains(DecisionTrigger.KnownNotification),"Local policy observers missed change");
            Check(!ready.ContainsKey("npc:02"),"Policy leaked to remote observer");
            w.Time.EventDecisions.Ready.Clear();w=Govern(w,ActionKind.SetOrganizationPolicy,target:"sect",policy:policy);
            Check(w.Time!.EventDecisions!.Ready.Keys.All(id=>id=="player"),"Identical policy woke observers");WorldIntegrity.Validate(w);
        }),
        ("R3 rejected governance does not wake or mutate its intended target",()=>{
            var w=GovernanceWorld();var e=new SimulationEngine(w);e.Submit(new("r3-denied-govern","npc:01","npc:00","organization",1,0,DecisionSource.Npc,ActionKind.ExpelMember,OrganizationId:"sect"));e.AdvanceTo(0);w=e.Snapshot();
            Check(w.Events.Any(x=>x.ActionId=="r3-denied-govern"&&x.Kind=="Rejected")&&w.Characters.Single(a=>a.Id=="npc:00").OrganizationId=="sect","Denied governance changed member");
            Check(!w.Time!.EventDecisions!.Ready.ContainsKey("npc:00"),"Denied governance notified target");WorldIntegrity.Validate(w);
        }),
        ("R3 resource credit wakes only recipient after deferred or reserved transfer completes",()=>{
            foreach(long duration in new[]{0L,3L}){
                var w=MissionWorld(false);const string recipient="npc:03";long before=Inventory.Balance(w,recipient,"spirit-stone");
                var e=new SimulationEngine(w);e.Submit(new("r3-credit","player",recipient,"spirit-stone",1,duration,DecisionSource.Player,ActionKind.Transfer));e.AdvanceTo(0);w=e.Snapshot();
                Check(!w.Time!.EventDecisions!.Ready.ContainsKey(recipient)&&Inventory.Balance(w,recipient,"spirit-stone")==before,"Escrow or acceptance woke recipient early");
                long due=w.Time.ActionRuntime!.Actions["r3-credit"].DueAt;var copy=new SimulationEngine(WorldCodec.Clone(w));
                e.AdvanceTo(due-1);Check(!e.Snapshot().Time!.EventDecisions!.Ready.ContainsKey(recipient),"Recipient woke before credit");
                e.AdvanceTo(due);copy.AdvanceTo(due);w=e.Snapshot();
                Check(Inventory.Balance(w,recipient,"spirit-stone")==before+1&&w.Time!.EventDecisions!.Ready.TryGetValue(recipient,out var request)&&request.Reasons.Contains(DecisionTrigger.StateChanged),"Successful credit missed recipient wake");
                Check(w.Time!.EventDecisions!.Ready.Values.Where(r=>r.Reasons.Contains(DecisionTrigger.StateChanged)).Select(r=>r.ActorId).SequenceEqual(new[]{recipient}),"Private credit woke unrelated actors");
                Check(WorldCodec.Digest(w)==WorldCodec.Digest(copy.Snapshot()),"Credit continuation differs");WorldIntegrity.Validate(w);
            }
        }),
        ("R3 failed transfer never wakes intended recipient",()=>{
            var w=MissionWorld(false);const string recipient="npc:03";long before=Inventory.Balance(w,recipient,"spirit-stone");var e=new SimulationEngine(w);
            e.Submit(new("r3-no-credit","player",recipient,"spirit-stone",1,0,DecisionSource.Player,ActionKind.Transfer));e.AdvanceTo(0);w=e.Snapshot();
            long available=Inventory.Balance(w,"player","spirit-stone");w.Ledger.Add(new(w.NextSequence++,w.Clock.Tick,"deplete-credit-fixture","player","sect","spirit-stone",available,"FixtureTransfer"));
            e=new(w);e.AdvanceTo(w.Time!.ActionRuntime!.Actions["r3-no-credit"].DueAt);w=e.Snapshot();
            Check(w.Time!.ActionRuntime!.Actions["r3-no-credit"].Phase==ActionPhase.Failed&&Inventory.Balance(w,recipient,"spirit-stone")==before,"Failed transfer credited resources");
            Check(!w.Time.EventDecisions!.Ready.ContainsKey(recipient),"Failed transfer woke recipient");WorldIntegrity.Validate(w);
        }),
        ("R3 organization account credit creates no character request",()=>{
            var w=MissionWorld(false);var e=new SimulationEngine(w);e.Submit(new("r3-org-credit","player","sect","spirit-stone",1,0,DecisionSource.Player,ActionKind.Transfer));e.AdvanceTo(600);w=e.Snapshot();
            Check(w.Ledger.Any(l=>l.ActionId=="r3-org-credit"&&l.To=="sect"&&l.Reason=="Transfer"),"Organization credit failed");
            string leader=w.Organizations.Single(o=>o.Id=="sect").LeaderId!;
            Check(!w.Time!.EventDecisions!.Ready.ContainsKey("sect")&&w.Time.EventDecisions.Ready.TryGetValue(leader,out var notice)&&notice.Reasons.Contains(DecisionTrigger.StateChanged),"Organization credit missed leader or made account an actor");
            Check(w.Time.EventDecisions.Ready.Keys.All(id=>id=="player"||id==leader),"Organization credit leaked to unrelated actor");WorldIntegrity.Validate(w);
        }),
        ("R3 busy recipient retains resource notification until its own action completes",()=>{
            var w=MissionWorld(false);const string recipient="npc:03";w.Time!.ActionDriven=w.Time.ActionDriven! with {WaitDurationTicks=2};var e=new SimulationEngine(w,new EmptyPlanner());
            e.Submit(new("r3-busy-recipient",recipient,recipient,"time",1,0,DecisionSource.Npc,ActionKind.Wait));
            e.Submit(new("r3-busy-credit","player",recipient,"spirit-stone",1,0,DecisionSource.Player,ActionKind.Transfer));e.AdvanceTo(600,true);w=e.Snapshot();
            Check(w.Time!.EventDecisions!.Ready.TryGetValue(recipient,out var request)&&request.Reasons.Contains(DecisionTrigger.StateChanged)&&!w.Time.EventDecisions.Traces.ContainsKey(recipient),"Busy recipient lost notification or evaluated early");
            var copy=new SimulationEngine(WorldCodec.Clone(w),new EmptyPlanner());e.AdvanceTo(1200,true);copy.AdvanceTo(1200,true);w=e.Snapshot();
            Check(w.Time!.EventDecisions!.Traces.TryGetValue(recipient,out var trace)&&trace.Reasons.Contains(DecisionTrigger.StateChanged)&&trace.Reasons.Contains(DecisionTrigger.ActionCompleted),"Busy recipient did not merge credit and completion");
            Check(WorldCodec.Digest(w)==WorldCodec.Digest(copy.Snapshot()),"Busy credit continuation differs");
        }),
        ("R3 mission publication wakes local eligible observers without remote or private leakage",()=>{
            foreach(bool privateBoard in new[]{false,true}){
                var w=MissionAct(MissionWorld(privateBoard),ActionKind.PublishMission);var ready=w.Time!.EventDecisions!.Ready;
                Check(ready.TryGetValue("npc:00",out var member)&&member.Reasons.Contains(DecisionTrigger.KnownNotification),"Member missed publication");
                Check(ready.ContainsKey("npc:01")==!privateBoard,"Private board leaked or public observer missed");
                Check(!ready.ContainsKey("npc:02"),"Unknown remote publication leaked");WorldIntegrity.Validate(w);
            }
        }),
        ("R3 accepted contract cancellation and expiry notify moved participants with deterministic continuation",()=>{
            foreach(bool expire in new[]{false,true}){
                var w=MissionAct(MissionWorld(true),ActionKind.PublishMission);w=MissionAct(w,ActionKind.AcceptMission,"npc:00");
                MoveRemote(w,"npc:00");w.Time!.EventDecisions!.Ready.Clear();
                if(expire){var a=new SimulationEngine(w);var b=new SimulationEngine(WorldCodec.Clone(w));a.AdvanceTo(4800);b.AdvanceTo(4800);w=a.Snapshot();Check(WorldCodec.Digest(w)==WorldCodec.Digest(b.Snapshot()),"Mission expiry continuation differs");}
                else w=MissionAct(w,ActionKind.CancelMission);
                var ready=w.Time!.EventDecisions!.Ready;
                Check(ready.TryGetValue("npc:00",out var participant)&&participant.Reasons.Contains(DecisionTrigger.KnownNotification),"Moved participant missed contract update");
                Check(!ready.ContainsKey("npc:02")&&!ready.ContainsKey("npc:01"),"Private contract update leaked to nonparticipants");
                Check(w.Missions.Single(m=>m.Id=="r3-board").Status==(expire?MissionStatus.Expired:MissionStatus.Cancelled),"Mission did not close");WorldIntegrity.Validate(w);
            }
        }),
        ("R3 partial and final mission deliveries wake board observers while keeping payment rules",()=>{
            var w=MissionAct(MissionWorld(false),ActionKind.PublishMission);w=MissionAct(w,ActionKind.AcceptMission);
            for(int delivered=1;delivered<=2;delivered++){
                w.Time!.EventDecisions!.Ready.Clear();w=MissionAct(w,ActionKind.DeliverMission);
                Check(w.Time!.EventDecisions!.Ready.ContainsKey("npc:01")&&!w.Time.EventDecisions.Ready.ContainsKey("npc:02"),"Quota update observer scope wrong");
                var mission=w.Missions.Single(m=>m.Id=="r3-board");Check(mission.Delivered==delivered&&w.Deliveries.Where(d=>d.MissionId==mission.Id).Sum(d=>d.Payment)==delivered*4,"Delivery payment changed");
                Check(mission.Status==(delivered==2?MissionStatus.Completed:MissionStatus.Active),"Delivery status wrong");
            }
            WorldIntegrity.Validate(w);
        }),
        ("R3 automatic mission publication uses shared board visibility",()=>{
            var e=new SimulationEngine(MissionWorld(true,true));e.AdvanceTo(600);var w=e.Snapshot();
            Check(w.Events.Any(x=>x.Kind=="AutoMissionPublished"),"No automatic publication");
            Check(w.Time!.EventDecisions!.Ready.ContainsKey("npc:00")&&!w.Time.EventDecisions.Ready.ContainsKey("npc:01")&&!w.Time.EventDecisions.Ready.ContainsKey("npc:02"),"Automatic board visibility bypassed");WorldIntegrity.Validate(w);
        }),
        ("R3 busy action retains notification FIFO and merges completion after snapshot",()=>{
            foreach(bool independent in new[]{false,true}){
                var w=independent?LifeEventScenario.Create():Fresh();var mode=w.Time!.EventDecisions!;mode.Ready.Clear();
                string id=independent?"npc:00":"npc";
                EventDecisions.Request(w,id,DecisionTrigger.KnownNotification);long sequence=mode.Ready[id].Sequence;
                var p=new EmptyPlanner();var e=new SimulationEngine(w,p);
                e.Submit(new("busy-notification",id,id,"time",1,0,DecisionSource.Npc,ActionKind.Wait));e.AdvanceTo(0,true);
                w=e.Snapshot();Check(w.Time!.Deferred.Any(t=>t.Intent.Id=="busy-notification"),"Wait not started");
                Check(w.Time.EventDecisions!.Ready.TryGetValue(id,out var request)&&request.Sequence==sequence&&request.Reasons.Contains(DecisionTrigger.KnownNotification),"Busy request dropped");
                Check(!w.Time.EventDecisions.Traces.ContainsKey(id),"Busy actor evaluated");
                if(independent)Check(!w.Time.DiscreteEvents!.Pending.Any(x=>x.Kind==SimulationEventKind.DecisionDrain&&x.Version==w.Time.DiscreteEvents.Versions[x.CorrelationId]),"Busy actor polls decision drain");
                e.AdvanceTo(599,true);w=e.Snapshot();Check(!w.Time!.EventDecisions!.Traces.ContainsKey(id),"Early evaluation");
                var resumed=new SimulationEngine(WorldCodec.Clone(w),new EmptyPlanner());e.AdvanceTo(600,true);resumed.AdvanceTo(600,true);w=e.Snapshot();
                Check(w.Time!.EventDecisions!.Traces.TryGetValue(id,out var trace)&&trace.Reasons.Contains(DecisionTrigger.KnownNotification)&&trace.Reasons.Contains(DecisionTrigger.ActionCompleted),"Completion lost deferred reason");
                Check(WorldCodec.Digest(w)==WorldCodec.Digest(resumed.Snapshot()),"Busy completion snapshot differs");
            }
        }),
        ("R3 busy FIFO head does not block ready actors and regains priority when finished",()=>{
            var w=Fresh();w.PlayerAutoSchedule=true;w.Rules=w.Rules with {DecisionBudget=1};w.Time!.EventDecisions!.Ready.Clear();
            EventDecisions.Request(w,"npc",DecisionTrigger.KnownNotification);EventDecisions.Request(w,"player",DecisionTrigger.StateChanged);
            long original=w.Time.EventDecisions.Ready["npc"].Sequence;var p=new EmptyPlanner();var e=new SimulationEngine(w,p);
            e.Submit(new("busy-head","npc","npc","time",1,0,DecisionSource.Npc,ActionKind.Wait));e.AdvanceTo(0,true);
            Check(p.Actors.SequenceEqual(new[]{"player"}),"Busy head blocked eligible follower");w=e.Snapshot();
            Check(w.Time!.EventDecisions!.Ready["npc"].Sequence==original,"Busy FIFO position changed");
            EventDecisions.Request(w,"player",DecisionTrigger.KnownNotification);e=new(w,p);e.AdvanceTo(600,true);w=e.Snapshot();
            Check(p.Actors.SequenceEqual(new[]{"player","npc"})&&w.Time!.EventDecisions!.Ready.ContainsKey("player"),"Busy head lost priority on completion");
        }),
        ("R3 AP-only busy request keeps reasons until its exact available window",()=>{
            var w=LifeEventScenario.Create();w.Time!.EventDecisions!.Ready.Clear();const string id="npc:00";
            int n=w.Time.Budgets.FindIndex(b=>b.ActorId==id);Check(n>=0,"Missing AP fixture");
            w.Time.Budgets[n]=w.Time.Budgets[n] with {BusyThroughTick=2};
            EventDecisions.Request(w,id,DecisionTrigger.KnownNotification);long sequence=w.Time.EventDecisions.Ready[id].Sequence;
            var e=new SimulationEngine(w,new EmptyPlanner());e.AdvanceTo(1199,true);w=e.Snapshot();
            Check(w.Time!.EventDecisions!.Ready[id].Sequence==sequence&&!w.Time.EventDecisions.Traces.ContainsKey(id),"AP wait discarded request or evaluated early");
            var copy=new SimulationEngine(WorldCodec.Clone(w),new EmptyPlanner());e.AdvanceTo(1200,true);copy.AdvanceTo(1200,true);w=e.Snapshot();
            Check(w.Time!.EventDecisions!.Traces.TryGetValue(id,out var trace)&&trace.AtSeconds==1200&&trace.Reasons.Contains(DecisionTrigger.KnownNotification),"AP deadline lost reason");
            Check(WorldCodec.Digest(w)==WorldCodec.Digest(copy.Snapshot()),"AP deadline continuation differs");
        }),
        ("R3 FIFO reaches planner unchanged by actor IDs and legacy cursor",()=>{
            var w=Fresh();w.PlayerAutoSchedule=true;w.Time!.EventDecisions!.Ready.Clear();
            var ids=w.Characters.Select(a=>a.Id).OrderDescending(StringComparer.Ordinal).Take(2).ToArray();
            Check(ids.Length==2,"FIFO fixture needs two autonomous actors");w.Rules=w.Rules with {DecisionBudget=ids.Length};w.DecisionCursor=ids[0];
            foreach(string id in ids)EventDecisions.Request(w,id,DecisionTrigger.StateChanged);
            var p=new EmptyPlanner();var resumedPlanner=new EmptyPlanner();var e=new SimulationEngine(w,p);var resumed=new SimulationEngine(WorldCodec.Clone(w),resumedPlanner);
            e.AdvanceTo(0,true);resumed.AdvanceTo(0,true);
            Check(p.Actors.SequenceEqual(ids)&&resumedPlanner.Actors.SequenceEqual(ids),"Planner reordered FIFO batch");
            Check(WorldCodec.Digest(e.Snapshot())==WorldCodec.Digest(resumed.Snapshot()),"FIFO continuation differs");
        }),
        ("R3 same-second notifications retain reasons without another evaluation",()=>{
            var w=Fresh();w.Time!.EventDecisions!.Ready.Clear();string id=w.Characters.First(a=>a.Id!=w.ControlledCharacterId).Id;
            EventDecisions.Request(w,id,DecisionTrigger.Startup);var p=new EmptyPlanner();var e=new SimulationEngine(w,p);e.AdvanceTo(0,true);
            w=e.Snapshot();long before=w.Time!.EventDecisions!.Evaluations;
            EventDecisions.Request(w,id,DecisionTrigger.StateChanged);EventDecisions.Request(w,id,DecisionTrigger.KnownNotification);
            long sequence=w.Time.EventDecisions.Ready[id].Sequence;
            e=new(w,p);e.AdvanceTo(0,true);e.AdvanceTo(0,true);w=e.Snapshot();
            Check(w.Time!.EventDecisions!.Evaluations==before&&p.Calls==1,"Same-second actor evaluated twice");
            Check(w.Time.EventDecisions.Ready[id].Sequence==sequence&&w.Time.EventDecisions.Ready[id].Reasons.Count==2,"Deferred notification lost FIFO or reasons");
            var resumed=new SimulationEngine(WorldCodec.Clone(w),new EmptyPlanner());e.AdvanceTo(600,true);resumed.AdvanceTo(600,true);
            Check(e.Snapshot().Time!.EventDecisions!.Traces[id].Reasons.Length==2,"Deferred reasons missing from next evaluation");
            Check(WorldCodec.Digest(e.Snapshot())==WorldCodec.Digest(resumed.Snapshot()),"Deferred continuation differs");
        }),
        ("R3 independent busy world defers same-second wakes to completion without polling",()=>{
            var w=LifeEventScenario.Create();w.Time!.EventDecisions!.Ready.Clear();const string id="npc:00";
            EventDecisions.Request(w,id,DecisionTrigger.Startup);var e=new SimulationEngine(w,new EmptyPlanner());e.AdvanceTo(0,true);
            w=e.Snapshot();long before=w.Time!.EventDecisions!.Evaluations;
            EventDecisions.Request(w,id,DecisionTrigger.KnownNotification);e=new(w,new EmptyPlanner());
            Check(e.AdvanceTo(0,true).Reason==AdvanceStopReason.TargetReached,"Same-time drain loop exhausted budget");w=e.Snapshot();
            Check(w.Time!.EventDecisions!.Evaluations==before&&w.Time.EventDecisions.Ready.ContainsKey(id),"Independent wake evaluated twice or lost");
            var live=w.Time.DiscreteEvents!.Pending.Where(x=>x.CorrelationId=="$decision-drain"&&x.Version==w.Time.DiscreteEvents.Versions[x.CorrelationId]).ToArray();
            Check(live.Length==0,"Busy notification created a polling drain");
            var resumed=new SimulationEngine(WorldCodec.Clone(w),new EmptyPlanner());e.AdvanceTo(600,true);resumed.AdvanceTo(600,true);
            Check(WorldCodec.Digest(e.Snapshot())==WorldCodec.Digest(resumed.Snapshot()),"Independent deferred continuation differs");
        }),
        ("R3 targeted notification invalidates idle retry without duplicating it",()=>{
            var w=Fresh();var p=new EmptyPlanner();var e=new SimulationEngine(w,p);e.AdvanceTo(0,true);w=e.Snapshot();
            string id=p.Actors[0];var events=w.Time!.DiscreteEvents!;
            var retry=events.Pending.Single(x=>x.Kind==SimulationEventKind.DecisionRetry&&x.ActorId==id&&x.Version==events.Versions[x.CorrelationId]);
            EventDecisions.Request(w,id,DecisionTrigger.KnownNotification);EventDecisions.Request(w,id,DecisionTrigger.StateChanged);
            Check(events.Versions[retry.CorrelationId]!=retry.Version,"Old retry is still live");
            e=new(w,p);e.AdvanceTo(600,true);w=e.Snapshot();events=w.Time!.DiscreteEvents!;
            Check(events.Pending.Count(x=>x.Kind==SimulationEventKind.DecisionRetry&&x.ActorId==id&&x.Version==events.Versions[x.CorrelationId])==1,"Replacement retry duplicated");
            Check(w.Time.EventDecisions!.Traces[id].Reasons.All(r=>r!=DecisionTrigger.IdleRetry),"Canceled retry contaminated trigger reasons");
        }),
        ("R3 social invitations and responses wake only direct participants",()=>{
            foreach(var response in new[]{ActionKind.AcceptSocialOffer,ActionKind.DeclineSocialOffer}){
                var w=MissionWorld(false);w.Time!.EventDecisions!.Ready.Clear();
                var e=new SimulationEngine(w);
                e.Submit(new("r3-social","npc:00","player","social",1,0,DecisionSource.Npc,ActionKind.InviteCooperation));
                e.AdvanceTo(600);w=e.Snapshot();
                Check(w.SocialOffers.Any(o=>o.Id=="r3-social"),"Invitation failed");
                Check(w.Time!.EventDecisions!.Ready.Keys.Order().SequenceEqual(new[]{"npc:00","player"}),"Invitation woke unrelated actors or missed recipient");
                Check(w.Time.EventDecisions.Ready["player"].Reasons.Contains(DecisionTrigger.KnownNotification),"Missing private notification");
                w.Time.EventDecisions.Ready.Clear();e=new(w);
                e.Submit(new("r3-response","player","npc:00","social",1,0,DecisionSource.Player,response,SocialOfferId:"r3-social"));e.AdvanceTo(1200);
                w=e.Snapshot();
                Check(w.Time!.EventDecisions!.Ready.Keys.Order().SequenceEqual(new[]{"npc:00","player"}),"Response missed inviter or woke unrelated actors");
                WorldIntegrity.Validate(w);
            }
        }),
        ("R3 social expiration wakes both participants once and survives snapshot",()=>{
            var w=Fresh();w.SocialRules=GameConfiguration.Find().Read<SocialRules>("organizations/social.json");
            w.Time!.IndependentSocialExpiryEvents=true;w.Time.EventDecisions!.Ready.Clear();
            var e=new SimulationEngine(w);e.Submit(new("r3-expiry","npc","player","social",1,0,DecisionSource.Npc,ActionKind.InviteCooperation));e.AdvanceTo(600);
            w=e.Snapshot();long due=w.SocialOffers.Single(o=>o.Id=="r3-expiry").ExpiresAtSeconds!.Value;
            w.Time!.EventDecisions!.Ready.Clear();e=new(w);e.AdvanceTo(due-1);
            Check(e.Snapshot().Time!.EventDecisions!.Ready.Count==0,"Early expiration wake");
            var copy=new SimulationEngine(WorldCodec.Clone(e.Snapshot()));e.AdvanceTo(due);copy.AdvanceTo(due);
            w=e.Snapshot();Check(w.Time!.EventDecisions!.Ready.Keys.Order().SequenceEqual(new[]{"npc","player"}),"Expiry recipients wrong");
            Check(WorldCodec.Digest(w)==WorldCodec.Digest(copy.Snapshot()),"Expiry continuation differs");
            long sequence=w.Time.EventDecisions.NextSequence;e.AdvanceTo(due+1);
            Check(e.Snapshot().Time!.EventDecisions!.NextSequence==sequence,"Repeated expiration wake");
        }),
        ("R3 no event does not call planner and idle retry survives snapshot",()=>{
            var w=Fresh();var p=new EmptyPlanner();var e=new SimulationEngine(w,p);e.AdvanceTo(0,true);
            int first=p.Calls;Check(first>0,"No startup decision");
            long retry=e.Snapshot().Time!.DiscreteEvents!.Pending.Where(x=>x.Kind==SimulationEventKind.DecisionRetry).Min(x=>x.DueAt);
            e.AdvanceTo(retry-1,true);Check(p.Calls==first,"Planner ran without trigger");
            var copy=new SimulationEngine(WorldCodec.Clone(e.Snapshot()),p);copy.AdvanceTo(retry,true);
            Check(p.Calls>first,"Retry lost on snapshot");
        }),
        ("R3 directed notifications merge and leave unrelated actors asleep",()=>{
            var w=Fresh();var p=new EmptyPlanner();var e=new SimulationEngine(w,p);e.AdvanceTo(0,true);w=e.Snapshot();
            string id=w.Characters.First(a=>a.Id!=w.ControlledCharacterId).Id;
            EventDecisions.Request(w,id,DecisionTrigger.KnownNotification);EventDecisions.Request(w,id,DecisionTrigger.StateChanged);
            Check(w.Time!.EventDecisions!.Ready.Count==1&&w.Time.EventDecisions.Ready[id].Reasons.Count==2,"Not deduplicated");
            int before=p.Calls;e=new(w,p);e.AdvanceTo(0,true);Check(p.Calls==before,"Duplicate same-second planner call");
            e.AdvanceTo(600,true);Check(p.Calls==before+1,"Unrelated planner call");
            Check(e.Snapshot().Time!.EventDecisions!.Traces[id].Reasons.Length==2,"Lost trace triggers");
        }),
        ("R3 remote market facts require local observation and expire",()=>{
            var w=Fresh(true);int n=w.Characters.FindIndex(a=>a.Id=="player");
            w.Characters[n]=w.Characters[n] with {DemoLocationId="grove"};
            Check(ActorKnowledge.ReadMarket(w,"player","herb") is null,"Remote price leaked");
            w.Characters[n]=w.Characters[n] with {DemoLocationId="town"};var fact=ActorKnowledge.ReadMarket(w,"player","herb");Check(fact is not null,"Local observation absent");
            w.Characters[n]=w.Characters[n] with {DemoLocationId="grove"};
            w.MarketListings=w.MarketListings.Select(l=>l with {BasePrice=l.BasePrice+100}).ToList();
            Check(ActorKnowledge.ReadMarket(w,"player","herb")==fact,"Cached fact changed");
            w.Time!.DiscreteEvents!.WorldTime=fact!.ExpiresAt;
            Check(ActorKnowledge.ReadMarket(w,"player","herb") is null,"Expired fact returned");
        }),
        ("R3 decision budget preserves FIFO requests and deterministic continuation",()=>{
            var w=Fresh(true);w.Rules=w.Rules with {DecisionBudget=1};
            var ordered=w.Time!.EventDecisions!.Ready.Values.OrderBy(r=>r.Sequence).Select(r=>r.ActorId).ToArray();
            var e=new SimulationEngine(w,new EmptyPlanner());e.AdvanceTo(0,true);var first=e.Snapshot();
            Check(first.Time!.EventDecisions!.Evaluations==1&&first.Time.EventDecisions.Traces.ContainsKey(ordered[0]),"Budget/start order wrong");
            var a=new SimulationEngine(WorldCodec.Clone(first),new EmptyPlanner());var b=new SimulationEngine(WorldCodec.Clone(first),new EmptyPlanner());
            a.AdvanceTo(600,true);b.AdvanceTo(600,true);
            Check(a.Snapshot().Time!.EventDecisions!.Evaluations==2&&a.Snapshot().Time!.EventDecisions!.Traces.ContainsKey(ordered[1]),"Waiting actor starved");
            Check(WorldCodec.Serialize(a.Snapshot())==WorldCodec.Serialize(b.Snapshot()),"Snapshot continuation differs");
        }),
        ("R3 format six protects queue and corrupt provenance is rejected",()=>{
            var w=Fresh(true);int n=w.Characters.FindIndex(a=>a.Id=="player");w.Characters[n]=w.Characters[n] with {DemoLocationId="town"};
            var fact=ActorKnowledge.ReadMarket(w,"player","herb")!;
            string path=Path.Combine(Path.GetTempPath(),"r3-"+Guid.NewGuid()+".json");
            try{var store=new JsonWorldStore();store.Save(path,w);Check(JsonDocument.Parse(File.ReadAllText(path)).RootElement.GetProperty("SchemaVersion").GetInt32()==6,"Wire guard absent");
                var copy=store.Load(path);Check(copy.Time!.EventDecisions!.Ready.Count==w.Time!.EventDecisions!.Ready.Count,"Ready requests lost");}
            finally{if(File.Exists(path))File.Delete(path);}
            w.Time!.EventDecisions!.MarketKnowledge["player"]["herb"]=fact with {Source="omniscient"};
            try{WorldIntegrity.Validate(w);throw new Exception("Accepted unknown source");}catch(InvalidDataException){}
        }),
        ("R3 real player autonomy grows methods across four days",()=>{
            var e=new SimulationEngine(Fresh(true));e.SetPlayerAutonomy(true);
            for(int day=1;day<=4;day++){
                long target=day*86400L;var result=e.AdvanceTo(target,true);
                Check(result.Reason==AdvanceStopReason.TargetReached,"Four-day probe stopped early: "+result.Reason);
                Console.WriteLine("R3_DAY_PROGRESS "+day+" "+JsonSerializer.Serialize(e.Snapshot().Methods!.Learners["player"]));
            }
            var w=e.Snapshot();Check(w.Time!.EventDecisions!.Evaluations>0&&w.Time.Charges.Any(c=>c.ActorId=="player"),"No player activities");
            Check(w.Methods!.Learners["player"].Values.Sum()>0,"Player did not train methods");
            WorldIntegrity.Validate(w);
            Console.WriteLine("R3_FOUR_DAY_EVIDENCE "+JsonSerializer.Serialize(new {w.Clock.Tick,Seconds=e.WorldTimeSeconds,w.Time.EventDecisions.Evaluations,w.Time.EventDecisions.PlannerCalls,Methods=w.Methods.Learners["player"],Actor=w.Characters.Single(a=>a.Id=="player")}));
        })
    };
}
