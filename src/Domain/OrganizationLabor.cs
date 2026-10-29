namespace Cultivation.Domain;

public sealed record PopulationInvestmentRules(int GroupsPerAttempt,Dictionary<string,long> ResourcesPerGroup,
    decimal SuccessProbability,int PopulationPerSuccess,int ActionPoints);
public sealed record PopulationInvestmentReceipt(string Id,string SettlementId,string OwnerId,long Tick,long Sequence,
    int PopulationBefore,int Capacity,bool Success,int AddedPopulation,ulong RandomBefore,ulong RandomAfter,long Influence);
public sealed record OrganizationLaborRules(long RecruitmentCost,long DailySalary,int DormitoryBaseCapacity,
    int DormitoryCapacityPerLevel,int BaseMemberCapacity,int SeniorDormitoryCapacityPerLevel,int ActionPoints);
public sealed record RecruitStockRange(int Minimum,int Maximum);
public sealed record MemberRecruitmentOptions(string MemberTemplateId,string MemberName,long MemberCost,
    int MoneyMinimum,int MoneyMaximum,Dictionary<string,RecruitStockRange> Supplies);
public sealed record MemberRecruitmentRules(MemberRecruitmentOptions Options,CharacterState Template);
public sealed record LaborAutonomyRules(int TargetLaborers,int TargetMembers,int SalaryReserveDays,int DormitoryTargetLevel);
public sealed record OrganizationLaborState(string OrganizationId,string PayrollAccountId,int Count);
public sealed record LaborRecruitmentReceipt(string Id,string OrganizationId,string SettlementId,long Tick,int CountBefore);
public sealed record LaborPayrollReceipt(string Id,string OrganizationId,long Tick,int Count,long Required,bool Paid);
public sealed record MemberRecruitmentReceipt(string Id,string OrganizationId,string SettlementId,string MemberId,long Tick,
    ulong RandomBefore,ulong RandomAfter,Dictionary<string,long> Stock);

public static class OrganizationCapacity
{
    public static long LaborLimit(WorldState w,string organization)=>w.OrganizationLaborRules is not { } r?0:
        w.Settlements.SelectMany(s=>s.Buildings).Where(b=>b.OwnerOrganizationId==organization&&b.Kind==SettlementBuildingKind.Dormitory)
            .Sum(b=>(long)r.DormitoryBaseCapacity+(long)((b.Level??w.SettlementOperationRules!.InitialBuildingLevel)-w.SettlementOperationRules!.InitialBuildingLevel)*r.DormitoryCapacityPerLevel);
    public static long MemberLimit(WorldState w,string organization)=>w.OrganizationLaborRules is not { } r?long.MaxValue:
        r.BaseMemberCapacity+w.Settlements.SelectMany(s=>s.Buildings).Where(b=>b.OwnerOrganizationId==organization&&b.Kind==SettlementBuildingKind.SeniorDormitory)
            .Sum(b=>(long)(b.Level??w.SettlementOperationRules!.InitialBuildingLevel)*r.SeniorDormitoryCapacityPerLevel);
    public static int LaborCount(WorldState w,string organization)=>w.OrganizationLabor.FirstOrDefault(l=>l.OrganizationId==organization)?.Count??0;
    public static long Assigned(WorldState w,string organization)=>w.Settlements.SelectMany(s=>s.Buildings).Where(b=>b.OwnerOrganizationId==organization).Sum(b=>(long)b.LaborCount);
}
