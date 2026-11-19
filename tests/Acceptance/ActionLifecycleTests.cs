using System.Text.Json;
using Cultivation.Domain;
using Cultivation.Simulation;
using Cultivation.Persistence;

internal static class ActionLifecycleTests
{
    private static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    private static WorldState World()
    {
        var w=TestScenarioCatalog.Create("methods");DiscreteTime.EnableFresh(w);ActionLifecycle.EnableFresh(w);return w;
    }
    private static ActionIntent Train(string id,long count=1,string actor="player")=>new(id,actor,actor,"method:QingyunFist",count,0,
        actor=="player"?DecisionSource.Player:DecisionSource.Npc,ActionKind.TrainMortalAttribute);
    private static WorldState ServiceWorld()
    {
        var w=World();var doctor=w.Characters.Single(a=>a.TestProfession==TestProfession.Physician);
        w.Characters=w.Characters.Select(a=>a.Id=="player"?a with {Health=50,DemoLocationId=doctor.DemoLocationId,HexPosition=doctor.HexPosition,RegionId=doctor.RegionId}:a).ToList();
        w.Ledger.Add(new(w.NextSequence++,0,"fixture-herbs","$genesis","player","herb",30,"Genesis"));
        WorldIntegrity.Validate(w);return w;
    }
    private static ActionIntent Service(WorldState w,ServiceMaterialSource source)=>new("medical","player",w.Characters.Single(a=>a.TestProfession==TestProfession.Physician).Id,"medical",1,0,
        DecisionSource.Player,ActionKind.RequestLifeService,LimitPrice:PaidLifeServices.Quote(w,"medical",source),ServiceMaterials:source);
    public static IEnumerable<(string Name,Action Body)> Cases=>new (string,Action)[]{
        ("R2 independent scheduled gathering rejects missing or mismatched action",()=>{
            var w=LifeEventScenario.Create();
            var region=w.HexMap!.Regional!.Regions[1];
            int index=w.Characters.FindIndex(c=>c.Id=="player");
            w.Characters[index]=w.Characters[index] with {RegionId=region.Id,HexPosition=region.Resource,DemoLocationId="grove"};
            var engine=new SimulationEngine(w);
            var intent=new ActionIntent("r2-reverse-gather","player","grove","herb",1,0,DecisionSource.Player,ActionKind.Gather);
            engine.Submit(intent);engine.AdvanceTo(0);w=engine.Snapshot();
            Check(w.Scheduled.Any(t=>t.Intent.Id==intent.Id)&&w.Time!.ActionRuntime!.Actions.ContainsKey(intent.Id),"Gather fixture did not start");
            foreach(bool missing in new[]{true,false}){
                var altered=WorldCodec.Clone(w);var runtime=altered.Time!.ActionRuntime!;
                if(missing){runtime.Actions.Remove(intent.Id);runtime.Reservations.Remove(intent.Id+":gather-resource");}
                else runtime.Actions[intent.Id]=runtime.Actions[intent.Id] with {Kind=ActionKind.Travel};
                bool rejected=false;try{new SimulationEngine(altered);}catch(InvalidDataException){rejected=true;}
                Check(rejected,"Accepted scheduled work without a matching action");
            }
        }),
        ("R2 method batch exposes shared phases checkpoint consumption and terminal version",()=>{
            var e=new SimulationEngine(World());e.Submit(Train("batch",6));e.AdvanceTo(600);
            var w=e.Snapshot();var a=w.Time!.ActionRuntime!.Actions["batch"];
            Check(a.Phase==ActionPhase.Running&&a.CompletedSegments==1&&a.StaminaConsumed==1&&a.ActionPointsPaid==6,"Checkpoint/AP wrong");
            Check(a.Transitions.Select(t=>t.Phase).SequenceEqual(new[]{ActionPhase.Proposed,ActionPhase.Validated,ActionPhase.Reserved,ActionPhase.Running}),"Missing lifecycle phases");
            e.AdvanceTo(3600);w=e.Snapshot();a=w.Time!.ActionRuntime!.Actions["batch"];
            Check(a.Phase==ActionPhase.Completed&&a.CompletedSegments==6&&a.StaminaConsumed==6&&a.Version==1&&w.Methods!.Learners["player"]["QingyunFist"]==6,"Terminal settlement wrong");
            string before=WorldCodec.Digest(w);e.Submit(Train("batch",6));e.AdvanceTo(3600);
            Check(e.Snapshot().Methods!.Learners["player"]["QingyunFist"]==6&&e.Snapshot().Time!.ActionRuntime!.Actions["batch"].Version==1,"Duplicate completed twice");
        }),
        ("R2 paid treatment routes fee and materials through existing ledger escrow once",()=>{
            foreach(var source in Enum.GetValues<ServiceMaterialSource>()){
                var w=ServiceWorld();var i=Service(w,source);long client=Inventory.Balance(w,"player","spirit-stone"),provider=Inventory.Balance(w,i.TargetId,"spirit-stone");
                var e=new SimulationEngine(w);e.Submit(i);e.AdvanceTo(0);w=e.Snapshot();
                Check(w.Time!.ActionRuntime!.Actions[i.Id].Phase==ActionPhase.Running&&w.Time.ActionRuntime.Reservations.Count==2,"Missing real fee/material reservation");
                long reserved=w.Events.Single(x=>x.ActionId==i.Id&&x.Kind=="ActionPhaseChanged"&&x.Detail.StartsWith("Reserved:",StringComparison.Ordinal)).Sequence;
                Check(w.Ledger.Where(x=>x.ActionId==i.Id&&x.Reason is "ServiceFeeReserved" or "LifeReserved").All(x=>x.Sequence<reserved),"Reserved phase emitted before actual escrow");
                Check(Inventory.Balance(w,i.TargetId,"spirit-stone")==provider,"Paid early");
                e.AdvanceTo(1200);w=e.Snapshot();
                Check(w.Time!.ActionRuntime!.Actions[i.Id].Phase==ActionPhase.Completed&&w.PaidServiceReceipts.Single().Success&&
                    Inventory.Balance(w,"player","spirit-stone")==client-i.LimitPrice&&Inventory.Balance(w,i.TargetId,"spirit-stone")==provider+i.LimitPrice,"Payment wrong");
                Check(w.Time.ActionRuntime.Reservations.Values.All(r=>r.Remaining==0),"Leaked escrow");
            }
        }),
        ("R2 service invalidation refunds fee and supplied material while retaining paid AP",()=>{
            var w=ServiceWorld();var i=Service(w,ServiceMaterialSource.Client);long money=Inventory.Balance(w,"player","spirit-stone"),herbs=Inventory.Balance(w,"player","herb");
            var e=new SimulationEngine(w);e.Submit(i);e.AdvanceTo(0);w=e.Snapshot();
            w.Characters=w.Characters.Select(a=>a.Id=="player"?a with {Hostile=true}:a).ToList();e=new(w);e.AdvanceTo(1200);w=e.Snapshot();
            Check(w.Time!.ActionRuntime!.Actions[i.Id].Phase==ActionPhase.Failed&&Inventory.Balance(w,"player","spirit-stone")==money&&Inventory.Balance(w,"player","herb")==herbs,"Refund changed");
            Check(w.Time.Charges.Any(c=>c.ActionId==i.Id)&&w.Time.ActionRuntime.Reservations.Values.All(r=>r.Remaining==0),"AP refunded or resources leaked");
        }),
        ("R2 capacity contention and resource reservations cannot overbook authoritative assets",()=>{
            var e=new SimulationEngine(World());e.Submit(Train("first",2));e.AdvanceTo(0);var w=e.Snapshot();
            ReservationService.DefineCapacity(w,"fixture-slot",1);ReservationService.ReserveCapacity(w,"fixture-slot","first",1);
            string other=w.Characters.Single(a=>a.Id=="npc:method-trainee").Id;
            e=new(w);e.Submit(Train("second",2,other));e.AdvanceTo(0);w=e.Snapshot();
            string before=WorldCodec.Digest(w);
            try{ReservationService.ReserveCapacity(w,"fixture-slot","second",1);throw new Exception("Overbooked");}catch(InvalidOperationException){}
            Check(before==WorldCodec.Digest(w),"Rejected slot reservation mutated state");
            w.Ledger.Add(new(w.NextSequence++,0,"fixture-scarce-herbs","$genesis","player","herb",5,"Genesis"));
            long stock=Inventory.Balance(w,"player","herb");Check(stock>0,"Fixture lacks finite herbs");
            var transfer=Train("first",2) with {ResourceId="herb",Quantity=stock};
            ReservationService.Reserve(w,transfer,"fixture-herbs","player","$escrow:first","FixtureReserved");
            ReservationService.Reserve(w,transfer,"fixture-herbs","player","$escrow:first","FixtureReserved");
            Check(Inventory.Balance(w,"player","herb")==0,"Duplicate reservation deducted twice");
            try{ReservationService.Reserve(w,transfer with {Id="second"},"fixture-extra","player","$escrow:second","FixtureReserved");throw new Exception("Over-reserved");}catch(InvalidOperationException){}
            e=new(w);e.AdvanceTo(1200);w=e.Snapshot();
            Check(w.Time!.ActionRuntime!.Capacities["fixture-slot"].Leases.Count==0&&Inventory.Balance(w,"player","herb")==stock,"Terminal unused resources not released");
        }),
        ("R2 live action reservations and versions survive format five save continuation",()=>{
            var e=new SimulationEngine(ServiceWorld());var i=Service(e.Snapshot(),ServiceMaterialSource.Client);e.Submit(i);e.AdvanceTo(600);
            string path=Path.Combine(Path.GetTempPath(),"r2-"+Guid.NewGuid().ToString("N")+".json");
            try{var store=new JsonWorldStore();store.Save(path,e.Snapshot());using var json=JsonDocument.Parse(File.ReadAllText(path));
                Check(json.RootElement.GetProperty("SchemaVersion").GetInt32()==5,"Old engine format guard missing");
                var restored=new SimulationEngine(store.Load(path));e.AdvanceTo(1200);restored.AdvanceTo(1200);
                Check(WorldCodec.Digest(e.Snapshot())==WorldCodec.Digest(restored.Snapshot()),"Resumed action differs");
            }finally{File.Delete(path);}
        }),
        ("R2 forged reservation totals and terminal work are rejected",()=>{
            var e=new SimulationEngine(ServiceWorld());e.Submit(Service(e.Snapshot(),ServiceMaterialSource.Provider));e.AdvanceTo(0);var w=e.Snapshot();
            w.Time!.ActionRuntime!.Reservations.Values.First().Released++;
            try{new SimulationEngine(w);throw new Exception("Forged escrow accepted");}catch(InvalidDataException){}
            w=e.Snapshot();w.Time!.ActionRuntime!.Actions["medical"].Phase=ActionPhase.Completed;
            try{new SimulationEngine(w);throw new Exception("Forged terminal action accepted");}catch(InvalidDataException){}
            w=e.Snapshot();var action=w.Time!.ActionRuntime!.Actions["medical"];
            w.Time.ActionRuntime.Actions["medical"]=action with {ActorId=action.WorkerId,ActionPointsPaid=w.Time.Charges.Where(c=>c.ActionId=="service-work:medical"&&c.ActorId==action.WorkerId).Sum(c=>c.Cost)};
            try{new SimulationEngine(w);throw new Exception("Forged service owner accepted");}catch(InvalidDataException){}
        }),
        ("R2 exhaustion terminates a batch without refunding AP or retaining event versions",()=>{
            var w=World();w.Characters=w.Characters.Select(a=>a.Id=="player"?a with {Stamina=a.Stamina! with {Current=2}}:a).ToList();
            var e=new SimulationEngine(w);e.Submit(Train("exhaust",6));e.AdvanceTo(1200);w=e.Snapshot();
            var action=w.Time!.ActionRuntime!.Actions["exhaust"];
            Check(action.Phase==ActionPhase.Interrupted&&action.CompletedSegments==2&&action.StaminaConsumed==2&&action.ActionPointsPaid==6&&
                w.Time.Budgets.Single(b=>b.ActorId=="player").Remaining==24,"Exhaustion refunded AP or lost progress");
        }),
        ("R2 actual attack interrupts paid work and refunds escrow exactly once",()=>{
            var w=ServiceWorld();var i=Service(w,ServiceMaterialSource.Client);long funds=Inventory.Balance(w,"player","spirit-stone"),herbs=Inventory.Balance(w,"player","herb");
            var e=new SimulationEngine(w);e.Submit(i);e.AdvanceTo(0);w=e.Snapshot();var player=w.Characters.Single(a=>a.Id=="player");
            string enemy=w.Characters.First(a=>a.Id!="player"&&a.Id!=i.TargetId&&a.MonsterKind==MonsterKind.None).Id;
            w.Characters=w.Characters.Select(a=>a.Id=="player"?a with {Hostile=true}:a.Id==enemy?a with {DemoLocationId=player.DemoLocationId,HexPosition=player.HexPosition,RegionId=player.RegionId}:a).ToList();
            e=new(w);e.Submit(new("attack",enemy,"player","combat",1,0,DecisionSource.Npc,ActionKind.Engage));e.AdvanceTo(600);w=e.Snapshot();
            Check(w.Time!.ActionRuntime!.Actions[i.Id].Phase==ActionPhase.Interrupted&&w.Time.ActionRuntime.Reservations.Values.All(r=>r.Remaining==0)&&
                Inventory.Balance(w,"player","spirit-stone")==funds&&Inventory.Balance(w,"player","herb")==herbs&&w.PaidServiceReceipts.Count==1,"Interrupted refund duplicated/missing");
        }),
        ("R2 demo and hex travel share lifecycle without changing routes or arrival",()=>{
            foreach(string scenario in new[]{"methods","settlement"}){
                var w=TestScenarioCatalog.Create(scenario);DiscreteTime.EnableFresh(w);ActionLifecycle.EnableFresh(w);
                var e=new SimulationEngine(w);e.Submit(new("journey","player","grove","travel",1,0,DecisionSource.Player,ActionKind.Travel));e.AdvanceTo(0);w=e.Snapshot();
                Check(w.Time!.ActionRuntime!.Actions.ContainsKey("journey"),"Travel rejected: "+scenario);
                e.AdvanceTo(w.Time.ActionRuntime.Actions["journey"].DueAt);w=e.Snapshot();
                Check(w.Time!.ActionRuntime!.Actions["journey"].Phase==ActionPhase.Completed&&w.Characters.Single(a=>a.Id=="player").DemoLocationId=="grove","Travel failed: "+scenario);
            }
        }),
        ("R2 cannot masquerade as R1 wire format when loading",()=>{
            string path=Path.Combine(Path.GetTempPath(),"r2-forged-"+Guid.NewGuid().ToString("N")+".json");
            try{var root=System.Text.Json.Nodes.JsonNode.Parse(WorldCodec.Serialize(World()))!;root["SchemaVersion"]=4;File.WriteAllText(path,root.ToJsonString());
                try{new JsonWorldStore().Load(path);throw new Exception("R2 accepted as schema four");}catch(InvalidDataException){}
            }finally{File.Delete(path);}
        }),
        ("R2 is opt in and new game preserves the explicit lifecycle mode",()=>{
            var old=TestScenarioCatalog.Create("methods");Check(old.Time!.ActionRuntime is null,"Legacy mode enabled silently");
            var w=World();var fresh=NewGameFactory.CreateLike(w,"new");Check(fresh.Time!.ActionRuntime is not null&&fresh.Time.ActionRuntime.Actions.Count==0,"New game lost mode");
        }),
        ("R2 pharmacy and smithing deliver real products with both material sources",()=>{
            foreach(var source in Enum.GetValues<ServiceMaterialSource>())foreach(string service in new[]{"ointment","bleeding-powder","iron-sword","leather-armor"}){
                var w=World();var role=service is "ointment" or "bleeding-powder"?TestProfession.Pharmacist:TestProfession.Smith;
                if(service is "ointment" or "bleeding-powder")w.Ledger.RemoveAll(l=>l.To=="player"&&l.ResourceId==service);
                var provider=w.Characters.Single(a=>a.TestProfession==role);
                w.Characters=w.Characters.Select(a=>a.Id=="player"?a with {DemoLocationId=provider.DemoLocationId,HexPosition=provider.HexPosition,RegionId=provider.RegionId,EquippedWeaponId=null,CraftedWeaponId=null,EquippedArmorId=null}:a).ToList();
                string owner=source==ServiceMaterialSource.Client?"player":provider.Id;
                foreach(var input in w.LifeSystem!.Recipes.Single(r=>r.Id==service).Inputs)
                    w.Ledger.Add(new(w.NextSequence++,0,"fixture:"+service+":"+input.Key,"$genesis",owner,input.Key,input.Value,"Genesis"));
                var i=new ActionIntent("craft","player",provider.Id,service,1,0,DecisionSource.Player,ActionKind.RequestLifeService,
                    LimitPrice:PaidLifeServices.Quote(w,service,source),ServiceMaterials:source);
                var e=new SimulationEngine(w);e.Submit(i);e.AdvanceTo(0);w=e.Snapshot();Check(w.Time!.ActionRuntime!.Actions.ContainsKey(i.Id),"Commission rejected: "+service);
                e.AdvanceTo(w.Time.ActionRuntime.Actions[i.Id].DueAt);w=e.Snapshot();var receipt=w.PaidServiceReceipts.Single();
                Check(receipt.Success&&receipt.Output is not null&&Inventory.Balance(w,"player",receipt.Output)==1&&
                    w.Time!.ActionRuntime!.Actions[i.Id].Phase==ActionPhase.Completed&&w.Time.ActionRuntime.Reservations.Values.All(r=>r.Remaining==0),"Product/settlement wrong: "+service);
            }
        }),
        ("R2 partial settlement releases only unused resources and rejects excess consumption",()=>{
            var w=World();w.Ledger.Add(new(w.NextSequence++,0,"fixture-partial","$genesis","player","herb",5,"Genesis"));
            var e=new SimulationEngine(w);e.Submit(Train("partial",2));e.AdvanceTo(0);w=e.Snapshot();
            ReservationService.Reserve(w,Train("partial",2) with {ResourceId="herb",Quantity=5},"partial-herbs","player","$escrow:partial","FixtureReserved");
            ReservationService.Settle(w,"partial-herbs",2,true,"consumed","FixtureConsumed");
            ReservationService.Settle(w,"partial-herbs",1,false,"player","FixtureRefunded");
            string before=WorldCodec.Digest(w);try{ReservationService.Settle(w,"partial-herbs",3,true,"consumed","FixtureConsumed");throw new Exception("Over-consumed");}catch(InvalidOperationException){}
            Check(before==WorldCodec.Digest(w),"Rejected consumption changed assets");
            e=new(w);e.AdvanceTo(1200);w=e.Snapshot();var r=w.Time!.ActionRuntime!.Reservations["partial-herbs"];
            Check(r.Consumed==2&&r.Released==3&&r.Remaining==0&&Inventory.Balance(w,"player","herb")==3,"Partial settlement or unused refund wrong");
        }),
        ("R2 external actions cannot reuse the provider AP charge namespace",()=>{
            var e=new SimulationEngine(World());string before=WorldCodec.Digest(e.Snapshot());
            Check(!e.ExecutePlayerActivity(Train("service-work:spoof")).Accepted&&before==WorldCodec.Digest(e.Snapshot()),"Internal charge id accepted");
        }),
        ("R2 malformed transition snapshots fail with an explicit data error",()=>{
            var e=new SimulationEngine(World());e.Submit(Train("null-history"));e.AdvanceTo(0);var w=e.Snapshot();
            w.Time!.ActionRuntime!.Actions["null-history"].Transitions[0]=null!;
            try{new SimulationEngine(w);throw new Exception("Null transition accepted");}catch(InvalidDataException){}
        })
    };
}
