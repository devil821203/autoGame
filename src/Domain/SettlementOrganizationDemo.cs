namespace Cultivation.Domain;

public sealed record SettlementOrganizationDemo(OrganizationState Organization, string LeaderTemplateId,
    string LeaderName, string SettlementId, string SceneTemplateId, OrganizationSiteKind InitialSiteKind,
    Dictionary<string,long> InitialStock);
