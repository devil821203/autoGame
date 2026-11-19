using Cultivation.Domain;
using Cultivation.Simulation;

internal static class OrganizationLaborTests
{
    private static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    private static WorldState Fixture()
    {
        var w=SettlementOrganizationScenario.Create();w.LaborAutonomy=null;w.OrganizationDevelopment=null;
        foreach(var(item,amount)in new[]{("wood",1000L),("food",1000L),("spirit-stone",50000L)})
            w.Ledger.Add(new(w.NextSequence++,0,"labor-fixture:"+item,"$genesis","sect",item,amount,"Genesis"));
        return w;
    }
    private static ActionIntent Intent(WorldState w,ActionKind kind,string resource="recruit",long quantity=1,string? org="sect",HexCoord? pos=null)=>
        new("labor-test:"+w.NextSequence,w.ControlledCharacterId,w.Settlements.Single().Id,resource,quantity,0,DecisionSource.Player,kind,
            OrganizationId:org,SettlementBuildPosition:pos);
    private static WorldState Act(SimulationEngine e,ActionKind kind,string resource="recruit",long quantity=1,string? org="sect",HexCoord? pos=null)
    {
        var i=Intent(e.Snapshot(),kind,resource,quantity,org,pos);var error=ActionValidator.Validate(e.Snapshot(),i);
        Check(error is null,"Action rejected: "+kind+":"+error);e.Submit(i);e.Step();e.Step();return e.Snapshot();
    }
    private static void Site(SimulationEngine e)=>Act(e,ActionKind.CreateOrganizationSite,"Headquarters");
    private static string Build(SimulationEngine e,SettlementBuildingKind kind)
    {
        var w=e.Snapshot();var rule=w.SettlementBuildingRules!.Single(r=>r.Kind==kind);
        var tile=w.HexMap!.Tiles.First(t=>w.Settlements.Single().Territory.Contains(t.Coord)&&SettlementPlacement.Allows(rule,t));
        return Act(e,ActionKind.BuildSettlement,kind.ToString(),pos:tile.Coord).Settlements.Single().Buildings.Last().Id;
    }
    private static void AdvanceTo(SimulationEngine e,long tick){while(e.Snapshot().Clock.Tick<tick)e.Step();}
    public static IEnumerable<(string Name,Action Body)> Cases=>new (string,Action)[]{
        ("Population investment consumes one batch, awards influence and deterministic growth with save continuation",()=>{
            var w=Fixture();w.PopulationInvestmentRules=w.PopulationInvestmentRules! with{SuccessProbability=1};
            var e=new SimulationEngine(w);long food=Inventory.Balance(w,"sect","food");int before=w.Settlements.Single().Population;
            w=Act(e,ActionKind.InvestSettlementPopulation,"population");
            Check(w.Settlements.Single().Population==before+1&&w.Settlements.Single().WorkingPopulation==before+1,"Investment did not add resident and worker");
            Check(Inventory.Balance(w,"sect","food")==food-10&&w.Settlements.Single().Influence["sect"]==20,"Cost or influence differs");
            var resumed=new SimulationEngine(WorldCodec.Deserialize(WorldCodec.Serialize(w)));
            for(int t=0;t<32;t++){e.Step();resumed.Step();}
            Check(WorldCodec.Digest(e.Snapshot())==WorldCodec.Digest(resumed.Snapshot()),"Investment/day growth save divergence");
        }),
        ("Failed population investment pays materials and influence but full housing rejects without spending",()=>{
            var w=Fixture();w.PopulationInvestmentRules=w.PopulationInvestmentRules! with{SuccessProbability=0};
            var e=new SimulationEngine(w);int before=w.Settlements.Single().Population;
            w=Act(e,ActionKind.InvestSettlementPopulation,"population");Check(w.Settlements.Single().Population==before&&w.PopulationInvestments.Single().AddedPopulation==0,"Failed roll increased population");
            w=Fixture();w.Settlements.Single().Population=w.Settlements.Single().PopulationCapacity;w.Settlements.Single().WorkingPopulation=w.Settlements.Single().Population;
            Check(ActionValidator.Validate(w,Intent(w,ActionKind.InvestSettlementPopulation,"population")) is not null,"Housing limit bypassed");
        }),
        ("Dormitory upgrades enforce abstract labor capacity and recruitment consumes finite organization money",()=>{
            var e=new SimulationEngine(Fixture());Site(e);var dorm=Build(e,SettlementBuildingKind.Dormitory);var w=e.Snapshot();
            Check(OrganizationCapacity.LaborLimit(w,"sect")==20,"Initial dorm capacity incorrect");
            w=Act(e,ActionKind.UpgradeSettlementBuilding,dorm,org:null);Check(OrganizationCapacity.LaborLimit(w,"sect")==30,"Dorm upgrade capacity incorrect");
            int actors=w.Characters.Count,population=w.Settlements.Single().Population;long cash=Inventory.Balance(w,"sect","spirit-stone");
            w=Act(e,ActionKind.RecruitOrganizationLabor);Check(OrganizationCapacity.LaborCount(w,"sect")==1&&w.Characters.Count==actors&&w.Settlements.Single().Population==population,"Labor incorrectly created NPC or resident");
            Check(Inventory.Balance(w,"sect","spirit-stone")==cash-w.OrganizationLaborRules!.RecruitmentCost,"Labor recruitment cost missing");
        }),
        ("Labor replaces residents without duplicate wages and idle labor still receives daily salary",()=>{
            var e=new SimulationEngine(Fixture());Site(e);Build(e,SettlementBuildingKind.Dormitory);string farm=Build(e,SettlementBuildingKind.Farm);
            Act(e,ActionKind.RecruitOrganizationLabor);Act(e,ActionKind.RecruitOrganizationLabor);
            Act(e,ActionKind.SetSettlementEmployees,farm,1,null);Act(e,ActionKind.SetOrganizationLabor,farm,1);
            AdvanceTo(e,30);var w=e.Snapshot();var day=w.SettlementDays.Single(d=>d.BuildingId==farm);
            Check(day.Employees==2&&day.Laborers==1&&day.Wages==w.SettlementRules!.DailyWage&&day.GrossQuantity==2*w.SettlementRules.DailyOutput(SettlementBuildingKind.Farm),"Output or resident wage double counted");
            Check(w.LaborPayroll.Single().Required==10&&w.LaborPayroll.Single().Paid,"Idle labor salary not paid");
            var resumed=new SimulationEngine(WorldCodec.Deserialize(WorldCodec.Serialize(w)));e.Step();resumed.Step();Check(WorldCodec.Digest(e.Snapshot())==WorldCodec.Digest(resumed.Snapshot()),"Labor save continuation mismatch");
        }),
        ("Recruiting a fixed ordinary member transfers fee and supplies an audited random starting stock",()=>{
            var e=new SimulationEngine(Fixture());Site(e);Build(e,SettlementBuildingKind.SeniorDormitory);var w=e.Snapshot();
            Check(OrganizationCapacity.MemberLimit(w,"sect")==15,"Senior dorm member capacity wrong");
            w=Act(e,ActionKind.RecruitOrganizationMember);var receipt=w.MemberRecruitments.Single();var member=w.Characters.Single(c=>c.Id==receipt.MemberId);
            Check(member.Position==OrganizationPosition.Member&&member.TestProfession==TestProfession.None&&member.BaseAttack==w.MemberRecruitmentRules!.Template.BaseAttack&&member.Health==w.MemberRecruitmentRules.Template.Health,"Fixed ordinary template not used");
            Check(Inventory.Balance(w,member.Id,"spirit-stone")==1000+receipt.Stock["spirit-stone"]&&receipt.Stock["spirit-stone"] is >=1 and <=100,"Recruitment fee or money roll missing");
            Check(receipt.Stock["herb"] is >=0 and <=5&&receipt.Stock["ointment"] is >=0 and <=2&&receipt.Stock["beast-hide"] is >=0 and <=3,"Starting supplies out of range");
            Check(ActionValidator.Validate(w,Intent(w,ActionKind.RecruitOrganizationLabor)) is not null,"Labor recruited without dormitory");
        }),
        ("Labor and member capacity reject over-recruitment and unpaid labor cannot produce",()=>{
            var e=new SimulationEngine(Fixture());Site(e);Build(e,SettlementBuildingKind.Dormitory);string farm=Build(e,SettlementBuildingKind.Farm);
            for(int n=0;n<20;n++)Act(e,ActionKind.RecruitOrganizationLabor);
            var w=e.Snapshot();Check(ActionValidator.Validate(w,Intent(w,ActionKind.RecruitOrganizationLabor)) is not null,"Labor capacity bypassed");
            Check(ActionValidator.Validate(w,Intent(w,ActionKind.RecruitOrganizationMember)) is not null,"Existing over-capacity roster admitted new members");
            Act(e,ActionKind.SetOrganizationLabor,farm,1);w=e.Snapshot();
            long cash=Inventory.Balance(w,"sect","spirit-stone");
            w.Ledger.Add(new(w.NextSequence++,w.Clock.Tick,"labor-test-empty-treasury","sect","consumed","spirit-stone",cash,"TestFixtureDrain"));
            e=new(w);long target=(w.Clock.Tick/w.Time!.Rules.TicksPerDay+1)*w.Time.Rules.TicksPerDay;AdvanceTo(e,target);w=e.Snapshot();
            Check(!w.LaborPayroll.Last().Paid&&!w.SettlementDays.Any(d=>d.BuildingId==farm&&d.Tick==target),"Unpaid labor produced resources");
            Check(OrganizationCapacity.LaborCount(w,"sect")==20,"Unpaid labor silently dismissed");
        }),
        ("Recruitment issuance cannot be forged and legacy human resource rules remain disabled",()=>{
            var w=Fixture();w.OrganizationLaborRules=null;w.MemberRecruitmentRules=null;
            Check(ActionValidator.Validate(w,Intent(w,ActionKind.RecruitOrganizationLabor)) is not null,"Legacy snapshot gained labor actions");
            w=Fixture();w.Ledger.Add(new(w.NextSequence++,0,"forged-recruitment","$recruitment",w.ControlledCharacterId,"spirit-stone",1,"RecruitInitialStock"));
            try{_ = new SimulationEngine(w);}catch(InvalidDataException){return;}
            throw new Exception("Forged recruitment stock accepted");
        }),
        ("Autonomous resident organization builds dormitories recruits and assigns labor within configured targets",()=>{
            var e=new SimulationEngine(SettlementOrganizationScenario.Create());for(int day=1;day<=45;day++)e.StepValidatedBatch(day*30,30,validateEveryTick:false);
            var w=e.Snapshot();string org=w.SettlementOrganizationDemo!.Organization.Id;var auto=w.LaborAutonomy!;
            Check(OrganizationCapacity.LaborCount(w,org)==auto.TargetLaborers&&OrganizationCapacity.Assigned(w,org)==auto.TargetLaborers,"Autonomous labor target not achieved");
            Check(w.Characters.Count(c=>c.OrganizationId==org)==auto.TargetMembers,"Autonomous member target not achieved");
            Check(w.MemberRecruitments.All(r=>w.Events.Any(ev=>ev.ActionId.Contains(r.MemberId,StringComparison.Ordinal)||w.Plans.Any(p=>p.ActorId==r.MemberId))||w.Memories.Any(m=>m.ActorId==r.MemberId)),"New member did not enter autonomous scheduling");
        })
    };
}
