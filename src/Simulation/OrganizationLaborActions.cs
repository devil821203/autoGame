using Cultivation.Domain;

namespace Cultivation.Simulation;

public static class OrganizationLaborActions
{
    internal const string RecruitSource="$recruitment";
    public static bool Handles(ActionKind k)=>k is ActionKind.RecruitOrganizationLabor or ActionKind.RecruitOrganizationMember or ActionKind.SetOrganizationLabor;
    internal static void EnableFresh(WorldState w,GameConfiguration config)
    {
        w.OrganizationLaborRules=config.Read<OrganizationLaborRules>("organizations/labor.json");
        var o=config.Read<MemberRecruitmentOptions>("organizations/recruitment.json");
        var template=config.Read<List<CharacterState>>("characters/autonomous.json").SingleOrDefault(c=>c.Id==o.MemberTemplateId)
            ??throw new InvalidDataException("organizations/recruitment.json: unknown MemberTemplateId");
        w.MemberRecruitmentRules=new(o,template);
    }
    public static string? Validate(WorldState w,ActionIntent i)
    {
        if(w.OrganizationLaborRules is not { } r||w.MemberRecruitmentRules is null||i.DurationTicks!=0||i.MissionId is not null||
            i.OrganizationId is null||!w.Organizations.Any(o=>o.Id==i.OrganizationId&&o.LeaderId==i.ActorId))return "Organization labor unavailable or leader permission required";
        var s=w.Settlements.SingleOrDefault(s=>s.Id==i.TargetId);var actor=w.Characters.Single(c=>c.Id==i.ActorId);
        if(s is null||actor.RegionId!="valley"||HexNavigation.Position(w.HexMap!,actor)!=w.HexMap!.Sites.Single(t=>t.Id==s.SiteId).Anchor)return "Must arrive at settlement";
        if(!w.OrganizationSites.Any(t=>t.SettlementId==s.Id&&t.OrganizationId==i.OrganizationId))return "Organization site required";
        if(i.Kind==ActionKind.SetOrganizationLabor)
        {
            var b=s.Buildings.SingleOrDefault(b=>b.Id==i.ResourceId&&b.OwnerOrganizationId==i.OrganizationId&&w.SettlementRules!.Outputs.ContainsKey(b.Kind));
            if(b is null||i.Quantity<0||i.Quantity>int.MaxValue||i.Quantity+b.EmployeeCount>b.EmployeeLimit)return "Invalid organization labor assignment";
            return OrganizationCapacity.Assigned(w,i.OrganizationId)-b.LaborCount+i.Quantity>OrganizationCapacity.LaborCount(w,i.OrganizationId)?"Not enough unassigned organization labor":null;
        }
        if(i.Quantity!=1||i.ResourceId!="recruit")return "Recruit one person per order";
        if(i.Kind==ActionKind.RecruitOrganizationLabor&&OrganizationCapacity.LaborCount(w,i.OrganizationId)>=OrganizationCapacity.LaborLimit(w,i.OrganizationId))return "Labor dormitory capacity reached";
        if(i.Kind==ActionKind.RecruitOrganizationMember&&w.Characters.Count(c=>c.OrganizationId==i.OrganizationId)>=OrganizationCapacity.MemberLimit(w,i.OrganizationId))return "Organization member capacity reached";
        if(i.Kind==ActionKind.RecruitOrganizationMember&&w.OrganizationPolicies.Any(p=>p.OrganizationId==i.OrganizationId&&w.Characters.Count(c=>c.OrganizationId==i.OrganizationId)>=p.MaximumMembers))return "Organization policy member capacity reached";
        long cost=i.Kind==ActionKind.RecruitOrganizationLabor?r.RecruitmentCost:w.MemberRecruitmentRules.Options.MemberCost;
        return Inventory.Balance(w,i.OrganizationId,"spirit-stone")<cost?"Insufficient organization recruitment money":null;
    }
    internal static void Execute(WorldState w,ActionIntent i)
    {
        string org=i.OrganizationId!;
        if(i.Kind==ActionKind.SetOrganizationLabor)
        {
            var s=w.Settlements.Single(s=>s.Id==i.TargetId);int n=s.Buildings.FindIndex(b=>b.Id==i.ResourceId);
            s.Buildings[n]=s.Buildings[n] with{LaborCount=checked((int)i.Quantity)};
        }
        else if(i.Kind==ActionKind.RecruitOrganizationLabor)
        {
            int n=w.OrganizationLabor.FindIndex(l=>l.OrganizationId==org);
            if(n<0)
            {
                string account="labor-payroll:"+org;
                w.Organizations.Add(new(account,w.Organizations.Single(o=>o.Id==org).Name+"雜役薪資帳"));
                w.OrganizationLabor.Add(new(org,account,0));n=w.OrganizationLabor.Count-1;
            }
            var labor=w.OrganizationLabor[n];
            w.Ledger.Add(new(w.NextSequence++,w.Clock.Tick,i.Id,org,"consumed","spirit-stone",w.OrganizationLaborRules!.RecruitmentCost,"LaborRecruitmentFee"));
            w.LaborRecruitments.Add(new(i.Id,org,i.TargetId,w.Clock.Tick,labor.Count));
            w.OrganizationLabor[n]=labor with{Count=checked(labor.Count+1)};
        }
        else
        {
            var rules=w.MemberRecruitmentRules!;var s=w.Settlements.Single(s=>s.Id==i.TargetId);var anchor=w.HexMap!.Sites.Single(t=>t.Id==s.SiteId).Anchor;
            string id="recruited:"+i.Id;var t=rules.Template;
            var character=t with{Id=id,Name=rules.Options.MemberName+" "+(w.MemberRecruitments.Count+1),OrganizationId=org,Position=OrganizationPosition.Member,
                DemoLocationId=anchor.ToString(),HexPosition=anchor,TestProfession=TestProfession.None,
                Stamina=w.StaminaRules is { } stamina?new(stamina.Maximum):null,Mindset=w.MindsetRules?.Initial,
                LifeSkills=w.LifeSkillRules is null?null:new(),MortalCultivation=w.MortalActionRules is { } mortal?
                    new(){BaseAttributes=Enum.GetValues<MortalAttribute>().ToDictionary(a=>a,_=>mortal.InitialAttributeValue)}:null};
            w.Characters.Add(character);
            ulong before=w.RandomState;var stock=InitialStock(rules.Options,before,out ulong after);w.RandomState=after;
            w.Ledger.Add(new(w.NextSequence++,w.Clock.Tick,i.Id,org,id,"spirit-stone",rules.Options.MemberCost,"MemberRecruitmentFee"));
            foreach(var(item,quantity)in stock.Where(p=>p.Value>0))
                w.Ledger.Add(new(w.NextSequence++,w.Clock.Tick,i.Id,RecruitSource,id,item,quantity,"RecruitInitialStock"));
            w.MemberRecruitments.Add(new(i.Id,org,s.Id,id,w.Clock.Tick,before,after,stock));
            TimeActions.Refresh(w);
        }
        ActionExecutor.Emit(w,i.Id,"OrganizationLaborChanged",$"{org}:{i.Kind}:{i.ResourceId}:{i.Quantity}");
    }
    internal static Dictionary<string,long> InitialStock(MemberRecruitmentOptions o,ulong before,out ulong after)
    {
        var random=new SeededRandom(before);
        long Draw(int min,int max)
        {
            ulong width=(ulong)((long)max-min+1),limit=((ulong)uint.MaxValue+1)/width*width;uint roll;
            do{roll=random.NextUInt32();}while(roll>=limit);
            return min+(long)(roll%width);
        }
        var stock=new Dictionary<string,long>{{"spirit-stone",Draw(o.MoneyMinimum,o.MoneyMaximum)}};
        foreach(var(item,range)in o.Supplies.OrderBy(p=>p.Key,StringComparer.Ordinal))stock.Add(item,Draw(range.Minimum,range.Maximum));
        after=random.State;return stock;
    }
    internal static void PayDaily(WorldState w)
    {
        if(w.OrganizationLaborRules is not { } r||w.Time is null||w.Clock.Tick==0||w.Clock.Tick%w.Time.Rules.TicksPerDay!=0)return;
        foreach(var labor in w.OrganizationLabor.OrderBy(l=>l.OrganizationId,StringComparer.Ordinal))
        {
            if(w.LaborPayroll.Any(p=>p.OrganizationId==labor.OrganizationId&&p.Tick==w.Clock.Tick))continue;
            string id=$"labor-payroll:{labor.OrganizationId}:{w.Clock.Tick}";long due=checked(labor.Count*r.DailySalary);
            bool paid=Inventory.Balance(w,labor.OrganizationId,"spirit-stone")>=due;
            if(paid&&due>0)w.Ledger.Add(new(w.NextSequence++,w.Clock.Tick,id,labor.OrganizationId,labor.PayrollAccountId,"spirit-stone",due,"OrganizationLaborWages"));
            w.LaborPayroll.Add(new(id,labor.OrganizationId,w.Clock.Tick,labor.Count,due,paid));
            ActionExecutor.Emit(w,id,paid?"LaborSalaryPaid":"LaborSalaryUnpaid",$"{labor.OrganizationId}:{labor.Count}:{due}");
        }
    }
    internal static bool PaidToday(WorldState w,string organization)=>w.LaborPayroll.Any(p=>p.OrganizationId==organization&&p.Tick==w.Clock.Tick&&p.Paid);
}
