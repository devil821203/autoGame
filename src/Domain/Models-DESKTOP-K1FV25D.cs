namespace Cultivation.Domain;

public enum DecisionSource { Player, Npc }
public sealed record CharacterState(string Id, string Name)
{
    public TestProfession TestProfession { get; init; }
    public MortalCultivationState? MortalCultivation { get; init; }
    public Dictionary<string, Dictionary<MortalAttribute, long>> CurrentAttributeModifiers { get; init; } = [];
    public bool SeverelyInjured { get; init; }
    public string? CraftedWeaponId { get; init; }
    public string? EquippedArmorId { get; init; }
    public int EquipmentAttackBonus { get; init; }
    public int EquipmentDefenseBonus { get; init; }
    public bool LegacyWeaponBroken { get; init; }
    public LifeSkills? LifeSkills { get; init; }
    public int? Mindset { get; init; }
    public StaminaState? Stamina { get; init; }
    public HexCoord? HexPosition { get; init; }
    public string RegionId { get; init; } = "valley";
    public string DemoLocationId { get; init; } = "town";
    public MonsterKind MonsterKind { get; init; }
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
public enum Aspiration { Balanced, Wealth, Alchemy, Cultivation, Combat }
public enum ActionKind { Transfer, Gather, Brew, Buy, Sell, Cultivate, Heal, PublishMission, AcceptMission, DeliverMission, CancelMission, LearnMethod, Breakthrough, EquipWeapon, UnequipWeapon, Engage, Retreat, CombatHeal, PowerStrike, InviteCooperation, InviteApprentice, InviteRecruit, AcceptSocialOffer, DeclineSocialOffer, AssistPartner, StudyWithMentor, SetOrganizationPolicy, ApplyToOrganization, LeaveOrganization, PromoteMember, DemoteMember, ExpelMember, RewardMember, InviteSuccessor, Travel, ExploreDungeon, AcceptSectWork, SectGather, SectBrew, BuyContribution, RedeemContribution, SetSectStockReservation, InviteParty, AcceptParty, DeclineParty, LeaveParty, AcceptHunt, WaitParty, ConstructBuilding, CancelBuilding, Rest, BoardTurn, SetBoardAutoCombat, MedicalTreatment, TreatSevereInjury, CraftLifeItem, UseMedicine, UseCombatMedicine, EquipCraftedItem, UnequipCraftedItem, TrainMortalAttribute, MeditateQi, RequestLifeService, SetSettlementEmployees, SettlementBuy, SettlementSell, SupportSettlement, UpgradeSettlement, BuildSettlement, CreateOrganizationSite, UpgradeOrganizationSite, UpgradeSettlementBuilding, ExternalMarketBuy, ExternalMarketSell, InvestSettlementPopulation, RecruitOrganizationLabor, RecruitOrganizationMember, SetOrganizationLabor, Wait }
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
    long GatheringExperiencePerSpeedLevel = 10, double GrowthUtilityMultiplier = 1, bool KeepGrowthPill = false);
public sealed record OrganizationState(string Id, string Name, string? LeaderId = null, string RegionId = "valley", OrganizationKind Kind = OrganizationKind.Sect);
public readonly record struct SimulationTick(long Value);
public sealed record GameClock(long Tick, int SimulatedSecondsPerTick);
public sealed record ActionIntent(string Id, string ActorId, string TargetId,
    string ResourceId, long Quantity, long DurationTicks, DecisionSource Source,
    ActionKind Kind = ActionKind.Transfer, string? DecisionDetail = null, NpcPlan? Plan = null,
    MissionOffer? Offer = null, string? MissionId = null, long? LimitPrice = null, string? SocialOfferId = null,
    OrganizationPolicy? Policy = null, string? OrganizationId = null, SectStockReservation? StockReservation = null, BoardTurn? BoardTurn = null,
    ServiceMaterialSource? ServiceMaterials = null, HexCoord? SettlementBuildPosition = null, string? SettlementShopId = null);
public sealed record QueuedIntent(long Sequence, ActionIntent Intent);
public sealed record ScheduledTransfer(long Sequence, long DueTick, ActionIntent Intent);
public sealed record LedgerEntry(long Sequence, long Tick, string ActionId,
    string From, string To, string ResourceId, long Quantity, string Reason);
public sealed record WorldEvent(long Sequence, long Tick, string ActionId, string Kind, string Detail);
public sealed record WorldNotification(long EventSequence, string RecipientId);
public sealed record ActionResult(string IntentId, bool Accepted, string Reason);

