using Cultivation.Domain;

namespace Cultivation.Simulation;

internal static class OrganizationLaborDecisions
{
    internal static ActionIntent? Choose(WorldState w,CharacterState actor)
    {
        if(w.LaborAutonomy is not { } auto||w.OrganizationDevelopment is not { } policy||w.SettlementOrganizationDemo is not { } profile||
            actor.Id==w.ControlledCharacterId||actor.OrganizationId!=profile.Organization.Id||
            !w.Organizations.Any(o=>o.Id==profile.Organization.Id&&o.LeaderId==actor.Id)||actor.Health<=0||actor.SeverelyInjured||CombatActions.Active(w,actor.Id) is not null)return null;
        string owner=profile.Organization.Id;long money=Inventory.Balance(w,owner,"spirit-stone");int count=OrganizationCapacity.LaborCount(w,owner);
        ActionKind? kind=null;var rules=w.OrganizationLaborRules!;
        bool CanRecruit(long cost,int salaryCount)=>money>=policy.ReserveMoney&&money-policy.ReserveMoney>=cost&&
            money-policy.ReserveMoney-cost>=checked(salaryCount*rules.DailySalary*auto.SalaryReserveDays)&&Inventory.Balance(w,owner,"wood")>=policy.ReserveWood;
        if(count<auto.TargetLaborers&&count<OrganizationCapacity.LaborLimit(w,owner)&&CanRecruit(rules.RecruitmentCost,count+1))kind=ActionKind.RecruitOrganizationLabor;
        else if(count>=auto.TargetLaborers&&w.Characters.Count(c=>c.OrganizationId==owner)<auto.TargetMembers&&
            w.Characters.Count(c=>c.OrganizationId==owner)<OrganizationCapacity.MemberLimit(w,owner)&&CanRecruit(w.MemberRecruitmentRules!.Options.MemberCost,count))kind=ActionKind.RecruitOrganizationMember;
        if(kind is null)return null;
        var intent=new ActionIntent($"organization-recruit:{actor.Id}:{w.Clock.Tick}",actor.Id,profile.SettlementId,"recruit",1,0,DecisionSource.Npc,kind.Value,
            OrganizationId:owner,DecisionDetail:"依設定目標招攬，保留組織資產與雜役薪資預算");
        var s=w.Settlements.Single(s=>s.Id==profile.SettlementId);var anchor=w.HexMap!.Sites.Single(t=>t.Id==s.SiteId).Anchor;
        if(HexNavigation.Position(w.HexMap,actor)!=anchor)intent=new(intent.Id,actor.Id,anchor.ToString(),"travel",1,0,DecisionSource.Npc,ActionKind.Travel,DecisionDetail:"返回聚落招攬組織人力");
        return ActionValidator.Validate(w,intent) is null?intent:null;
    }
}
