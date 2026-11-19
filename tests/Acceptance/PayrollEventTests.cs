using Cultivation.Domain;
using Cultivation.Simulation;
using Cultivation.Persistence;

internal static class PayrollEventTests
{
    private static void Check(bool ok,string why){if(!ok)throw new Exception(why);}
    private static WorldState World()
    {
        var w=LifeEventScenario.Create();w.SectDuty=null;w.TestProcurement=null;
        w.SectPayroll=w.SectPayroll! with{IntervalTicks=3};
        WorldPeriodFixture.Reset(w);
        var q=new EventScheduler(w.Time!.DiscreteEvents!);q.Cancel("$sect-payroll");
        w.Time.SectPayrollDueSeconds=1800;q.Schedule(1800,SimulationEventKind.SectPayroll,"$sect-payroll");return w;
    }
    public static IEnumerable<(string Name,Action Body)> Cases=>new (string,Action)[]{
        ("Payroll seconds preserve configured interval conversion",()=>{
            var w=LifeEventScenario.Create();Check(w.Time!.SectPayrollDueSeconds==w.SectPayroll!.IntervalTicks*DiscreteTime.SecondsPerStep(w),"Wrong configured payroll interval");
        }),
        ("Payroll seconds pay eligible members once per due period and exclude leader",()=>{
            var w=World();var r=w.SectPayroll!;string leader=w.Organizations.Single(o=>o.Id==r.OrganizationId).LeaderId!;
            var ids=w.Characters.Where(c=>c.OrganizationId==r.OrganizationId&&c.Id!=leader&&c.Position is OrganizationPosition.Member or OrganizationPosition.Elder).Select(c=>c.Id).ToArray();
            Check(ids.Length>0,"Empty payroll test");var e=new SimulationEngine(w);e.AdvanceTo(1799);Check(!e.Snapshot().Ledger.Any(x=>x.Reason=="SectSalary"),"Early salary");
            e.AdvanceTo(1800);var after=e.Snapshot();Check(after.Ledger.Count(x=>x.Reason=="SectSalary")==ids.Length,"Missing or duplicate payment");
            foreach(string id in ids)Check(Inventory.Balance(after,id,"spirit-stone")==Inventory.Balance(w,id,"spirit-stone")+r.StonesPerMember,"Wrong salary");
            Check(!after.Ledger.Any(x=>x.Reason=="SectSalary"&&x.To==leader),"Leader paid");
            Check(Inventory.Balance(after,r.OrganizationId,"spirit-stone")==Inventory.Balance(w,r.OrganizationId,"spirit-stone")-ids.Length*r.StonesPerMember,"Funds not conserved");
            e.AdvanceTo(3600);Check(e.Snapshot().Events.Count(x=>x.Kind=="SectPayrollResolved")==2,"Period lost or duplicated");
        }),
        ("Payroll seconds insufficient funds skip whole batch but schedule next period",()=>{
            var w=World();w.SectPayroll=w.SectPayroll! with{StonesPerMember=Inventory.Balance(w,w.SectPayroll!.OrganizationId,"spirit-stone")+1};
            var e=new SimulationEngine(w);e.AdvanceTo(3600);w=e.Snapshot();
            Check(!w.Ledger.Any(x=>x.Reason=="SectSalary")&&w.Events.Count(x=>x.Kind=="PayrollSkipped")==2&&w.Time!.SectPayrollDueSeconds==5400,"Partial payment or missing retry");
        }),
        ("Payroll seconds same-time duty expulsion precedes salary",()=>{
            var w=World();w.SectDuty=LifeEventScenario.Create().SectDuty! with{ReviewIntervalTicks=3,MissLimit=1};
            string org=w.SectPayroll!.OrganizationId;string leader=w.Organizations.Single(o=>o.Id==org).LeaderId!;
            var members=w.Characters.Where(c=>c.OrganizationId==org&&c.Id!=leader&&c.Position is OrganizationPosition.Member or OrganizationPosition.Elder).ToArray();
            w.MemberDuties.Clear();foreach(var a in members)w.MemberDuties.Add(new(org,a.Id,0,0));
            WorldPeriodFixture.Reset(w);
            var e=new SimulationEngine(w);e.AdvanceTo(1800);w=e.Snapshot();
            Check(w.Events.Count(x=>x.Kind=="DutyExpelled")==members.Length&&!w.Ledger.Any(x=>x.Reason=="SectSalary"),"Expelled members received salary");
            Check(w.Events.Count(x=>x.Kind=="SectPayrollResolved")==1,"Payroll event missing");
        }),
        ("Payroll seconds boundary interruption and latest stores do not repay salaries",()=>{
            var e=new SimulationEngine(World());e.ScheduleWorldSignal(1800,"salary-boundary","stop",true);Check(e.AdvanceTo(5400).Reason==AdvanceStopReason.ImportantEvent,"No boundary stop");
            var w=e.Snapshot();Check(!w.Ledger.Any(x=>x.Reason=="SectSalary"),"Payroll overtook prerequisite bridge");
            string dir=Path.Combine(Path.GetTempPath(),"payroll-events-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
            try{foreach(IWorldStore store in new IWorldStore[]{new JsonWorldStore(),new SqliteWorldStore()}){
                string p=Path.Combine(dir,store.GetType().Name);store.Save(p,w);var a=new SimulationEngine(w);var b=new SimulationEngine(store.Load(p));a.AdvanceTo(3600);b.AdvanceTo(3600);
                Check(WorldCodec.Digest(a.Snapshot())==WorldCodec.Digest(b.Snapshot())&&b.Snapshot().Events.Count(x=>x.Kind=="SectPayrollResolved")==2,"Payroll replay changed");
            }}finally{foreach(string p in Directory.GetFiles(dir))File.Delete(p);Directory.Delete(dir);}
        }),
        ("Payroll seconds reject missing or duplicate live event",()=>{
            foreach(bool duplicate in new[]{false,true}){
                var w=World();if(duplicate)new EventScheduler(w.Time!.DiscreteEvents!).Schedule(1800,SimulationEventKind.SectPayroll,"$sect-payroll");
                else w.Time!.DiscreteEvents!.Pending.RemoveAll(x=>x.Kind==SimulationEventKind.SectPayroll);
                bool failed=false;try{WorldIntegrity.Validate(w);}catch(InvalidDataException){failed=true;}Check(failed,"Invalid payroll queue accepted");
            }
        })
    };
}