// Detached persistence DTO. Simulation never returns its live instance.
public sealed class WorldState
{
    public List<CurrencyDefinition>? Currencies { get; set; }
    public List<SettlementState> Settlements { get; set; } = [];
    public SettlementOrganizationDemo? SettlementOrganizationDemo { get; set; }
    public OrganizationDevelopmentRules? OrganizationDevelopment { get; set; }
    public ExternalMarketState? ExternalMarket { get; set; }
    public EquipmentDurabilityState? EquipmentDurability { get; set; }
    public PopulationInvestmentRules? PopulationInvestmentRules { get; set; }
    public List<PopulationInvestmentReceipt> PopulationInvestments { get; set; } = [];
    public OrganizationLaborRules? OrganizationLaborRules { get; set; }
    public MemberRecruitmentRules? MemberRecruitmentRules { get; set; }
    public LaborAutonomyRules? LaborAutonomy { get; set; }
    public List<OrganizationLaborState> OrganizationLabor { get; set; } = [];
    public List<LaborRecruitmentReceipt> LaborRecruitments { get; set; } = [];
    public List<LaborPayrollReceipt> LaborPayroll { get; set; } = [];
    public List<MemberRecruitmentReceipt> MemberRecruitments { get; set; } = [];
    public SettlementDailyEconomyRules? SettlementDailyEconomy { get; set; }
    public List<SettlementConsumptionReceipt> SettlementConsumption { get; set; } = [];
    public List<SettlementPopulationReceipt> SettlementPopulationGrowth { get; set; } = [];
    public List<SettlementBuildingRule>? SettlementBuildingRules { get; set; }
    public SettlementRuntimeRules? SettlementRules { get; set; }
    public List<SettlementDayReceipt> SettlementDays { get; set; } = [];
    public List<SettlementInfluenceReceipt> SettlementInfluence { get; set; } = [];
    public List<SettlementConstructionRule>? SettlementConstructionRules { get; set; }
    public List<SettlementConstructionReceipt> SettlementConstruction { get; set; } = [];
    public OrganizationSiteRules? OrganizationSiteRules { get; set; }
    public List<OrganizationSettlementSite> OrganizationSites { get; set; } = [];
    public List<OrganizationSiteReceipt> OrganizationSiteReceipts { get; set; } = [];
    public SettlementOperationRules? SettlementOperationRules { get; set; }
    public SettlementTaxRules? SettlementTaxRules { get; set; }
    public List<SettlementBuildingUpgradeReceipt> SettlementBuildingUpgrades { get; set; } = [];
    public bool MedicineOnly { get; set; }
    public bool LifeSkillDemo { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public string RecoveryResourceId => MedicineOnly ? "ointment" : "pill";
    public bool AlchemyEnabled { get; set; } = true;
    public LifeSystemState? LifeSystem { get; set; }
    public LifeSkillRules? LifeSkillRules { get; set; }
    public WorldTimeState? Time { get; set; }
    public bool PlayerAutoSchedule { get; set; }
    public BoardBattleRules? BoardRules { get; set; }
    public BoardBattle? BoardBattle { get; set; }
    public MindsetRules? MindsetRules { get; set; }
    public MortalTrainingRules? MortalTrainingRules { get; set; }
    public MethodWorldState? Methods { get; set; }
    public QiAwakeningRules? QiAwakeningRules { get; set; }
    public MortalActionRules? MortalActionRules { get; set; }
    public SpecialistRules? SpecialistRules { get; set; }
    public PaidServiceRules? PaidServiceRules { get; set; }
    public List<PaidServiceReceipt> PaidServiceReceipts { get; set; } = [];
    public List<MortalTrainingWork> MortalTrainingWork { get; set; } = [];
    public StaminaRules? StaminaRules { get; set; }
    public HexMapState? HexMap { get; set; }
    public PerformanceRules? Performance { get; set; }
    public List<AiUpdateState> AiUpdates { get; set; } = [];
    public int? ScaleNpcCount { get; set; }
    internal LedgerBalanceIndex? BalanceIndex { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public HashSet<string>? ProcessedIntentIndex { get; set; }
    public PartyRules? PartyRules { get; set; }
    public HuntRules? HuntRules { get; set; }
    public List<PartyState> Parties { get; set; } = [];
    public List<PartyInvitation> PartyInvitations { get; set; } = [];
    public List<HuntMission> Hunts { get; set; } = [];
    public List<HuntReceipt> HuntReceipts { get; set; } = [];
    public List<PartyLootReceipt> PartyLoot { get; set; } = [];
    public List<ItemDefinition> Items { get; set; } = [];
    public List<AspirationDefinition> Aspirations { get; set; } = [];
    public DemoWorldRules? MapRules { get; set; }
    public MissionTemplate? MissionDefaults { get; set; }
    public bool DemoMapEnabled { get; set; }
    public RegenerationRules? HerbRegeneration { get; set; }
    public SectPayrollRules? SectPayroll { get; set; }
    public DungeonRules? Dungeon { get; set; }
    public SectDutyRules? SectDuty { get; set; }
    public List<MemberDuty> MemberDuties { get; set; } = [];
    public ProcurementRules? TestProcurement { get; set; }
    public List<ProcurementQuote> ProcurementQuotes { get; set; } = [];
    public List<ProcurementReceipt> ProcurementReceipts { get; set; } = [];
    public SectFacilityRules? SectFacilities { get; set; }
    public List<SectWorkAssignment> SectWorkAssignments { get; set; } = [];
    public List<SectWorkReceipt> SectWorkReceipts { get; set; } = [];
    public const int CurrentSchemaVersion = 3;
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public GameClock Clock { get; set; } = new(0, 60);
    public ulong RandomState { get; set; } = 1;
    public long NextSequence { get; set; } = 1;
    public string ControlledCharacterId { get; set; } = "player";
    public List<CharacterState> Characters { get; set; } = [];
    public List<OrganizationState> Organizations { get; set; } = [];
    public List<LedgerEntry> Ledger { get; set; } = [];
    public List<WorldEvent> Events { get; set; } = [];
    public List<WorldNotification> Notifications { get; set; } = [];
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
    public static void BeginFrame(WorldState world) => world.BalanceIndex = new LedgerBalanceIndex(world.Ledger);
    // The ledger is the only source of balances in phase 0.
    public static long Balance(WorldState world, string owner, string resource)
    {
        if (world.BalanceIndex is { } index && (!owner.StartsWith('$') || owner.StartsWith("$escrow:", StringComparison.Ordinal) || owner.StartsWith("$mission:", StringComparison.Ordinal))) return index.Balance(world.Ledger, owner, resource);
        long balance = 0;
        foreach (var entry in world.Ledger.Where(e => e.ResourceId == resource))
        {
            if (entry.To == owner) balance = checked(balance + entry.Quantity);
            if (entry.From == owner) balance = checked(balance - entry.Quantity);
        }
        return balance;
    }
}











