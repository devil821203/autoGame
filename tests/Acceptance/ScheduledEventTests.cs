using Cultivation.Domain;
using Cultivation.Simulation;
using Cultivation.Persistence;

internal static class ScheduledEventTests
{
    private static void Check(bool ok,string why){if(!ok)throw new Exception(why);}
    private static HexCoord Neighbor(WorldState w,string actor="player"){
        var at=HexNavigation.Position(w.HexMap!,w.Characters.Single(a=>a.Id==actor));
        return w.HexMap!.Tiles.First(t=>at.Neighbors().Contains(t.Coord)&&HexNavigation.Passable(t)&&RegionGeometry.Allows(w.HexMap,at,t.Coord)&&HexNavigation.Cost(w.HexMap,t)==1).Coord;
    }
    private static ActionIntent Travel(string id,string target,string actor="player")=>new(id,actor,target,"travel",1,0,actor=="player"?DecisionSource.Player:DecisionSource.Npc,ActionKind.Travel);
    public static IEnumerable<(string Name,Action Body)> Cases=>new (string,Action)[]{
        ("Scheduled seconds one hex preserves full duration from minute seventeen",()=>{
            var w=LifeEventScenario.Create();var target=Neighbor(w);var e=new SimulationEngine(w);e.AdvanceTo(1020);var before=e.Snapshot().Characters.Single(a=>a.Id=="player").HexPosition;
            e.Submit(Travel("one-hex",target.ToString()));e.AdvanceTo(1619);Check(e.Snapshot().Characters.Single(a=>a.Id=="player").HexPosition==before,"Early arrival");e.AdvanceTo(1620);
            Check(e.Snapshot().Characters.Single(a=>a.Id=="player").HexPosition==target&&e.Snapshot().Scheduled.Count==0,"Missing exact arrival");
        }),
        ("Scheduled seconds interactive cross-region route advances each segment without grid rounding",()=>{
            var w=LifeEventScenario.Create();string target=RegionGeometry.PlaceId(w.HexMap!,w.HexMap!.Regional!.Regions[1].Id,"town");long ticks=HexNavigation.Duration(w,w.Characters.Single(a=>a.Id=="player"),target);
            var e=new SimulationEngine(w);e.AdvanceTo(1020);var result=e.ExecutePlayerActivity(Travel("cross-region-precise",target));Check(result.Accepted,result.ToString());
            var final=e.Snapshot();Check(e.WorldTimeSeconds==1020+ticks*DiscreteTime.SecondsPerStep(w)&&final.Characters.Single(a=>a.Id=="player").RegionId==w.HexMap.Regional.Regions[1].Id,"Route timing or region incorrect");
            Check(final.Time!.Charges.Single(c=>c.ActionId=="cross-region-precise").Cost==ticks,"Travel charged more than once");
        }),
        ("Scheduled seconds gathering reserves once and completes exactly from off-grid start",()=>{
            var w=LifeEventScenario.Create();var region=w.HexMap!.Regional!.Regions[1];int n=w.Characters.FindIndex(a=>a.Id=="player");w.Characters[n]=w.Characters[n] with{RegionId=region.Id,HexPosition=region.Resource,DemoLocationId="grove"};
            long stock=Inventory.Balance(w,"player","herb");var e=new SimulationEngine(w);e.AdvanceTo(1020);var i=new ActionIntent("precise-gather","player","grove","herb",1,0,DecisionSource.Player,ActionKind.Gather);
            Check(ActionValidator.Validate(e.Snapshot(),i) is null,"Gather rejected: "+ActionValidator.Validate(e.Snapshot(),i));e.Submit(i);e.AdvanceTo(1020);long due=e.Snapshot().Scheduled.Single().DueAtSeconds!.Value;
            e.AdvanceTo(due-1);Check(Inventory.Balance(e.Snapshot(),"player","herb")==stock,"Early gather");e.AdvanceTo(due);Check(Inventory.Balance(e.Snapshot(),"player","herb")==stock+1,"Gather output missing");e.AdvanceTo(due+1200);Check(Inventory.Balance(e.Snapshot(),"player","herb")==stock+1,"Repeated gather");
        }),
        ("Scheduled seconds deferred transfer waits its full duration and keeps shared validation",()=>{
            var e=new SimulationEngine(LifeEventScenario.Create());e.AdvanceTo(1020);long stock=Inventory.Balance(e.Snapshot(),"npc:03","iron-material");
            e.Submit(new("precise-transfer","player","npc:03","iron-material",1,0,DecisionSource.Player));e.AdvanceTo(1619);Check(Inventory.Balance(e.Snapshot(),"npc:03","iron-material")==stock,"Early transfer");e.AdvanceTo(1620);Check(Inventory.Balance(e.Snapshot(),"npc:03","iron-material")==stock+1,"Deferred transfer rejected");
        }),
        ("Scheduled and deferred equal-time work follows accepted global sequence",()=>{
            foreach(bool scheduledFirst in new[]{true,false}){
                var w=LifeEventScenario.Create();var e=new SimulationEngine(w);e.AdvanceTo(1020);var trip=Travel("mixed-trip",Neighbor(w,"npc:03").ToString(),"npc:03");var transfer=new ActionIntent("mixed-transfer","player","npc:04","iron-material",1,0,DecisionSource.Player);
                if(scheduledFirst){e.Submit(trip);e.Submit(transfer);}else{e.Submit(transfer);e.Submit(trip);}e.AdvanceTo(1620);
                var rows=e.Snapshot().Events.Where(r=>r.Kind is "ScheduledWorkResolved" or "DeferredWorkResolved").Select(r=>r.ActionId).ToArray();
                Check(rows.SequenceEqual(scheduledFirst?new[]{trip.Id,transfer.Id}:new[]{transfer.Id,trip.Id}),"Same-time order changed");
            }
        }),
        ("Scheduled construction consumes reserved funds at exact event without invalid building timestamp",()=>{
            var w=LifeEventScenario.Create();var def=w.HexMap!.BuildingDefinitions.First(d=>d.Kind==OrganizationKind.Sect);var tile=w.HexMap.Tiles.First(t=>t.LocalOrganizationId=="sect"&&HexNavigation.Passable(t)&&!w.HexMap.Buildings.Any(b=>b.Coord==t.Coord));
            var e=new SimulationEngine(w);e.AdvanceTo(1020);var i=new ActionIntent("precise-build","player",tile.Coord.ToString(),"spirit-stone",def.Cost,0,DecisionSource.Player,ActionKind.ConstructBuilding,MissionId:def.Id,OrganizationId:"sect");
            Check(ActionValidator.Validate(e.Snapshot(),i) is null,"Build invalid: "+ActionValidator.Validate(e.Snapshot(),i));e.Submit(i);e.AdvanceTo(1020);long due=e.Snapshot().Scheduled.Single().DueAtSeconds!.Value;e.AdvanceTo(due-1);
            Check(e.Snapshot().HexMap!.Buildings.Single(b=>b.Id==i.Id).Status==BuildingStatus.Constructing,"Early build");e.AdvanceTo(due);Check(e.Snapshot().HexMap!.Buildings.Single(b=>b.Id==i.Id).Status==BuildingStatus.Complete,"Missing build");
        }),
        ("Scheduled cross-region closure interrupts at current tile without refunding paid travel AP",()=>{
            var w=LifeEventScenario.Create();var route=w.HexMap!.Regional!.Routes[0];string target=RegionGeometry.PlaceId(w.HexMap,w.HexMap.Regional.Regions[1].Id,"town");long cost=HexNavigation.Duration(w,w.Characters.Single(a=>a.Id=="player"),target);
            var e=new SimulationEngine(w);e.AdvanceTo(1020);e.Submit(Travel("blocked-precise",target));e.AdvanceTo(1020);
            for(int n=0;n<w.HexMap.Tiles.Count;n++){
                var s=e.Snapshot();if(s.HexMap!.Journeys.Single().Remaining[0]==route.To)break;e.AdvanceTo(s.Scheduled.Single().DueAtSeconds!.Value);
            }
            w=e.Snapshot();Check(w.HexMap!.Journeys.Single().Remaining[0]==route.To,"Did not reach route boundary");long due=w.Scheduled.Single().DueAtSeconds!.Value;
            w.HexMap.Regional!.Routes[0]=route with{Open=false};e=new(w);e.AdvanceTo(due);var final=e.Snapshot();
            Check(final.Characters.Single(a=>a.Id=="player").HexPosition==route.From&&final.Scheduled.Count==0&&final.Time!.ActionRuntime!.Actions["blocked-precise"].Phase==ActionPhase.Interrupted,"Travel did not interrupt");
            Check(final.Time!.Budgets.Single(b=>b.ActorId=="player").Remaining==final.Time.Rules.DailyActionPoints-cost,"Travel AP was refunded or charged twice");
        }),
        ("Scheduled construction cancel invalidates pending completion and refunds only once",()=>{
            var w=LifeEventScenario.Create();var def=w.HexMap!.BuildingDefinitions.First(d=>d.Kind==OrganizationKind.Sect);int definition=w.HexMap.BuildingDefinitions.IndexOf(def);w.HexMap.BuildingDefinitions[definition]=def with{Ticks=10};
            // Preserve a long existing timed cost solely in this cancellation fixture.
            w.Time!.Costs[ActionKind.ConstructBuilding]=10;
            var tile=w.HexMap.Tiles.First(t=>t.LocalOrganizationId=="sect"&&HexNavigation.Passable(t)&&!w.HexMap.Buildings.Any(b=>b.Coord==t.Coord));long money=Inventory.Balance(w,"sect","spirit-stone");
            var e=new SimulationEngine(w);e.AdvanceTo(1020);e.Submit(new("cancelled-precise-build","player",tile.Coord.ToString(),"spirit-stone",def.Cost,0,DecisionSource.Player,ActionKind.ConstructBuilding,MissionId:def.Id,OrganizationId:"sect"));e.AdvanceTo(1020);w=e.Snapshot();
            int n=w.Time!.Budgets.FindIndex(b=>b.ActorId=="player");w.Time.Budgets[n]=w.Time.Budgets[n] with{BusyThroughTick=w.Clock.Tick};e=new(w);
            e.Submit(new("cancel-precise-build","player","cancelled-precise-build","spirit-stone",1,0,DecisionSource.Player,ActionKind.CancelBuilding,OrganizationId:"sect"));e.AdvanceTo(7620);
            var final=e.Snapshot();Check(final.HexMap!.Buildings.Single(b=>b.Id=="cancelled-precise-build").Status==BuildingStatus.Cancelled&&final.Ledger.Count(r=>r.ActionId=="cancelled-precise-build"&&r.Reason=="BuildingRefund"&&r.Quantity==def.Cost)==1,"Cancel refund or state incorrect: "+string.Join(";",final.Events.Where(r=>r.Kind=="Rejected"||r.Kind=="TimedCompletionRejected").Select(r=>r.Detail)));
            Check(!final.Events.Any(r=>r.ActionId=="cancelled-precise-build"&&r.Kind=="BuildingCompleted"),"Cancelled event completed");
        }),
        ("Latest scheduled travel snapshot resumes the same remaining path and seconds",()=>{
            var w=LifeEventScenario.Create();var region=w.HexMap!.Regional!.Regions[1];var e=new SimulationEngine(w);e.AdvanceTo(1020);e.Submit(Travel("travel-snapshot",RegionGeometry.PlaceId(w.HexMap,region.Id,"town")));e.AdvanceTo(1700);
            var snapshot=e.Snapshot();var replay=new SimulationEngine(WorldCodec.Deserialize(WorldCodec.Serialize(snapshot)));e.AdvanceTo(24000);replay.AdvanceTo(24000);
            Check(WorldCodec.Digest(e.Snapshot())==WorldCodec.Digest(replay.Snapshot()),"Latest snapshot continuation changed");
        })
    };
}
