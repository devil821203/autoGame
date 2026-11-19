namespace Cultivation.Domain;

public sealed record SectPayrollRules(string OrganizationId = "sect", long IntervalTicks = 10, long StonesPerMember = 30);
