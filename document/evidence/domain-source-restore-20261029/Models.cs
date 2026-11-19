namespace Cultivation.Domain;

public enum DecisionSource { Player, Npc }
public sealed record CharacterState(string Id, string Name)
{
    public string RegionId { get; init; } = "valley";
    public double AlchemyChance { get; init; }
    public double WealthWeight { get; init; } = 1;
    public double GrowthWeight { get; init; } = 1;
    public double Caution { get; init; } = 1;
    public long Cultivation { get; init; }
    public Aspiration Aspiration { get; init; }
    public double AlchemyTalent { get; init; } = 1;
    public long AlchemyExperience { get; init; }
    public long GatheringExperience { get; init; }
    public long DesiredStones { get; init; } = 30;
    public long DesiredCultivation { get; init; } = 100;
    public int Health { get; init; } = 100;
    public double SocialWeight { get; init; } = 0.1;
    public double MarketAffinity { get; init; } = 0.5;
    public long Reputation { get; init; }
    public long DesiredReputation { get; init; } = 20;
    public double SectAffinity { get; init; } = 0.5;
    public CharacterIdentity Identity { get; init; }
    public string? OrganizationId { get; init; }
    public OrganizationPosition Position { get; init; }
    public int Realm { get; init; }
    public string? MethodId { get; init; }
    public long MethodExperience { get; init; }
    public string? EquippedWeaponId { get; init; }
    public int BaseAttack { get; init; } = 10;
    public int BaseDefense { get; init; } = 2;
    public bool Hostile { get; init; }
    public double CombatDrive { get; init; } = 0.5;
    public long CombatExperience { get; init; }
    public string? MentorId { get; init; }
    public long SocialCooldownUntil { get; init; }
}
public enum Aspiration { Balanced, Wealth, Alchemy, Cultivation }
public enum ActionKind { Transfer, Gather, Brew, Buy, Sell, Cultivate, Heal, PublishMission, AcceptMission, DeliverMission, CancelMission, LearnMethod, Breakthrough, EquipWeapon, UnequipWeapon, Engage, Retreat, CombatHeal, PowerStrike, InviteCooperation, InviteApprentice, InviteRecruit, AcceptSocialOffer, DeclineSocialOffer, AssistPartner, StudyWithMentor, SetOrganizationPolicy, ApplyToOrganization, LeaveOrganization, PromoteMember, DemoteMember, ExpelMember, RewardMember }
public sealed record NpcPlan(string ActorId, string Goal, List<ActionKind> Steps, long ExpiresTick,
    string? MissionId = null, long? ObservedHerbPrice = null, long? ObservedPillPrice = null);
public sealed record ActionMemory(string ActorId, long Tick, ActionKind Kind, bool Success,
    string ResourceId, long StoneDelta, long ItemDelta, string Source);
public sealed record EconomyRules(long HerbPrice = 4, long PillPrice = 15,
    long HerbsPerPill = 2, long GatherTicks = 2, long BrewTicks = 3,
    long CultivateTicks = 3, long GrowthPerPill = 10,
    long ExperiencePerAction = 1, double SkillChancePerExperience = 0.002,
    double MaximumSkillBonus = 0.2, int HealingPerPill = 40,
    long MemoryTicks = 20, long PlanLifetimeTicks = 30, int DecisionBudget = 12,
    long GatheringExperiencePerSpeedLevel = 10);
public sealed record OrganizationState(string Id, string Name, string? LeaderId = null, string RegionId = "valley");
public readonly record struct SimulationTick(long Value);
public sealed record GameClock(long Tick, int SimulatedSecondsPerTick);
public sealed record ActionIntent(string Id, string ActorId, string TargetId,
    string ResourceId, long Quantity, long DurationTicks, DecisionSource Source,
    ActionKind Kind = ActionKind.Transfer, string? DecisionDetail = null, NpcPlan? Plan = null,
    MissionOffer? Offer = null, string? MissionId = null, long? LimitPrice = null, string? SocialOfferId = null,
    OrganizationPolicy? Policy = null, string? OrganizationId = null);
public sealed record QueuedIntent(long Sequence, ActionIntent Intent);
public sealed record ScheduledTransfer(long Sequence, long DueTick, ActionIntent Intent);
public sealed record LedgerEntry(long Sequence, long Tick, string ActionId,
    string From, string To, string ResourceId, long Quantity, string Reason);
public sealed record WorldEvent(long Sequence, long Tick, string ActionId, string Kind, string Detail);
public sealed record ActionResult(string IntentId, bool Accepted, string Reason);

// Detached persistence DTO. Simulation never returns its live instance.
public sealed class WorldState
{
    public int SchemaVersion { get; set; } = 1;
    public GameClock Clock { get; set; } = new(0, 60);
    public ulong RandomState { get; set; } = 1;
    public long NextSequence { get; set; } = 1;
    public string ControlledCharacterId { get; set; } = "player";
    public List<CharacterState> Characters { get; set; } = [];
    public List<OrganizationState> Organizations { get; set; } = [];
    public List<LedgerEntry> Ledger { get; set; } = [];
    public List<WorldEvent> Events { get; set; } = [];
    public List<QueuedIntent> Pending { get; set; } = [];
    public List<ScheduledTransfer> Scheduled { get; set; } = [];
    public List<string> ProcessedIntentIds { get; set; } = [];
    public EconomyRules Rules { get; set; } = new();
    public List<NpcPlan> Plans { get; set; } = [];
    public List<ActionMemory> Memories { get; set; } = [];
    public string? DecisionCursor { get; set; }
    public List<MarketListing> MarketListings { get; set; } = [];
    public List<TradeReceipt> Trades { get; set; } = [];
    public List<MissionState> Missions { get; set; } = [];
    public List<DeliveryReceipt> Deliveries { get; set; } = [];
    public AdvancementRules? Advancement { get; set; }
    public CombatRules? CombatRules { get; set; }
    public List<CombatEncounter> Combats { get; set; } = [];
    public List<LootReceipt> Loot { get; set; } = [];
    public SocialRules? SocialRules { get; set; }
    public List<RelationshipState> Relationships { get; set; } = [];
    public List<SocialOffer> SocialOffers { get; set; } = [];
    public List<OrganizationPolicy> OrganizationPolicies { get; set; } = [];
}

public static class Inventory
{
    // The ledger is the only source of balances in phase 0.
    public static long Balance(WorldState world, string owner, string resource)
    {
        long balance = 0;
        foreach (var entry in world.Ledger.Where(e => e.ResourceId == resource))
        {
            if (entry.To == owner) balance = checked(balance + entry.Quantity);
            if (entry.From == owner) balance = checked(balance - entry.Quantity);
        }
        return balance;
    }
}
