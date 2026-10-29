namespace Cultivation.Domain;

public sealed record SectFacilityRules(string OrganizationId = "sect", long BrewContribution = 120,
    long GatherContribution = 4, long PillExchangePrice = 120, long HerbExchangePrice = 4,
    long ReservedPills = 10, long ReservedHerbs = 10);
public sealed record SectStockReservation(long Pills, long Herbs);
public sealed record SectWorkAssignment(string ActorId, string OrganizationId, string ProductId);
public sealed record SectWorkReceipt(string Id, long Tick, string ActorId, string OrganizationId, ActionKind Kind,
    bool Success, long Quantity, long Contribution);
