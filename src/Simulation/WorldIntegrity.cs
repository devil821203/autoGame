using Cultivation.Domain;

namespace Cultivation.Simulation;

public static class WorldIntegrity
{
    public static void Validate(WorldState world)
    {
        if (world.Organizations.Any(o => !Enum.IsDefined(o.Kind))) throw new InvalidDataException("Invalid or removed organization kind");
        SettlementIntegrity.Validate(world);
        SettlementEconomyIntegrity.Validate(world);
        SettlementOrganizationScenario.Validate(world);
        PopulationInvestmentActions.ValidateWorld(world);
        OrganizationLaborIntegrity.Validate(world);
        EquipmentDurabilityActions.Validate(world);
        ExternalMarketActions.ValidateWorld(world);
        SettlementDailyEconomy.Validate(world);
        SettlementPopulationGrowth.Validate(world);
        MortalCultivationIntegrity.Validate(world);
        MethodTraining.ValidateWorld(world);
        SpecialistProfile.Validate(world);
        PaidLifeServiceIntegrity.Validate(world);
        LifeSkillIntegrity.Validate(world);
        LifeSystemIntegrity.Validate(world);
        TimeActions.ValidateWorld(world);
        BoardBattleIntegrity.Validate(world);
        StaminaActions.ValidateWorld(world);
        CharacterGrowthIntegrity.Validate(world);
        ConfigurationIntegrity.Validate(world);
        PerformanceIntegrity.Validate(world);
        ExpeditionIntegrity.Validate(world);
        TravelActions.ValidateWorld(world);
        TestEconomyIntegrity.Validate(world);
        SectFacilityIntegrity.Validate(world);
        if (world.Characters.Any(c => !Enum.IsDefined(c.MonsterKind) || c.MonsterKind != MonsterKind.None && (!c.Hostile || c.DemoLocationId != "dungeon" || world.Dungeon is null))) throw new InvalidDataException("Invalid dungeon monster");
        if (world.Dungeon is { } dungeon && (!world.DemoMapEnabled || world.CombatRules is null || new[] { dungeon.EncounterChance, dungeon.WolfChance, dungeon.BanditStoneChance, dungeon.BanditPillChance, dungeon.HerbSuccessChance }.Any(p => !double.IsFinite(p) || p < 0 || p > 1))) throw new InvalidDataException("Invalid dungeon rules");
        if (world.SectPayroll is { } payroll && (payroll.IntervalTicks <= 0 || payroll.StonesPerMember <= 0 || !world.Organizations.Any(o => o.Id == payroll.OrganizationId))) throw new InvalidDataException("Invalid sect payroll rules");
        if (world.HerbRegeneration is { } regeneration && (regeneration.IntervalTicks <= 0 || regeneration.Amount <= 0 || regeneration.Capacity <= 0 || !world.Organizations.Any(o => o.Id == "grove"))) throw new InvalidDataException("Invalid regeneration rules");
        if (world.SchemaVersion != WorldState.CurrentSchemaVersion || world.Clock.Tick < 0 || world.Clock.SimulatedSecondsPerTick <= 0 || world.RandomState == 0)
            throw new InvalidDataException("Unsupported schema or invalid clock/random state.");
        var owners = world.Characters.Select(c => c.Id).Concat(world.Organizations.Select(o => o.Id)).ToArray();
        var ownerSet = owners.ToHashSet(StringComparer.Ordinal);
        var notificationEvents = world.Events.ToLookup(e => e.Sequence);
        var characters = world.Characters.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        if (world.Notifications.Distinct().Count() != world.Notifications.Count || world.Notifications.Any(n =>
            !characters.Contains(n.RecipientId) || notificationEvents[n.EventSequence].Count() != 1 ||
            notificationEvents[n.EventSequence].Single().Kind != "ResourceRegenerated")) throw new InvalidDataException("Invalid world notification source or recipient");
        var rules = world.Rules;
        if (world.Characters.Any(c => !double.IsFinite(c.SectAffinity) || c.SectAffinity is < 0 or > 1)) throw new InvalidDataException("Invalid sect affinity.");
        if (rules.HealingPerPill <= 0 || rules.HealingPerPill > 100 || rules.MemoryTicks <= 0 || rules.PlanLifetimeTicks <= 0 || rules.DecisionBudget <= 0 || rules.GatheringExperiencePerSpeedLevel <= 0 ||
            world.Characters.Any(c => c.Reputation < 0 || c.DesiredReputation < 0) ||
            world.Characters.Any(c => c.Health is < 0 or > 100 || !double.IsFinite(c.SocialWeight) || c.SocialWeight < 0 || !double.IsFinite(c.MarketAffinity) || c.MarketAffinity is < 0 or > 1))
            throw new InvalidDataException("Invalid needs or scheduling rules.");
        if (world.Plans.Select(p => p.ActorId).Distinct().Count() != world.Plans.Count || world.Plans.Any(p => !world.Characters.Any(c => c.Id == p.ActorId) || p.Steps.Count == 0 || p.Steps.Any(s => !Enum.IsDefined(s))) ||
            world.Memories.Any(m => !world.Characters.Any(c => c.Id == m.ActorId) || m.Tick < 0 || m.Tick > world.Clock.Tick || !Enum.IsDefined(m.Kind)))
            throw new InvalidDataException("Invalid plan or memory.");
        if (rules.ExperiencePerAction <= 0 || !double.IsFinite(rules.SkillChancePerExperience) || rules.SkillChancePerExperience < 0 ||
            !double.IsFinite(rules.MaximumSkillBonus) || rules.MaximumSkillBonus < 0 || rules.MaximumSkillBonus > 1 ||
            world.Characters.Any(c => !Enum.IsDefined(c.Aspiration) || !double.IsFinite(c.AlchemyTalent) || c.AlchemyTalent <= 0 || c.AlchemyTalent > 10 ||
                c.AlchemyExperience < 0 || c.GatheringExperience < 0 || c.DesiredStones < 0 || c.DesiredCultivation < 0 || c.WealthWeight < 0 || c.GrowthWeight < 0 || c.Caution < 0))
            throw new InvalidDataException("Invalid development state.");
        if (rules.HerbPrice <= 0 || rules.PillPrice <= 0 || rules.HerbsPerPill <= 0 || rules.GatherTicks <= 0 || rules.BrewTicks <= 0 || rules.CultivateTicks <= 0 || rules.GrowthPerPill <= 0 ||
            world.Characters.Any(c => !double.IsFinite(c.AlchemyChance) || c.AlchemyChance < 0 || c.AlchemyChance > 1 || c.Cultivation < 0 || !double.IsFinite(c.WealthWeight) || !double.IsFinite(c.GrowthWeight) || !double.IsFinite(c.Caution)))
            throw new InvalidDataException("Invalid character or economy rules.");
        if (owners.Any(id => string.IsNullOrWhiteSpace(id) || id.StartsWith("$", StringComparison.Ordinal)) || owners.Distinct().Count() != owners.Length ||
            !world.Characters.Any(c => c.Id == world.ControlledCharacterId))
            throw new InvalidDataException("Invalid owners or player assignment.");
        var sequences = world.Ledger.Select(e => e.Sequence).Concat(world.Events.Select(e => e.Sequence))
            .Concat(world.Pending.Select(e => e.Sequence)).Concat(world.Scheduled.Select(e => e.Sequence)).ToArray();
        if (sequences.Any(s => s <= 0 || s >= world.NextSequence) || sequences.Distinct().Count() != sequences.Length)
            throw new InvalidDataException("Invalid sequence state.");
        if (world.ProcessedIntentIds.Distinct(StringComparer.Ordinal).Count() != world.ProcessedIntentIds.Count)
            throw new InvalidDataException("Duplicate processed IDs.");
        var balances = new Dictionary<(string, string), long>();
        var ledgerByAction = world.Ledger.ToLookup(e => e.ActionId, StringComparer.Ordinal);
        var eventsByAction = world.Events.ToLookup(e => e.ActionId, StringComparer.Ordinal);
        var workByAction = world.SectWorkReceipts.ToLookup(r => r.Id, StringComparer.Ordinal);
        var huntsByAction = world.HuntReceipts.ToLookup(r => r.Id, StringComparer.Ordinal);
        var procurementByAction = world.ProcurementReceipts.ToLookup(r => r.Id, StringComparer.Ordinal);
        var dungeonIssuance = world.Ledger.Where(e => e.From == "$dungeon").ToLookup(e => (e.To, e.ResourceId));
        var regenerationIssuance = world.Ledger.Where(e => e.From == "$regeneration").ToLookup(e => e.Tick);
        foreach (var entry in world.Ledger.OrderBy(e => e.Sequence))
        {
            if (entry.Quantity <= 0 || string.IsNullOrWhiteSpace(entry.ResourceId) || entry.Tick < 0 || entry.Tick > world.Clock.Tick || entry.From == entry.To)
                throw new InvalidDataException("Invalid ledger entry.");
            bool Known(string owner) => ownerSet.Contains(owner) || owner.StartsWith("$escrow:", StringComparison.Ordinal) || world.Missions.Any(m => MissionActions.Account(m.Id) == owner);
            if (!Known(entry.To) || (!Known(entry.From) && entry.From != "$genesis" && entry.From != "$alchemy" && entry.From != "$regeneration" && entry.From != "$dungeon" && entry.From != "$test-procurement" && entry.From != "$dungeon-herb" && entry.From != "$contribution" && entry.From != "$life-production" && entry.From != "$settlement-production" && entry.From != "$settlement-residents" && entry.From != "$external-market" && entry.From != "$recruitment" && entry.From != "$equipment-instance"))
                throw new InvalidDataException("Unknown ledger account.");
            if (entry.From == "$equipment-instance")
            {
                if(world.EquipmentDurability is null)throw new InvalidDataException("Equipment instance without durability rules");
            }
            else if (entry.From == "$recruitment")
            {
                if(world.MemberRecruitmentRules is null)throw new InvalidDataException("Recruitment source without rules");
            }
            else if (entry.From == "$external-market")
            {
                if(world.ExternalMarket is null)throw new InvalidDataException("External market issuance without market snapshot");
            }
            else if (entry.From == "$genesis")
            {
                if (entry.Tick != 0 || entry.Reason != "Genesis") throw new InvalidDataException("Invalid issuance.");
            }
            else if (entry.From == "$settlement-residents")
            {
                var receipt = world.SettlementConsumption.SingleOrDefault(r => r.Id == entry.ActionId);
                if (world.SettlementDailyEconomy is null || receipt is null || entry.Reason != "SettlementConsumptionIncome" ||
                    entry.Tick != receipt.Tick || entry.ResourceId != "spirit-stone" || entry.Quantity != receipt.Income ||
                    !world.Settlements.Any(s => s.Id == receipt.SettlementId && s.OrganizationId == entry.To))
                    throw new InvalidDataException("Invalid settlement resident consumption income");
            }
            else if (entry.From == "$settlement-production")
            {
                if(world.SettlementOperationRules is not null)
                {
                    var receipt=world.SettlementDays.SingleOrDefault(d=>d.Id==entry.ActionId);
                    var settlement=world.Settlements.SingleOrDefault(s=>s.Id==receipt?.SettlementId);
                    if(receipt is null||settlement is null||entry.Tick!=receipt.Tick||entry.ResourceId!=receipt.Output||
                        !(entry.Reason=="SettlementProduced"&&entry.To==receipt.OwnerId&&entry.Quantity==receipt.Quantity||
                          entry.Reason=="SettlementProductionTax"&&entry.To==settlement.OrganizationId&&entry.Quantity==receipt.TaxQuantity))
                        throw new InvalidDataException("Invalid owned settlement production issuance");
                }
                else
                {
                if (world.SettlementRules is null || entry.Reason != "SettlementProduced" ||
                    world.SettlementDays.Count(d => d.Id == entry.ActionId && d.Tick == entry.Tick && d.Output == entry.ResourceId &&
                        d.Quantity == entry.Quantity && world.Settlements.Any(s => s.Id == d.SettlementId && s.OrganizationId == entry.To)) != 1 ||
                    ledgerByAction[entry.ActionId].Count(e => e.From == "$settlement-production") != 1)
                    throw new InvalidDataException("Invalid settlement production issuance");
                }
            }
            else if (entry.From == "$contribution")
            {
                var facilities = world.SectFacilities;
                bool earned = entry.Reason == "ContributionHuntReward" && huntsByAction[entry.ActionId].Any(r => r.Tick == entry.Tick && r.ActorId == entry.To && r.Contribution == entry.Quantity) || entry.Reason == "ContributionWorkReward" && workByAction[entry.ActionId].Any(r => r.Tick == entry.Tick && r.ActorId == entry.To && r.Success && r.Contribution == entry.Quantity);
                bool bought = entry.Reason == "ContributionPurchased" && facilities is not null && ledgerByAction[entry.ActionId].Count(e => e.Tick == entry.Tick && e.From == entry.To && e.To == facilities.OrganizationId && e.ResourceId == "spirit-stone" && e.Quantity == entry.Quantity && e.Reason == "ContributionPurchasePayment") == 1;
                if (facilities is null || entry.ResourceId != "contribution" || !earned && !bought || ledgerByAction[entry.ActionId].Count(e => e.From == "$contribution") != 1) throw new InvalidDataException("Invalid contribution issuance");
            }
            else if (entry.From == "$dungeon-herb")
            {
                if (world.Dungeon is null || entry.ResourceId != "herb" || entry.Quantity != 1 || entry.Reason != "DungeonForaged" || !world.Characters.Any(c => c.Id == entry.To) ||
                    !eventsByAction[entry.ActionId].Any(e => e.Tick == entry.Tick && e.Kind == "DungeonGathered" && e.Detail == entry.To) ||
                    ledgerByAction[entry.ActionId].Count(e => e.From == "$dungeon-herb") != 1) throw new InvalidDataException("Invalid dungeon gathering issuance");
            }
            else if (entry.From == "$test-procurement")
            {
                if (world.TestProcurement is not { } procurement || entry.To != procurement.BuyerId || entry.ResourceId != "spirit-stone" || entry.Reason != "TestProcurementFunding" ||
                    !procurementByAction[entry.ActionId].Any(r => r.Tick == entry.Tick && r.Payment == entry.Quantity)) throw new InvalidDataException("Invalid test funding issuance");
            }
            else if (entry.From == "$dungeon")
            {
                var monster = world.Characters.SingleOrDefault(c => c.Id == entry.To);
                if (world.Dungeon is null || entry.Reason != "DungeonSpawnLoot" || monster is null ||
                    DungeonLoot.Definition(world, monster.MonsterKind, entry.ResourceId) is not { } drop ||
                    entry.Quantity < drop.MinimumQuantity || entry.Quantity > drop.MaximumQuantity ||
                    dungeonIssuance[(entry.To, entry.ResourceId)].Count() != 1)
                    throw new InvalidDataException("Invalid dungeon loot issuance");
            }
            else if (entry.From == "$regeneration")
            {
                var r = world.HerbRegeneration;
                if (r is null || entry.Tick <= 0 || entry.Tick % r.IntervalTicks != 0 || entry.ActionId != "regeneration:" + entry.Tick ||
                    entry.Reason != "Regenerated" || entry.To != "grove" || entry.ResourceId != "herb" || entry.Quantity > r.Amount ||
                    regenerationIssuance[entry.Tick].Count() != 1 ||
                    balances.GetValueOrDefault(("grove", "herb")) > r.Capacity - entry.Quantity) throw new InvalidDataException("Invalid regeneration issuance");
            }
            else if (entry.From == "$life-production")
            {
                var receipt=world.LifeSystem?.Receipts.SingleOrDefault(r=>r.ActionId==entry.ActionId);
                var recipe=world.LifeSystem?.Recipes.SingleOrDefault(r=>r.Id==receipt?.RecipeId);
                if(receipt is null || recipe is null || entry.Quantity!=1 || entry.Reason!="LifeProduced" || entry.ResourceId!=receipt.Output || entry.To!=receipt.ActorId || entry.Tick!=receipt.Tick ||
                    ledgerByAction[entry.ActionId].Count(e=>e.From=="$life-production")!=1 || recipe.Inputs.Any(p=>!ledgerByAction[entry.ActionId].Any(e=>e.To=="consumed"&&e.ResourceId==p.Key&&e.Quantity==p.Value&&e.Reason=="LifeConsumed"&&e.Sequence<entry.Sequence)))
                    throw new InvalidDataException("Invalid life production issuance");
            }            else if (entry.From == "$alchemy")
            {
                if (entry.Reason != "Brewed" || entry.ResourceId != "pill" || entry.Quantity != 1 ||
                    ledgerByAction[entry.ActionId].Count(e => e.From == "$alchemy") != 1 ||
                    !ledgerByAction[entry.ActionId].Any(e => e.To == "consumed" && e.ResourceId == "herb" && e.Quantity == rules.HerbsPerPill && e.Sequence < entry.Sequence))
                    throw new InvalidDataException("Invalid production issuance.");
            }
            else
            {
                var key = (entry.From, entry.ResourceId);
                balances[key] = checked(balances.GetValueOrDefault(key) - entry.Quantity);
                if (balances[key] < 0) throw new InvalidDataException("Negative inventory.");
            }
            var recipient = (entry.To, entry.ResourceId);
            balances[recipient] = checked(balances.GetValueOrDefault(recipient) + entry.Quantity);
        }
        var tasks = world.Scheduled;
        if (tasks.Select(t => t.Intent.Id).Distinct().Count() != tasks.Count)
            throw new InvalidDataException("Duplicate scheduled action.");
        foreach (var task in tasks)
        {
            if (task.Intent.Kind == ActionKind.Travel)
            {
                if (task.DueTick <= world.Clock.Tick || !world.ProcessedIntentIds.Contains(task.Intent.Id)) throw new InvalidDataException("Invalid travel timing");
                continue;
            }
            if (task.DueTick <= world.Clock.Tick || !world.ProcessedIntentIds.Contains(task.Intent.Id) ||
                task.Intent.Kind != ActionKind.ConstructBuilding && !owners.Contains(task.Intent.TargetId) || task.Intent.Quantity <= 0 ||
                Inventory.Balance(world, ActionExecutor.Escrow(task.Intent.Id), task.Intent.ResourceId) != task.Intent.Quantity)
                throw new InvalidDataException("Invalid scheduled reservation.");
        }
        foreach (var pair in balances.Where(p => p.Key.Item1.StartsWith("$escrow:", StringComparison.Ordinal) && p.Value != 0))
            if (!tasks.Any(t => ActionExecutor.Escrow(t.Intent.Id) == pair.Key.Item1 && t.Intent.ResourceId == pair.Key.Item2 && t.Intent.Quantity == pair.Value) &&
                world.LifeSystem?.Work.Any(t => ActionExecutor.Escrow(t.Intent.Id) == pair.Key.Item1 && t.Inputs.GetValueOrDefault(pair.Key.Item2) == pair.Value ||
                    t.Service is { } c && PaidLifeServices.Account(t.Intent.Id) == pair.Key.Item1 && pair.Key.Item2 == "spirit-stone" && pair.Value == c.Fee) != true)
                throw new InvalidDataException("Orphaned reservation.");
        try { EconomyIntegrity.Validate(world); }
        catch (OverflowException error) { throw new InvalidDataException("Economy arithmetic overflow.", error); }
        AdvancementIntegrity.Validate(world);
        CombatIntegrity.Validate(world);
        SocialIntegrity.Validate(world);
    }
}






