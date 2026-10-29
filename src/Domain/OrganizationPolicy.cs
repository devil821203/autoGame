namespace Cultivation.Domain;

public sealed record OrganizationPolicy(string OrganizationId, bool OpenApplications = false,
    int MaximumMembers = 30, long MinimumReputation = 0, bool MissionsMembersOnly = false);
