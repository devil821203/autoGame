using Cultivation.AI;
using Cultivation.Domain;
using Cultivation.Persistence;
using Cultivation.Simulation;

internal static class R301NotificationTests
{
    private static void Check(bool value,string message){if(!value)throw new Exception(message);}
    private static HashSet<string> Ready(WorldState w)=>w.Time!.EventDecisions!.Ready.Keys.ToHashSet(StringComparer.Ordinal);
    private static SimulationEngine Quiet(SimulationEngine e){var w=e.Snapshot();w.Time!.EventDecisions!.Ready.Clear();return new(w);}
    private static ActionIntent Command(WorldState w,string actor,ActionKind kind,string target,string resource="party",string? organization=null,long quantity=1,HexCoord? position=null)=>
        new("r301:"+kind+":"+w.NextSequence,actor,target,resource,quantity,0,actor==w.ControlledCharacterId?DecisionSource.Player:DecisionSource.Npc,kind,OrganizationId:organization,SettlementBuildPosition:position);
    private static WorldState Run(SimulationEngine e,ActionIntent intent)
    {
        e.Submit(intent);e.Step();
        for(int step=0;step<60;step++){
            var w=e.Snapshot();
            if(!w.Pending.Any(x=>x.Intent.Id==intent.Id)&&!w.Scheduled.Any(x=>x.Intent.Id==intent.Id)&&w.Time?.Deferred.Any(x=>x.Intent.Id==intent.Id)!=true){
                Check(!w.Events.Any(x=>x.ActionId==intent.Id&&x.Kind is "Rejected" or "TimedCompletionRejected"),"Rejected "+intent.Kind+": "+string.Join(";",w.Events.Where(x=>x.ActionId==intent.Id).Select(x=>x.Detail)));
                WorldIntegrity.Validate(w);return w;
            }
            e.Step();
        }
        throw new Exception("Action did not finish: "+intent.Kind);
    }
    private static WorldState PartyWorld(bool independent=true)
    {
        var w=independent?EventDecisionTests.MissionWorld(false):EventDecisionTests.Fresh(true);
        if(!independent){w.SectDuty=null;w.MemberDuties.Clear();w.SectPayroll=null;w.TestProcurement=null;w.HuntRules=null;}
        w.PartyRules=w.PartyRules! with {InvitationTicks=4};
        foreach(string id in new[]{"player","npc:00","npc:01"}){
            int n=w.Characters.FindIndex(a=>a.Id==id);var a=w.Characters[n] with {OrganizationId="sect",Position=OrganizationPosition.Member,Health=100,DemoLocationId="sect"};
            w.Characters[n]=a with {HexPosition=RegionGeometry.Resolve(w.HexMap!,a,"sect")};
        }
        if(w.Time!.Content is null)ContentProfile.EnableFresh(w);
        w.Time.EventDecisions!.Ready.Clear();return w;
    }
    private static string Invite(SimulationEngine e,string target)
    {
        var i=Command(e.Snapshot(),"player",ActionKind.InviteParty,target);Run(e,i);return i.Id;
    }
    private static SimulationEngine Team()
    {
        var e=new SimulationEngine(PartyWorld());
        foreach(string member in new[]{"npc:00","npc:01"}){string id=Invite(e,member);Run(e,Command(e.Snapshot(),member,ActionKind.AcceptParty,id));}
        return Quiet(e);
    }
    private static void Stores(WorldState w,long until)
    {
        string dir=Path.Combine(Path.GetTempPath(),"r301-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
        try{foreach(IWorldStore store in new IWorldStore[]{new JsonWorldStore(),new SqliteWorldStore()}){
            string file=Path.Combine(dir,store.GetType().Name);store.Save(file,w);var a=new SimulationEngine(w);var b=new SimulationEngine(store.Load(file));a.AdvanceTo(until);b.AdvanceTo(until);
            Check(WorldCodec.Digest(a.Snapshot())==WorldCodec.Digest(b.Snapshot()),"Store continuation differs: "+store.GetType().Name);
        }}finally{foreach(string f in Directory.GetFiles(dir))File.Delete(f);Directory.Delete(dir);}
    }
    private static WorldState OrgWorld()
    {
        var w=SettlementOrganizationScenario.Create();w.OrganizationDevelopment=null;w.LaborAutonomy=null;w.SectDuty=null;w.MemberDuties.Clear();w.SectPayroll=null;w.TestProcurement=null;w.HuntRules=null;
        var c=GameConfiguration.Find();var driven=c.Read<ActionDrivenRules>("time/action-driven.json");w.Time!.ActionDriven=driven;
        w.Time.Rules=w.Time.Rules with {TicksPerDay=driven.MinutesPerDay!.Value/driven.MinutesPerActionPoint!.Value};
        foreach(var item in new[]{"wood","food","spirit-stone"})w.Ledger.Add(new(w.NextSequence++,0,"r301-stock:"+item,"$genesis","sect",item,50000,"Genesis"));
        DiscreteTime.EnableFresh(w);ActionLifecycle.EnableFresh(w);EventDecisions.EnableFresh(w);ContentProfile.EnableFresh(w);w.Time.EventDecisions!.Ready.Clear();return w;
    }
    private static void Site(SimulationEngine e)=>Run(e,Command(e.Snapshot(),"player",ActionKind.CreateOrganizationSite,e.Snapshot().Settlements.Single().Id,"Headquarters","sect"));
    private static string Build(SimulationEngine e,SettlementBuildingKind kind)
    {
        var w=e.Snapshot();var s=w.Settlements.Single();var rule=w.SettlementBuildingRules!.Single(r=>r.Kind==kind);
        var tile=w.HexMap!.Tiles.First(t=>s.Territory.Contains(t.Coord)&&SettlementPlacement.Allows(rule,t));
        w=Run(e,Command(w,"player",ActionKind.BuildSettlement,s.Id,kind.ToString(),"sect",position:tile.Coord));return w.Settlements.Single().Buildings.Last().Id;
    }
    private static void NextDay(SimulationEngine e)
    {
        var w=e.Snapshot();long target=(w.Clock.Tick/w.Time!.Rules.TicksPerDay+1)*w.Time.Rules.TicksPerDay*DiscreteTime.SecondsPerStep(w);e.AdvanceTo(target);
    }
    private static WorldState HuntWorld(bool existing)
    {
        var w=PartyWorld();w.PartyRules=w.PartyRules! with {InvitationTicks=20};
        w.HuntRules=GameConfiguration.Find().Read<HuntRules>("organizations/hunts.json") with {IntervalTicks=40,RequiredKills=2};
        if(existing){w.Hunts.Add(new("r301-hunt","sect",MonsterKind.Wolf,2,w.HuntRules.RewardPerKill,1000,0,["npc:00","npc:01"]));w.Hunts.Add(new("r301-bandit","sect",MonsterKind.Bandit,2,w.HuntRules.RewardPerKill,1000,0,[]));}
        var events=w.Time!.DiscreteEvents!;events.PeriodicDeadlines.Add("$hunt-publication",600);new EventScheduler(events).Schedule(600,SimulationEventKind.HuntPublication,"$hunt-publication");return w;
    }
    public static IEnumerable<(string Name,Action Body)> Cases=>new (string,Action)[]{
        ("R301 organization accounts notify only their leader and merge multiple accounts across stores",()=>{
            var w=EventDecisionTests.MissionWorld(false);
            w.Organizations.Add(new("r301-bank-a","A","npc:03"));w.Organizations.Add(new("r301-bank-b","B","npc:03"));var e=new SimulationEngine(w);
            foreach(var (actor,bank) in new[]{("player","r301-bank-a"),("npc:00","r301-bank-b")})e.Submit(Command(w,actor,ActionKind.Transfer,bank,"spirit-stone") with {Id="bank:"+bank});
            e.AdvanceTo(600);w=e.Snapshot();
            Check(Inventory.Balance(w,"r301-bank-a","spirit-stone")==1&&Inventory.Balance(w,"r301-bank-b","spirit-stone")==1,"Bank credit failed");
            var request=w.Time!.EventDecisions!.Ready["npc:03"];Check(request.Reasons.SetEquals([DecisionTrigger.StateChanged]),"Leader reasons or merge wrong");
            Check(Ready(w).SetEquals(["player","npc:00","npc:03"]),"Private bank changes leaked to members/accounts");Stores(w,1200);
        }),
        ("R301 unavailable or absent organization leader creates no invalid recipient",()=>{
            var w=EventDecisionTests.MissionWorld(false);int n=w.Characters.FindIndex(a=>a.Id=="npc:03");w.Characters[n]=w.Characters[n] with {Health=0};
            w.Organizations.Add(new("r301-dead-bank","D","npc:03"));w.Organizations.Add(new("r301-no-leader","N"));var e=new SimulationEngine(w);
            foreach(string bank in new[]{"r301-dead-bank","r301-no-leader"})Run(e,Command(e.Snapshot(),"player",ActionKind.Transfer,bank,"spirit-stone"));
            w=e.Snapshot();Check(Ready(w).SetEquals(["player"]),"Unavailable leader/account was notified");WorldIntegrity.Validate(w);
        }),
        ("R301 population success notifies local managers while failure only notifies donor",()=>{
            foreach(bool success in new[]{false,true}){
                var w=OrgWorld();w.PopulationInvestmentRules=w.PopulationInvestmentRules! with {SuccessProbability=success?1:0};
                string resident=w.SettlementOrganizationDemo!.Organization.LeaderId!;w.Organizations.Add(new("r301-unrelated","U","npc:00"));
                int before=w.Settlements.Single().Population;var e=new SimulationEngine(w);w=Run(e,Command(w,"player",ActionKind.InvestSettlementPopulation,w.Settlements.Single().Id,"population","sect"));
                Check(w.Settlements.Single().Population==before+(success?w.PopulationInvestmentRules!.PopulationPerSuccess:0),"Population result changed");
                Check(Ready(w).Contains(resident)==success&&Ready(w).Contains("player")&&!Ready(w).Contains("npc:00"),"Public workforce/private donation boundary wrong");
            }
        }),
        ("R301 resident assignment notifies shared workforce managers only on actual change",()=>{
            var e=new SimulationEngine(OrgWorld());Site(e);string farm=Build(e,SettlementBuildingKind.Farm);e=Quiet(e);var w=e.Snapshot();string resident=w.SettlementOrganizationDemo!.Organization.LeaderId!;
            w=Run(e,Command(w,"player",ActionKind.SetSettlementEmployees,w.Settlements.Single().Id,farm,quantity:1));Check(Ready(w).Contains(resident),"Resident allocation missed related manager");
            e=Quiet(e);w=Run(e,Command(e.Snapshot(),"player",ActionKind.SetSettlementEmployees,w.Settlements.Single().Id,farm,quantity:1));Check(!Ready(w).Contains(resident),"Unchanged allocation woke manager");
        }),
        ("R301 new housing capacity notifies stakeholders without broadcasting private construction assets",()=>{
            var e=new SimulationEngine(OrgWorld());Site(e);e=Quiet(e);var w=e.Snapshot();string resident=w.SettlementOrganizationDemo!.Organization.LeaderId!;int before=w.Settlements.Single().PopulationCapacity;
            Build(e,SettlementBuildingKind.Housing);w=e.Snapshot();
            Check(w.Settlements.Single().PopulationCapacity==before+w.SettlementRules!.HousingCapacity&&Ready(w).Contains(resident),"Housing capacity missed stakeholder");
            Check(!Ready(w).Contains("npc:00")&&!Ready(w).Contains("npc:01"),"Housing broadcast to ordinary members");WorldIntegrity.Validate(w);
        }),
        ("R301 private building upgrade wakes owner without waking other settlement managers",()=>{
            var e=new SimulationEngine(OrgWorld());Site(e);string farm=Build(e,SettlementBuildingKind.Farm);e=Quiet(e);var w=e.Snapshot();string resident=w.SettlementOrganizationDemo!.Organization.LeaderId!;
            int level=w.Settlements.Single().Buildings.Single(b=>b.Id==farm).Level!.Value;
            w=Run(e,Command(w,"player",ActionKind.UpgradeSettlementBuilding,w.Settlements.Single().Id,farm));
            Check(w.Settlements.Single().Buildings.Single(b=>b.Id==farm).Level==level+1&&Ready(w).Contains("player")&&!Ready(w).Contains(resident),"Private upgrade leaked or missed owner");
        }),
        ("R301 production wages tax and daily replay preserve authoritative receipts",()=>{
            var e=new SimulationEngine(OrgWorld());Site(e);string farm=Build(e,SettlementBuildingKind.Farm);var w=e.Snapshot();Run(e,Command(w,"player",ActionKind.SetSettlementEmployees,w.Settlements.Single().Id,farm,quantity:1));e=Quiet(e);
            NextDay(e);w=e.Snapshot();var day=w.SettlementDays.Last(d=>d.BuildingId==farm);
            Check(day.GrossQuantity==w.SettlementRules!.DailyOutput(SettlementBuildingKind.Farm)&&day.GrossQuantity==day.Quantity+day.TaxQuantity,"Production changed");
            Check(w.Time!.EventDecisions!.Ready["player"].Reasons.Contains(DecisionTrigger.StateChanged),"Production missed owner");
            Stores(w,w.Time.DiscreteEvents!.WorldTime+600);e=Quiet(e);e.AdvanceTo(w.Time.DiscreteEvents.WorldTime);Check(Ready(e.Snapshot()).Count==0,"Repeated same-day settlement notified again");
        }),
        ("R301 unpaid resident and abstract labor notify owner without paying or producing",()=>{
            foreach(bool labor in new[]{false,true}){
                var e=new SimulationEngine(OrgWorld());Site(e);string farm=Build(e,SettlementBuildingKind.Farm);
                if(labor){Build(e,SettlementBuildingKind.Dormitory);var x=e.Snapshot();Run(e,Command(x,"player",ActionKind.RecruitOrganizationLabor,x.Settlements.Single().Id,"recruit","sect"));x=e.Snapshot();Run(e,Command(x,"player",ActionKind.SetOrganizationLabor,x.Settlements.Single().Id,farm,"sect",1));}
                else {var x=e.Snapshot();Run(e,Command(x,"player",ActionKind.SetSettlementEmployees,x.Settlements.Single().Id,farm,quantity:1));}
                var w=e.Snapshot();long cash=Inventory.Balance(w,"sect","spirit-stone");w.Ledger.Add(new(w.NextSequence++,w.Clock.Tick,"r301-empty-treasury","sect","consumed","spirit-stone",cash,"FixtureTransfer"));w.Time!.EventDecisions!.Ready.Clear();e=new(w);NextDay(e);w=e.Snapshot();
                Check(Ready(w).Contains("player")&&!w.SettlementDays.Any(d=>d.BuildingId==farm),"Unpaid work produced or failed to notify owner");
                if(labor)Check(w.LaborPayroll.Last(p=>p.OrganizationId=="sect").Paid==false,"Unpaid abstract labor got paid");
                else Check(w.Events.Any(v=>v.Kind=="SettlementStopped"&&v.Detail.Contains(farm)),"No wage shortage event");WorldIntegrity.Validate(w);
            }
        }),
        ("R301 incoming organization funds wake existing autonomous development",()=>{
            var w=OrgWorld();var c=GameConfiguration.Find();w.OrganizationDevelopment=c.Read<OrganizationDevelopmentRules>("settlements/organization-development.json");w.LaborAutonomy=c.Read<LaborAutonomyRules>("organizations/labor-autonomy.json");
            string org=w.SettlementOrganizationDemo!.Organization.Id,leader=w.SettlementOrganizationDemo.Organization.LeaderId!;
            long amount=checked(w.OrganizationDevelopment.ReserveMoney+10000);w.Ledger.Add(new(w.NextSequence++,0,"r301-development-funds","$genesis","player","spirit-stone",amount,"Genesis"));var e=new SimulationEngine(w);
            e.Submit(Command(w,"player",ActionKind.Transfer,org,"spirit-stone",quantity:amount));e.AdvanceTo(600,true);w=e.Snapshot();
            Check(w.Time!.EventDecisions!.Traces.TryGetValue(leader,out var trace)&&trace.Reasons.Contains(DecisionTrigger.StateChanged)&&trace.SelectedAction is not null,"Funded leader did not reassess and choose valid work");
            Check(w.Time.Charges.Any(x=>x.ActorId==leader),"Leader did not pay shared AP");WorldIntegrity.Validate(w);
        }),
        ("R301 party accept existing members leave leader and dissolve notify exact participants",()=>{
            var e=new SimulationEngine(PartyWorld());string first=Invite(e,"npc:00");Check(Ready(e.Snapshot()).SetEquals(["player","npc:00"]),"Invite receiver boundary wrong");
            e=Quiet(e);Run(e,Command(e.Snapshot(),"npc:00",ActionKind.AcceptParty,first));Check(Ready(e.Snapshot()).SetEquals(["player","npc:00"]),"Accept missed inviter");
            e=Quiet(e);string next=Invite(e,"npc:01");Check(!Ready(e.Snapshot()).Contains("npc:00"),"Private invitation broadcast to team");e=Quiet(e);Run(e,Command(e.Snapshot(),"npc:01",ActionKind.AcceptParty,next));Check(Ready(e.Snapshot()).SetEquals(["player","npc:00","npc:01"]),"Joining missed old member");
            e=Quiet(e);var w=e.Snapshot();w=Run(e,Command(w,"player",ActionKind.LeaveParty,w.Parties.Single().Id));Check(w.Parties.Single().LeaderId=="npc:00"&&Ready(w).SetEquals(["player","npc:00","npc:01"]),"Leader departure missed team or changed succession");
            e=Quiet(e);w=e.Snapshot();w=Run(e,Command(w,"npc:00",ActionKind.LeaveParty,w.Parties.Single().Id));Check(w.Parties.Count==0&&Ready(w).SetEquals(["npc:00","npc:01"]),"Dissolution notified wrong actors");
        }),
        ("R301 party acceptance and decline close expiry without later notifications",()=>{
            foreach(var kind in new[]{ActionKind.AcceptParty,ActionKind.DeclineParty}){
                var e=new SimulationEngine(PartyWorld());string id=Invite(e,"npc:00");long due=e.Snapshot().PartyInvitations.Single(v=>v.Id==id).ExpiresAtSeconds!.Value;e=Quiet(e);
                Run(e,Command(e.Snapshot(),"npc:00",kind,id));Check(Ready(e.Snapshot()).SetEquals(["player","npc:00"]),"Response missed inviter");e=Quiet(e);e.AdvanceTo(due+1);
                Check(!Ready(e.Snapshot()).Contains("npc:00")&&e.Snapshot().PartyInvitations.Single(v=>v.Id==id).Status==(kind==ActionKind.AcceptParty?PartyInviteStatus.Accepted:PartyInviteStatus.Declined),"Closed invite expired or notified again");
            }
        }),
        ("R301 party expiry in both modes preserves deadlines stores and single notification",()=>{
            foreach(bool independent in new[]{false,true}){
                var e=new SimulationEngine(PartyWorld(independent));string id=Invite(e,"npc:00");var w=e.Snapshot();var invite=w.PartyInvitations.Single(v=>v.Id==id);long due=invite.ExpiresAtSeconds??invite.ExpiresTick*DiscreteTime.SecondsPerStep(w);e=Quiet(e);e.AdvanceTo(due-1);
                Check(!Ready(e.Snapshot()).Contains("npc:00"),"Early invitation expiry notice");Stores(e.Snapshot(),due);e.AdvanceTo(due);w=e.Snapshot();Check(Ready(w).Contains("player")&&Ready(w).Contains("npc:00")&&w.PartyInvitations.Single(v=>v.Id==id).Status==PartyInviteStatus.Expired,"Expiry missed participants");
                long seq=w.Time!.EventDecisions!.Ready["npc:00"].Sequence;e.AdvanceTo(due+1);Check(e.Snapshot().Time!.EventDecisions!.Ready["npc:00"].Sequence==seq,"Duplicate expiry request");
            }
        }),
        ("R301 governance membership cleanup notifies removed and remaining party members",()=>{
            var e=Team();var w=e.Snapshot();w=Run(e,Command(w,"npc:00",ActionKind.LeaveOrganization,"sect","organization"));
            Check(w.Parties.Single().Members.SequenceEqual(["player","npc:01"])&&Ready(w).IsSupersetOf(["player","npc:00","npc:01"]),"Membership cleanup missed removed or remaining member");
        }),
        ("R301 hunts notify only on actual publication and preserve own-organization visibility",()=>{
            var e=new SimulationEngine(HuntWorld(false));e.AdvanceTo(600);var w=e.Snapshot();Check(w.Events.Count(x=>x.Kind=="HuntPublished")==2&&w.Time!.EventDecisions!.Ready["npc:00"].Reasons.SetEquals([DecisionTrigger.KnownNotification]),"Actual hunt publication missing or not merged");
            e=new(HuntWorld(true));e.AdvanceTo(600);Check(!Ready(e.Snapshot()).Contains("npc:00")&&!e.Snapshot().Events.Any(x=>x.Kind=="HuntPublished"),"Empty publication period woke organization");
        }),
        ("R301 real hunt progress notifies absent participant completion notifies observers without extra reward",()=>{
            var w=HuntWorld(true);w.Dungeon=new(1,1,1,1);int outsider=w.Characters.FindIndex(a=>a.Id=="npc:04");w.Characters[outsider]=w.Characters[outsider] with {OrganizationId=null,Position=OrganizationPosition.None};
            int fighter=w.Characters.FindIndex(a=>a.Id=="npc:00");var actor=w.Characters[fighter] with {DemoLocationId="dungeon",BaseAttack=1000,BaseDefense=1000};w.Characters[fighter]=actor with {HexPosition=RegionGeometry.Resolve(w.HexMap!,actor,"dungeon")};
            var e=new SimulationEngine(w);
            for(int kill=1;kill<=2;kill++){
                w=e.Snapshot();long free=w.Time!.Budgets.Single(b=>b.ActorId=="npc:00").BusyThroughTick*DiscreteTime.SecondsPerStep(w);if(free>w.Time.DiscreteEvents!.WorldTime)e.AdvanceTo(free);e=Quiet(e);w=e.Snapshot();
                w=Run(e,Command(w,"npc:00",ActionKind.ExploreDungeon,"dungeon","combat"));var mission=w.Hunts.Single(h=>h.Id=="r301-hunt");
                Check(mission.Kills==kill&&Ready(w).Contains("npc:01"),"Real hunt progress missed absent participant");Check(Ready(w).Contains("npc:02")== (kill==2)&&!Ready(w).Contains("npc:04"),"Hunt progress leaked or completion missed observer");
                Check(w.HuntReceipts.Where(r=>r.MissionId==mission.Id).Sum(r=>r.Contribution)==kill*mission.RewardPerKill,"Hunt reward changed");WorldIntegrity.Validate(w);
            }
            Stores(w,w.Time!.DiscreteEvents!.WorldTime+600);
        })
    };
}
