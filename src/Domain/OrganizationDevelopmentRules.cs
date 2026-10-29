namespace Cultivation.Domain;

public sealed record OrganizationDevelopmentRules(List<SettlementBuildingKind> BuildOrder,
    int TargetBuildingLevel, int MaximumSiteLevel, long ReserveMoney, long ReserveWood, bool AutoStaff);
