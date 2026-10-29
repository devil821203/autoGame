namespace Cultivation.Domain;

public sealed record PartyRules(int MaximumMembers = 3, long InvitationTicks = 20, int MinimumHealth = 50);
public enum PartyInviteStatus { Pending, Accepted, Declined, Expired }
public sealed record PartyInvitation(string Id, string FromId, string ToId, long ExpiresTick, PartyInviteStatus Status = PartyInviteStatus.Pending);
public sealed record PartyState(string Id, string LeaderId, List<string> Members);
public sealed record HuntRules(string OrganizationId = "sect", long IntervalTicks = 120, long RequiredKills = 5, long RewardPerKill = 10);
public sealed record HuntMission(string Id, string OrganizationId, MonsterKind Target, long Required, long RewardPerKill,
    long DeadlineTick, long Kills, List<string> Participants);
public sealed record HuntReceipt(string Id, string MissionId, string CombatId, string ActorId, long Tick, long Contribution);
public sealed record PartyLootReceipt(string Id, string CombatId, string FromId, string ToId, string ResourceId, long Quantity, long Tick);
