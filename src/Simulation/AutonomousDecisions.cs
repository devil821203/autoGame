using Cultivation.AI;
using Cultivation.Domain;

namespace Cultivation.Simulation;

internal static class AutonomousDecisions
{
    internal static void Queue(WorldState w, IActionPlanner planner, bool playerOnly = false)
    {
        var actors = w.Characters.Where(c => (c.Id == w.ControlledCharacterId ? w.PlayerAutoSchedule : !playerOnly) && !(w.BoardBattle is { Phase: BoardBattlePhase.Active } activeBoard && activeBoard.Fighters.Any(f => f.ActorId == c.Id)) && (c.RegionId == "valley" || w.Performance is not null) && c.MonsterKind == MonsterKind.None).OrderBy(c => c.Id, StringComparer.Ordinal).ToArray();
        var ordered = actors.Where(c => string.CompareOrdinal(c.Id, w.DecisionCursor) > 0)
            .Concat(actors.Where(c => string.CompareOrdinal(c.Id, w.DecisionCursor) <= 0)).ToArray();
        int evaluated = 0;
        var spatial = new WorldSpatialIndex(w);
        var byId = spatial.ById;
        var stamps = w.AiUpdates.ToDictionary(s => s.ActorId, StringComparer.Ordinal);
        var enemies = w.Characters.Where(c => c.Hostile && c.Health >= (w.CombatRules?.MinimumEngageHealth ?? 30)).ToArray();
        var relations = w.Relationships.ToLookup(r => r.FromId, StringComparer.Ordinal);
        var invitations = w.SocialOffers.Where(o => o.Status == SocialOfferStatus.Pending).ToLookup(o => o.ToId, StringComparer.Ordinal);
        var memberCounts = w.Characters.Where(c => c.OrganizationId is not null).ToLookup(c => c.OrganizationId!);
        var membershipRewards = w.Missions.Where(m => m.MembersOnly && m.ResourceId == "pill" && m.Status == MissionStatus.Active &&
            m.Delivered < m.Required && m.DeadlineTick > w.Clock.Tick + 4).ToLookup(m => m.OrganizationId, StringComparer.Ordinal);
        var admissionNotices = w.OrganizationPolicies.Join(w.Organizations, p => p.OrganizationId, o => o.Id, (policy, organization) => (policy, organization)).ToArray();
        var recentRecruitments = w.SocialOffers.Where(o => o.Kind == SocialOfferKind.Recruitment &&
            (o.Status == SocialOfferStatus.Pending || o.ExpiresTick > w.Clock.Tick)).ToLookup(o => o.FromId, StringComparer.Ordinal);
        var recentSuccessions = w.SocialOffers.Where(o => o.Kind == SocialOfferKind.Succession &&
            (o.Status == SocialOfferStatus.Pending || o.ExpiresTick > w.Clock.Tick)).ToLookup(o => o.FromId, StringComparer.Ordinal);
        foreach (var actor in ordered)
        {
            if (evaluated >= w.Rules.DecisionBudget) break;
            var source = actor.Id == w.ControlledCharacterId ? DecisionSource.Player : DecisionSource.Npc;
            w.DecisionCursor = actor.Id;
            if (!NpcDecisionTiming.Ready(w, actor, spatial.LatestNotifications.GetValueOrDefault(actor.Id, -1))) continue;
            w.Time?.IdleDecisions.Remove(actor.Id);
            if (w.MortalTrainingWork.Any(t => t.Intent.ActorId == actor.Id) || spatial.ScheduledActors.Contains(actor.Id) || w.Pending.Any(t => t.Intent.ActorId == actor.Id) ||
                w.Time is not null && TimeActions.Budget(w, actor.Id).BusyThroughTick > w.Clock.Tick || !AiLodScheduling.Due(w, actor, stamps, spatial.LatestNotifications)) continue;
            if (actor.Stamina is { Resting: true } || w.Time?.ApRestingActors.Contains(actor.Id) == true || w.LifeSystem?.Work.Any(t => t.Intent.ActorId == actor.Id || t.Service?.ClientId == actor.Id) == true) continue;
            if (TimeActions.NeedsApRest(w, actor.Id) && TimeActions.Budget(w, actor.Id).Remaining == 0) {
                var rest = new ActionIntent("ap-rest:" + actor.Id + ":" + w.Clock.Tick, actor.Id, actor.Id, "stamina", 1, 0, source, ActionKind.Rest, "AP不足，休息回復行動能力");
                if (ActionValidator.Validate(w, rest) is null) { w.Pending.Add(new(w.NextSequence++, rest)); evaluated++; continue; }
            }
            bool canRequestHealing = PaidLifeServices.Evaluate(w, actor, source).Any(c => c.Intent.ResourceId is "medical" or "severe-injury" && ActionValidator.Validate(w, c.Intent) is null);
            if (w.StaminaRules is { } stamina && !actor.SeverelyInjured && (actor.Stamina!.Current < stamina.ActivityCost || stamina.HealthRecoveryPerTick > 0 && actor.Health is > 0 and < 100 && !canRequestHealing) && CombatActions.Active(w, actor.Id) is null)
            {
                var rest = new ActionIntent("rest:" + actor.Id + ":" + w.Clock.Tick, actor.Id, actor.Id, "stamina", 1, 0, source, ActionKind.Rest, "體力不足或受傷，先休息回復" );
                if (ActionValidator.Validate(w, rest) is null) w.Pending.Add(new(w.NextSequence++, rest));
                evaluated++; continue;
            }
            evaluated++;
            AiLodScheduling.Mark(w, actor);
            var development = ExternalMarketDecisions.Choose(w, actor) ?? OrganizationLaborDecisions.Choose(w,actor) ?? OrganizationDevelopmentDecisions.Choose(w, actor);
            if (development is not null)
            {
                w.Plans.RemoveAll(p => p.ActorId == actor.Id);
                w.Pending.Add(new(w.NextSequence++, development));
                continue;
            }
            if (actor.RegionId != "valley")
            {
                var remote = AiLodScheduling.RemoteChoice(w, actor);
                if (remote is not null && ActionValidator.Validate(w, remote.Intent with { Source = source }) is null)
                    w.Pending.Add(new(w.NextSequence++, remote.Intent with { Source = source, DecisionDetail = remote.Reason }));
                continue;
            }
            var herbQuote = MarketPricing.Quote(w, "herb");
            var pillQuote = MarketPricing.Quote(w, "pill");
            OpponentObservation Observe(CharacterState target)
            {
                var stats = CharacterProgression.Stats(target, w.Advancement);
                return new(target.Id, target.Health, stats.Attack, stats.Defense, "sight:" + actor.RegionId, w.Clock.Tick);
            }
            var battle = CombatActions.Active(w, actor.Id);
            var battleObservation = battle is null ? null : new BattleObservation(battle.Id,
                Observe(byId[battle.DefenderId == actor.Id ? battle.InitiatorId : battle.DefenderId]), battle.PreparedAllies?.Contains(actor.Id) == true || (battle.InitiatorId == actor.Id ? battle.InitiatorBoost : battle.DefenderBoost));
            double MissionRelationship(MissionState mission)
            {
                string? leader = w.Organizations.Single(o => o.Id == mission.OrganizationId).LeaderId;
                var relation = relations[actor.Id].FirstOrDefault(r => r.ToId == leader);
                return relation is null ? 0 : (relation.Trust + relation.Affinity - relation.Grudge * 2) / 400.0;
            }
            long Own(string item) => Inventory.Balance(w, actor.Id, item);
            var observation = new LocalObservation(actor, w.Clock.Tick, Own("spirit-stone"), Own("herb"), Own("pill"),
                Inventory.Balance(w, "grove", "herb"), Inventory.Balance(w, "market", "spirit-stone"),
                Inventory.Balance(w, "market", "herb"), Inventory.Balance(w, "market", "pill"), w.Rules with { HerbPrice = herbQuote.BuyPrice, PillPrice = pillQuote.BuyPrice }, "market:valley", w.Clock.Tick,
                w.Memories.Count(m => m.ActorId == actor.Id && m.Kind == ActionKind.Brew && !m.Success),
                w.Memories.Where(m => m.ActorId == actor.Id).ToArray(),
                w.Missions.Where(m => m.RegionId == actor.RegionId && m.Status == MissionStatus.Active && m.DeadlineTick > w.Clock.Tick + 1 && (!m.MembersOnly || actor.OrganizationId == m.OrganizationId))
                    .Select(m => new MissionObservation(m.Id, m.OrganizationId, m.ResourceId, m.Required - m.Delivered, m.UnitPrice, m.DeadlineTick, m.Participants.Contains(actor.Id), $"board:{m.OrganizationId}:{m.RegionId}", w.Clock.Tick, MissionRelationship(m))).ToArray(),
                herbQuote.SellPrice, pillQuote.SellPrice, w.MarketListings.Count > 0,
                w.Advancement, w.Advancement is null ? 0 : EquipmentDurabilityActions.LegacyStock(w,actor.Id,w.Advancement.WeaponId), w.CombatRules,
                (w.DemoMapEnabled ? spatial.ByLocation[(actor.RegionId, actor.DemoLocationId)] : spatial.ByRegion[actor.RegionId]).Where(c => c.Id != actor.Id && c.Hostile && c.Health >= (w.CombatRules?.MinimumEngageHealth ?? 30)).Take(3).Select(Observe).ToArray(), battleObservation,
                w.SocialRules,
                invitations[actor.Id].Where(i => byId[i.FromId].RegionId == actor.RegionId).Select(i =>
                {
                    var relation = relations[actor.Id].FirstOrDefault(r => r.ToId == i.FromId);
                    return new ReceivedSocialOffer(i.Id, i.FromId, i.Kind, i.ExpiresTick, relation?.Trust ?? 0, relation?.Grudge ?? 0, "invitation:" + actor.Id, w.Clock.Tick);
                }).ToArray(),
                relations[actor.Id].Where(r => byId[r.ToId].RegionId == actor.RegionId && (r.Cooperating || actor.MentorId == r.ToId)).Take(4).Select(r =>
                {
                    var other = byId[r.ToId];
                    return new KnownContact(other.Id, r.Cooperating, actor.MentorId == other.Id,
                        r.Cooperating && other.AlchemyChance > 0 && Inventory.Balance(w, other.Id, "herb") < w.Rules.HerbsPerPill,
                        other.MethodId is not null && (actor.MethodId is null || actor.MethodExperience < other.MethodExperience),
                        r.Trust, r.Grudge, "contact:" + actor.Id, w.Clock.Tick,
                        w.SocialRules is null ? null : SocialPricing.Lesson(w.SocialRules, relations[other.Id].FirstOrDefault(t => t.ToId == actor.Id)?.Trust ?? 0));
                }).ToArray(),
                admissionNotices.Where(n => n.organization.RegionId == actor.RegionId).Select(n =>
                {
                    var relation = relations[actor.Id].FirstOrDefault(r => r.ToId == n.organization.LeaderId);
                    var opportunity = membershipRewards[n.organization.Id].Where(m => m.RegionId == actor.RegionId)
                        .OrderByDescending(m => m.UnitPrice).ThenByDescending(m => m.DeadlineTick).ThenBy(m => m.Id, StringComparer.Ordinal).FirstOrDefault();
                    return new OrganizationObservation(n.policy, n.organization.RegionId, memberCounts[n.organization.Id].Count(),
                        relation?.Trust ?? 0, relation?.Grudge ?? 0, $"admission:{n.organization.Id}:{actor.RegionId}", w.Clock.Tick,
                        n.organization.LeaderId == actor.Id,
                        opportunity?.UnitPrice ?? 0, opportunity?.DeadlineTick ?? long.MaxValue);
                }).ToArray(),
                relations[actor.Id].Where(r =>
                {
                    var target = byId[r.ToId];
                    return target.Id != actor.Id && target.RegionId == actor.RegionId && target.OrganizationId is null &&
                        !target.Hostile && target.Health > 0 && !recentRecruitments[actor.Id].Any(i => i.ToId == target.Id);
                }).OrderBy(r => r.ToId, StringComparer.Ordinal).Take(4).Select(r =>
                    new RecruitmentContact(r.ToId, byId[r.ToId].Reputation, r.Trust, r.Grudge,
                        $"recruitment-contact:{actor.Id}:{actor.RegionId}", w.Clock.Tick)).ToArray(),
                relations[actor.Id].Where(r =>
                {
                    var target = byId[r.ToId];
                    return actor.OrganizationId is not null && target.OrganizationId == actor.OrganizationId &&
                        target.Position is OrganizationPosition.Member or OrganizationPosition.Elder &&
                        target.RegionId == actor.RegionId && target.Health > 0 && !target.Hostile && r.Trust > 0 &&
                        !recentSuccessions[actor.Id].Any(i => i.ToId == target.Id);
                }).OrderBy(r => r.ToId, StringComparer.Ordinal).Take(4).Select(r =>
                    new RecruitmentContact(r.ToId, byId[r.ToId].Reputation, r.Trust, r.Grudge,
                        $"successor-contact:{actor.Id}:{actor.RegionId}", w.Clock.Tick)).ToArray(), w.Dungeon, Own("beast-hide"),
                w.MemberDuties.FirstOrDefault(d => d.ActorId == actor.Id) is { } duty ?
                    (duty.Misses + 1) * (double)(w.SectPayroll?.StonesPerMember ?? 30) : 0,
                w.SectFacilities is { } facilities && actor.OrganizationId == facilities.OrganizationId ? new SectWorkObservation(facilities,
                    w.SectWorkAssignments.Any(t => t.ActorId == actor.Id && t.ProductId == "herb"),
                    w.SectWorkAssignments.Any(t => t.ActorId == actor.Id && t.ProductId == "pill"),
                    Math.Max(0, Inventory.Balance(w, facilities.OrganizationId, "herb") - facilities.ReservedHerbs), Inventory.Balance(w, facilities.OrganizationId, "pill"), Own("contribution"),
                    $"sect-work:{facilities.OrganizationId}:{actor.RegionId}", w.Clock.Tick) : null, w.Aspirations, ExpeditionObservations.Observe(w, actor), CharacterGrowthCalculator.Modifiers(w, actor).CombinedMultiplier);
            var candidates = (w.Methods?.TrainingFocusActors?.Contains(actor.Id) == true ? MethodTraining.Evaluate(w, actor, source).Concat(LifeDecisions.Evaluate(w, actor, source)) : actor.TestProfession != TestProfession.None && w.SpecialistRules is not null ? SpecialistDecisions.Evaluate(w, actor, source) : planner.Evaluate(observation).Where(c => w.AlchemyEnabled || !(c.Intent.Kind is ActionKind.Brew or ActionKind.SectBrew || c.Intent.Kind == ActionKind.AcceptSectWork && c.Intent.ResourceId == "pill" || c.Intent.Kind == ActionKind.Buy && c.Intent.ResourceId == "herb")).Concat(LifeDecisions.Evaluate(w, actor, source)).Concat(MortalTrainingActions.Evaluate(w, actor, source))).Concat(PaidLifeServices.Evaluate(w, actor, source)).OrderByDescending(c => c.Score).ThenBy(c => c.Intent.Kind).Select(c => c with { Intent = c.Intent with {
                Source = source, Id = source == DecisionSource.Player ? $"player-auto:{actor.Id}:{w.Clock.Tick}" : c.Intent.Id } }).ToArray();
            if (w.DemoMapEnabled)
                candidates = candidates.Select(c =>
                {
                    var destination = TravelActions.RequiredLocation(c.Intent.Kind);
                    if (destination is null || destination == actor.DemoLocationId || ActionValidator.ValidateForTravel(w, c.Intent) is not null) return c;
                    long ticks = HexNavigation.Duration(w, actor, destination);
                    if (c.Intent.MissionId is { } missionId && w.Missions.Single(m => m.Id == missionId).DeadlineTick - w.Clock.Tick <= ticks + 2) return c;
                    return new ScoredAction(new(c.Intent.Id, actor.Id, destination, "travel", 1, 0, source, ActionKind.Travel),
                        c.Score / (1 + ticks / (double)w.Rules.PlanLifetimeTicks), $"為{c.Intent.Kind}前往{destination}，旅行{ticks} Tick；依計畫期限折算旅行成本，抵達後重評。{c.Reason}");
                }).OrderByDescending(c => c.Score).ThenBy(c => c.Intent.Kind).ToArray();
            var feasible = candidates.Where(c => ActionValidator.Validate(w, c.Intent) is null).ToArray();
            var oldPlan = w.Plans.SingleOrDefault(p => p.ActorId == actor.Id);
            bool quotesChanged = w.MarketListings.Count > 0 && oldPlan is not null && (oldPlan.ObservedHerbPrice != herbQuote.BuyPrice || oldPlan.ObservedPillPrice != pillQuote.BuyPrice);
            var choice = oldPlan is not null && !quotesChanged && oldPlan.ExpiresTick > w.Clock.Tick && oldPlan.Steps.Count > 0 && actor.Health >= 50
                ? feasible.FirstOrDefault(c => c.Intent.Kind == oldPlan.Steps[0] &&
                    c.Intent.MissionId == oldPlan.MissionId &&
                    (oldPlan.Goal != "Produce and sell" || c.Intent.Kind is not (ActionKind.Buy or ActionKind.Sell) || c.Intent.ResourceId == (c.Intent.Kind == ActionKind.Buy ? "herb" : "pill"))) : null;
            if (choice is null)
            {
                if (oldPlan is not null) ActionExecutor.Emit(w, actor.Id, "PlanInvalidated", "Expired, unsafe or next step infeasible");
                w.Plans.RemoveAll(p => p.ActorId == actor.Id);
                choice = feasible.FirstOrDefault();
                oldPlan = null;
            }
            if (choice is null) {
                if (TimeActions.NeedsApRest(w, actor.Id)) {
                    var rest = new ActionIntent("ap-recover:" + actor.Id + ":" + w.Clock.Tick, actor.Id, actor.Id, "stamina", 1, 0, source, ActionKind.Rest,
                        "目前無可行活動且AP未滿，先休息恢復");
                    if (ActionValidator.Validate(w, rest) is null) { w.Pending.Add(new(w.NextSequence++, rest)); continue; }
                }
                if (w.Time?.ActionDriven is { } driven) w.Time.IdleDecisions[actor.Id] =
                    new(w.Clock.Tick, checked(w.Clock.Tick + driven.IdleRetryTicks), actor.Health, actor.DemoLocationId);
                continue;
            }
            var plan = oldPlan ?? BuildPlan(observation, choice.Intent);
            string details = w.Performance is { LogCandidateDetails: false } ? choice.Reason : string.Join("; ", candidates.Select(c => $"{c.Intent.Kind}={c.Score.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)}:{c.Reason}:{ActionValidator.Validate(w, c.Intent) ?? "feasible"}"));
            w.Pending.Add(new(w.NextSequence++, choice.Intent with { Plan = plan, DecisionDetail = source == DecisionSource.Player ? "玩家自動排程；" + details : details }));
        }
        ActionExecutor.Emit(w, "autonomy", "DecisionBudget", evaluated.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private static NpcPlan BuildPlan(LocalObservation o, ActionIntent intent)
    {
        var steps = new List<ActionKind> { intent.Kind };
        string goal = intent.Kind.ToString();
        if (intent.MissionId is not null)
        {
            if (intent.Kind == ActionKind.Buy && intent.ResourceId == "herb")
            {
                for (long n = o.Herbs + 1; n < o.Rules.HerbsPerPill && steps.Count < 32; n++) steps.Add(ActionKind.Buy);
                steps.Add(ActionKind.Brew); steps.Add(ActionKind.DeliverMission);
            }
            else if (intent.Kind == ActionKind.Buy || intent.Kind == ActionKind.Brew) steps.Add(ActionKind.DeliverMission);
            else if (intent.Kind == ActionKind.Gather) { steps.Add(ActionKind.Brew); steps.Add(ActionKind.DeliverMission); }
            var mission = o.Missions!.Single(m => m.Id == intent.MissionId);
            return new(o.Actor.Id, "Mission:" + mission.Id, steps, Math.Min(mission.DeadlineTick, checked(o.Tick + o.Rules.PlanLifetimeTicks)), mission.Id, o.Rules.HerbPrice, o.Rules.PillPrice);
        }
        if (intent.Kind == ActionKind.Buy && intent.ResourceId == "pill")
        {
            bool breakthrough = o.Advancement is { } advancement && o.Actor.MethodId is not null && o.Actor.Health >= 50 && o.Actor.Realm < advancement.MaximumRealm && o.Actor.Cultivation >= CharacterProgression.Threshold(o.Actor, advancement);
            if (breakthrough)
            {
                for (long n = o.Pills + 1; n < o.Advancement!.BreakthroughPills && steps.Count < 32; n++) steps.Add(ActionKind.Buy);
                steps.Add(ActionKind.Breakthrough); goal = "Breakthrough";
            }
            else
            {
                steps.Add(o.Actor.Health < 100 ? ActionKind.Heal : ActionKind.Cultivate);
                goal = o.Actor.Health < 100 ? "Healing" : "Cultivation";
            }
        }
        if (intent.Kind == ActionKind.Buy && intent.ResourceId == "herb")
        {
            for (long n = o.Herbs + 1; n < o.Rules.HerbsPerPill && steps.Count < 32; n++) steps.Add(ActionKind.Buy);
            steps.Add(ActionKind.Brew); steps.Add(ActionKind.Sell);
            goal = "Produce and sell";
        }
        return new(o.Actor.Id, goal, steps, checked(o.Tick + o.Rules.PlanLifetimeTicks), ObservedHerbPrice: o.Rules.HerbPrice, ObservedPillPrice: o.Rules.PillPrice);
    }
}






