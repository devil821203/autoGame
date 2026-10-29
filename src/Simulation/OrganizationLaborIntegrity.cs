using Cultivation.Domain;

namespace Cultivation.Simulation;

internal static class OrganizationLaborIntegrity
{
    internal static void Validate(WorldState w)
    {
        if(w.OrganizationLaborRules is not { } r)
        {
            if(w.OrganizationLabor.Count+w.LaborRecruitments.Count+w.LaborPayroll.Count+w.MemberRecruitments.Count>0||w.Settlements.SelectMany(s=>s.Buildings).Any(b=>b.LaborCount!=0))
                throw new InvalidDataException("Organization labor data without rules");
            return;
        }
        var member=w.MemberRecruitmentRules;var o=member?.Options;
        if(w.SettlementRules is null||w.Time is null||w.SettlementOperationRules is null||r.RecruitmentCost<=0||r.DailySalary<=0||
            r.DailySalary>long.MaxValue/int.MaxValue||r.DormitoryBaseCapacity<=0||r.DormitoryCapacityPerLevel<=0||r.BaseMemberCapacity<=0||r.SeniorDormitoryCapacityPerLevel<=0||r.ActionPoints is <1 or >30||
            member is null||o is null||string.IsNullOrWhiteSpace(o.MemberName)||member.Template.Id!=o.MemberTemplateId||member.Template.OrganizationId is not null||member.Template.Position!=OrganizationPosition.None||member.Template.Hostile||
            member.Template.EquippedWeaponId is not null||o.MemberCost<=0||o.MoneyMinimum<1||o.MoneyMaximum<o.MoneyMinimum||o.Supplies is null||
            o.Supplies.Any(p=>p.Key is "spirit-stone" or "contribution" or "pill"||!w.Items.Any(i=>i.Id==p.Key)||p.Value.Minimum<0||p.Value.Maximum<p.Value.Minimum))
            throw new InvalidDataException("config/organizations/labor.json or recruitment.json: invalid costs, capacity, salary, AP, fixed member template or random stock ranges");
        if(w.LaborAutonomy is { } auto&&(w.SettlementOrganizationDemo is null||auto.TargetLaborers<0||auto.TargetMembers<1||auto.SalaryReserveDays<1||auto.DormitoryTargetLevel<w.SettlementOperationRules.InitialBuildingLevel))
            throw new InvalidDataException("config/organizations/labor-autonomy.json: invalid target count, salary reserve or dormitory level");
        if(w.OrganizationLabor.Select(l=>l.OrganizationId).Distinct().Count()!=w.OrganizationLabor.Count||
            w.LaborRecruitments.Select(l=>l.Id).Distinct().Count()!=w.LaborRecruitments.Count||w.MemberRecruitments.Select(l=>l.Id).Distinct().Count()!=w.MemberRecruitments.Count||
            w.LaborPayroll.Select(p=>(p.OrganizationId,p.Tick)).Distinct().Count()!=w.LaborPayroll.Count)
            throw new InvalidDataException("Duplicate organization labor state or receipts");
        var entries=w.Ledger.ToLookup(e=>e.ActionId);
        foreach(var labor in w.OrganizationLabor)
        {
            if(!w.Organizations.Any(org=>org.Id==labor.OrganizationId)||labor.Count<0||labor.Count>OrganizationCapacity.LaborLimit(w,labor.OrganizationId)||
                labor.PayrollAccountId!="labor-payroll:"+labor.OrganizationId||!w.Organizations.Any(org=>org.Id==labor.PayrollAccountId))throw new InvalidDataException("Invalid labor capacity or payroll account");
            int count=0;
            foreach(var hire in w.LaborRecruitments.Where(h=>h.OrganizationId==labor.OrganizationId))
            {
                if(hire.CountBefore!=count++||hire.Tick<=0||hire.Tick>w.Clock.Tick||!w.ProcessedIntentIds.Contains(hire.Id)||!w.Settlements.Any(s=>s.Id==hire.SettlementId)||
                    entries[hire.Id].Count()!=1||!entries[hire.Id].Any(e=>e.Tick==hire.Tick&&e.From==labor.OrganizationId&&e.To=="consumed"&&e.ResourceId=="spirit-stone"&&e.Quantity==r.RecruitmentCost&&e.Reason=="LaborRecruitmentFee"))
                    throw new InvalidDataException("Unbacked labor recruitment");
            }
            if(count!=labor.Count||OrganizationCapacity.Assigned(w,labor.OrganizationId)>count)throw new InvalidDataException("Labor headcount or assignment mismatch");
        }
        foreach(var s in w.Settlements)foreach(var b in s.Buildings)
            if(b.LaborCount<0||(long)b.LaborCount+b.EmployeeCount>b.EmployeeLimit||b.LaborCount>0&&
                (!w.SettlementRules.Outputs.ContainsKey(b.Kind)||b.OwnerOrganizationId==s.OrganizationId||b.LaborCount>OrganizationCapacity.LaborCount(w,b.OwnerOrganizationId!)))
                throw new InvalidDataException("Invalid building labor staffing");
        foreach(var p in w.LaborPayroll)
        {
            var labor=w.OrganizationLabor.SingleOrDefault(l=>l.OrganizationId==p.OrganizationId);
            long count=w.LaborRecruitments.Count(h=>h.OrganizationId==p.OrganizationId&&h.Tick<=p.Tick);
            if(labor is null||p.Tick<=0||p.Tick>w.Clock.Tick||p.Tick%w.Time.Rules.TicksPerDay!=0||p.Count!=count||p.Required!=checked(count*r.DailySalary)||
                entries[p.Id].Count()!=(p.Paid&&p.Required>0?1:0)||p.Paid&&p.Required>0&&!entries[p.Id].Any(e=>e.Tick==p.Tick&&e.From==p.OrganizationId&&e.To==labor.PayrollAccountId&&e.ResourceId=="spirit-stone"&&e.Quantity==p.Required&&e.Reason=="OrganizationLaborWages"))
                throw new InvalidDataException("Invalid labor daily salary receipt");
        }
        if(w.SettlementDays.Where(d=>d.Laborers>0).GroupBy(d=>(d.OwnerId,d.Tick)).Any(g=>g.Sum(d=>d.Laborers)>
            (w.LaborPayroll.SingleOrDefault(p=>p.OrganizationId==g.Key.OwnerId&&p.Tick==g.Key.Tick&&p.Paid)?.Count??0)))
            throw new InvalidDataException("Labor produced in multiple buildings beyond paid headcount");
        foreach(var m in w.MemberRecruitments)
        {
            var expected=OrganizationLaborActions.InitialStock(o,m.RandomBefore,out ulong after);
            if(m.Tick<=0||m.Tick>w.Clock.Tick||m.RandomBefore==0||m.RandomAfter!=after||m.MemberId!="recruited:"+m.Id||!w.ProcessedIntentIds.Contains(m.Id)||
                !w.Characters.Any(c=>c.Id==m.MemberId)||!w.Organizations.Any(org=>org.Id==m.OrganizationId)||!w.Settlements.Any(s=>s.Id==m.SettlementId)||
                !m.Stock.OrderBy(p=>p.Key).SequenceEqual(expected.OrderBy(p=>p.Key))||entries[m.Id].Count()!=1+expected.Count(p=>p.Value>0)||
                !entries[m.Id].Any(e=>e.Tick==m.Tick&&e.From==m.OrganizationId&&e.To==m.MemberId&&e.ResourceId=="spirit-stone"&&e.Quantity==o.MemberCost&&e.Reason=="MemberRecruitmentFee")||
                expected.Where(p=>p.Value>0).Any(p=>entries[m.Id].Count(e=>e.Tick==m.Tick&&e.From==OrganizationLaborActions.RecruitSource&&e.To==m.MemberId&&e.ResourceId==p.Key&&e.Quantity==p.Value&&e.Reason=="RecruitInitialStock")!=1))
                throw new InvalidDataException("Invalid fixed member recruitment or random stock backing");
        }
        var hired=w.LaborRecruitments.Select(r=>r.Id).ToHashSet();var paid=w.LaborPayroll.Select(r=>r.Id).ToHashSet();var joined=w.MemberRecruitments.Select(r=>r.Id).ToHashSet();
        if(w.LaborRecruitments.Any(h=>!w.OrganizationLabor.Any(l=>l.OrganizationId==h.OrganizationId))||w.Ledger.Any(e=>
            e.Reason=="LaborRecruitmentFee"&&!hired.Contains(e.ActionId)||e.Reason=="OrganizationLaborWages"&&!paid.Contains(e.ActionId)||
            (e.From==OrganizationLaborActions.RecruitSource||e.Reason=="MemberRecruitmentFee")&&!joined.Contains(e.ActionId)))
            throw new InvalidDataException("Unbacked organization recruitment or salary ledger");
    }
}
