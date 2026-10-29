using Cultivation.Domain;

namespace Cultivation.AI;

public sealed record LocalObservation(CharacterState Actor, long Tick, long Stones,
    long Herbs, long Pills, long Gatherable, long ShopStones, long ShopHerbs, long ShopPills,
    EconomyRules Rules, string Source, long ObservedTick, int RecentFailures = 0,
    IReadOnlyList<ActionMemory>? Memories = null, IReadOnlyList<MissionObservation>? Missions = null,
    long? HerbSellPrice = null, long? PillSellPrice = null, bool LiveQuotes = false,
    AdvancementRules? Advancement = null, long WeaponStock = 0,
    CombatRules? CombatRules = null, IReadOnlyList<OpponentObservation>? Opponents = null, BattleObservation? Battle = null,
    SocialRules? SocialRules = null, IReadOnlyList<ReceivedSocialOffer>? SocialOffers = null, IReadOnlyList<KnownContact>? Contacts = null,
    IReadOnlyList<OrganizationObservation>? Organizations = null,
    IReadOnlyList<RecruitmentContact>? RecruitmentContacts = null,
    IReadOnlyList<RecruitmentContact>? SuccessorContacts = null, DungeonRules? Dungeon = null, long WolfHides = 0, double DutyIncentive = 0,
    SectWorkObservation? SectWork = null, IReadOnlyList<AspirationDefinition>? Aspirations = null, ExpeditionObservation? Expedition = null, double GrowthMultiplier = 1);
public sealed record ScoredAction(ActionIntent Intent, double Score, string Reason);
public interface IActionPlanner
{
    IReadOnlyList<ScoredAction> Evaluate(LocalObservation observation);
}

// Receding-horizon planner: acquisition steps gain value from the feasible next goal.
public sealed class UtilityPlanner : IActionPlanner
{
    public IReadOnlyList<ScoredAction> Evaluate(LocalObservation o)
    {
        if (o.Source != "market:valley" || o.ObservedTick != o.Tick || o.Actor.RegionId != "valley") return [];
        var choices = new List<ScoredAction>();
        var a = o.Actor;
        if (o.Battle is not null) return CombatUtility.Evaluate(o);
        choices.AddRange(CombatUtility.Evaluate(o));
        choices.AddRange(SocialUtility.Evaluate(o));
        choices.AddRange(OrganizationUtility.Evaluate(o));
        choices.AddRange(SectWorkUtility.Evaluate(o));
        choices.AddRange(ExpeditionUtility.Evaluate(o));
        var r = o.Rules;
        var needs = CharacterDevelopment.Needs(a, o.Stones, o.Herbs, o.Pills, r);
        double need = 1 + needs.Wealth;
        var aspiration = o.Aspirations?.SingleOrDefault(d => d.Id == a.Aspiration);
        double growth = o.GrowthMultiplier * a.GrowthWeight * needs.Growth * r.GrowthUtilityMultiplier * (aspiration?.GrowthMultiplier ?? (a.Aspiration == Aspiration.Cultivation ? 1.5 : 1)) / (1 + a.Cultivation / 40.0);
        double wealth = a.WealthWeight * (aspiration?.WealthMultiplier ?? (a.Aspiration == Aspiration.Wealth ? 1.5 : 1));
        double chance = CharacterDevelopment.SuccessChance(a, r);
        double ObservedPrice(string item, long fallback)
        {
            if (o.LiveQuotes) return fallback;
            var memory = o.Memories?.LastOrDefault(m => m.ActorId == a.Id && m.Source == "market:valley" && m.ResourceId == item && m.Kind is ActionKind.Buy or ActionKind.Sell && m.Tick <= o.Tick && m.Tick >= o.Tick - r.MemoryTicks);
            return memory is null ? fallback : Math.Abs((double)memory.StoneDelta);
        }
        double profit = chance * ObservedPrice("pill", o.PillSellPrice ?? r.PillPrice) - r.HerbsPerPill * ObservedPrice("herb", r.HerbPrice);
        double social = a.SocialWeight * a.MarketAffinity * needs.Reputation;
        double healing = Math.Max((100 - a.Health) * (1 + a.Caution), r.KeepGrowthPill && a.Health < 100 ? r.PillPrice * wealth * need + 1 : 0);
        long herbSell = o.HerbSellPrice ?? r.HerbPrice;
        long pillSell = o.PillSellPrice ?? r.PillPrice;
        void Add(ActionKind kind, string item, string target, double score, string reason) =>
            choices.Add(new(new($"ai:{a.Id}:{o.Tick}", a.Id, target, item, 1, 0, DecisionSource.Npc, kind), score, reason));
        if (o.Dungeon is { } dungeon && o.CombatRules is { } combat && a.Health >= Math.Max(50, combat.MinimumEngageHealth) && !a.Hostile)
        {
            double herbs = (1 - dungeon.EncounterChance) * dungeon.HerbSuccessChance * (o.HerbSellPrice ?? r.HerbPrice);
            var loot = DungeonLootTable.For(dungeon);
            double DropValue(DungeonDrop drop) => drop.MeanQuantity * (drop.ResourceId switch
            {
                "spirit-stone" => 1,
                "herb" => herbSell,
                "$recovery" or "pill" or "ointment" => pillSell,
                _ => 0 // No known local price: do not invent a global market value.
            });
            double bandit = dungeon.EncounterChance * (1 - dungeon.WolfChance) * (dungeon.BanditStoneChance * DropValue(loot.BanditMoney) + dungeon.BanditPillChance * DropValue(loot.BanditRecovery));
            double combatPreference = aspiration?.CombatMultiplier ?? (a.Aspiration == Aspiration.Combat ? 1.5 : 1);
            Add(ActionKind.ExploreDungeon, "combat", "dungeon", ((herbs + bandit * combatPreference) * wealth * need - dungeon.EncounterChance * a.Caution * 10) / 4,
                $"比較副本採藥／山匪掉落的預期收益與遭遇風險；戰鬥志向倍率{combatPreference}，並非保證產出");
        }
        if (o.Advancement is { } advancement)
        {
            if (a.EquippedWeaponId is null && o.WeaponStock > 0)
                Add(ActionKind.EquipWeapon, advancement.WeaponId, a.Id, (advancement.WeaponAttack + advancement.WeaponDefense) * (1 + a.GrowthWeight), "裝備自有武器提升能力，不生成物品");
            if (a.Health >= 50 && a.MethodId is null && o.Stones >= advancement.LearningCost)
                Add(ActionKind.LearnMethod, "spirit-stone", a.Id,
                    (growth * (advancement.GrowthBonusPerMethodLevel * advancement.MaximumMethodLevel + advancement.AttackPerRealm * 2) - advancement.LearningCost * wealth / 10) / advancement.LearningTicks,
                    "比較功法長期成長與學習費用及耗時");
            if (a.Health >= 50 && a.MethodId is not null && a.Realm < advancement.MaximumRealm && a.Cultivation >= CharacterProgression.Threshold(a, advancement))
            {
                double benefit = growth * CharacterProgression.Threshold(a, advancement);
                if (o.Pills >= advancement.BreakthroughPills)
                    Add(ActionKind.Breakthrough, "pill", a.Id, benefit / advancement.BreakthroughTicks, "境界門檻與丹藥齊備，評估突破收益與時間");
                else if (o.ShopPills > 0 && o.Stones >= r.PillPrice)
                    Add(ActionKind.Buy, "pill", "market", benefit / (advancement.BreakthroughTicks + advancement.BreakthroughPills - o.Pills) - r.PillPrice * wealth / 10,
                        "購丹補足突破資源，再重新驗證門檻");
            }
        }
        if (o.Gatherable > 0 && a.Health >= 30)
            Add(ActionKind.Gather, "herb", "grove", herbSell * wealth * need / r.GatherTicks, "採集收益／耗時，資金不足提高工作需求");
        if (a.AlchemyChance > 0 && o.Herbs >= r.HerbsPerPill)
            Add(ActionKind.Brew, "herb", a.Id, (profit * wealth + growth * r.GrowthPerPill + (aspiration?.AlchemyExperienceMultiplier ?? (a.Aspiration == Aspiration.Alchemy ? 1 : 0)) * r.ExperiencePerAction * a.AlchemyTalent * o.GrowthMultiplier - (1 - chance) * a.Caution * (1 + o.RecentFailures) * r.HerbsPerPill * r.HerbPrice) / r.BrewTicks, "成品收益、天賦熟練與志向，扣除近期失敗風險及原料機會成本");
        if (o.Pills > 0)
        {
            if (a.Health < 100) Add(ActionKind.Heal, "pill", a.Id, healing, "治療需求與安全風險");
            Add(ActionKind.Cultivate, "pill", a.Id, growth * r.GrowthPerPill / r.CultivateTicks, "修煉需求隨既有成長遞減");
            if (o.ShopStones >= pillSell && (!r.KeepGrowthPill || a.Cultivation >= a.DesiredCultivation && a.Health == 100 || o.Pills > 1)) Add(ActionKind.Sell, "pill", "market", pillSell * wealth * need + social, "出售成品補充資金與本地合作偏好，保留需要自用的最後一顆丹");
        }
        if (o.Herbs > 0 && o.ShopStones >= herbSell && (a.AlchemyChance == 0 || o.Herbs > r.HerbsPerPill * 2))
            Add(ActionKind.Sell, "herb", "market", herbSell * wealth * need + social, "出售不需自用的原料與本地合作偏好");
        if (o.Pills == 0 && o.ShopPills > 0 && o.Stones >= r.PillPrice)
            Add(ActionKind.Buy, "pill", "market", Math.Max(healing, growth * r.GrowthPerPill / r.CultivateTicks) - r.PillPrice * wealth / 10 + social, "購丹後治療／修煉，包含安全與本地合作偏好");
        if (a.AlchemyChance > 0 && o.Herbs < r.HerbsPerPill && o.ShopHerbs > 0 && o.Stones >= r.HerbPrice && profit > 0)
            Add(ActionKind.Buy, "herb", "market", profit * a.WealthWeight / (r.BrewTicks + r.HerbsPerPill) + growth, "購料後煉丹，僅在預期利潤為正時考慮");
        foreach (var mission in o.Missions ?? [])
        {
            if (r.KeepGrowthPill && mission.ResourceId == "pill" && (a.Cultivation < a.DesiredCultivation || a.Health < 100) && o.Pills <= 1) continue;
            // Sect production outputs belong to the sect, so personal pill production cannot fulfill this order.
            if (o.SectWork is not null && mission.ResourceId == "pill" && o.Pills == 0 && !(o.ShopPills > 0 && o.Stones >= r.PillPrice && mission.UnitPrice > r.PillPrice)) continue;
            if (mission.ResourceId == "beast-hide")
            {
                if (mission.Source != $"board:{mission.OrganizationId}:{a.RegionId}" || mission.ObservedTick != o.Tick || mission.Remaining <= 0 || mission.DeadlineTick <= o.Tick + 2) continue;
                bool canHunt = o.Dungeon is not null && o.CombatRules is not null && a.Health >= Math.Max(50, o.CombatRules.MinimumEngageHealth) && a.CombatDrive > 0;
                if (o.WolfHides == 0 && !canHunt) continue;
                double value = o.WolfHides > 0 ? mission.UnitPrice * wealth * need + a.SocialWeight * 10 : mission.UnitPrice * wealth * o.Dungeon!.EncounterChance * o.Dungeon.WolfChance / 10 + a.CombatDrive - a.Caution * 3;
                ActionKind kind = !mission.Accepted ? ActionKind.AcceptMission : o.WolfHides > 0 ? ActionKind.DeliverMission : ActionKind.ExploreDungeon;
                choices.Add(new(new($"ai:{a.Id}:{o.Tick}", a.Id, kind == ActionKind.ExploreDungeon ? "dungeon" : mission.OrganizationId,
                    kind == ActionKind.ExploreDungeon ? "combat" : "beast-hide", 1, 0, DecisionSource.Npc, kind,
                    MissionId: kind == ActionKind.ExploreDungeon ? null : mission.Id), value, "衡量獸皮任務報酬、現有獸皮、健康與副本風險；探索不保證遇狼或成功"));
                continue;
            }
            if (mission.Source != $"board:{mission.OrganizationId}:{a.RegionId}" || mission.ObservedTick != o.Tick || mission.ResourceId != "pill" || mission.Remaining <= 0) continue;
            long remainingTicks = mission.DeadlineTick - o.Tick;
            double cost = o.Pills > 0 ? pillSell : a.AlchemyChance > 0 ? r.HerbsPerPill * r.HerbPrice / Math.Max(0.01, chance) : r.PillPrice;
            double time = o.Pills > 0 ? 1 : a.AlchemyChance > 0 ? r.BrewTicks / Math.Max(0.01, chance) + Math.Max(0, r.HerbsPerPill - o.Herbs) + 2 : 2;
            bool canAcquire = o.Pills > 0 || (a.AlchemyChance > 0 && (o.Herbs >= r.HerbsPerPill || o.Stones >= r.HerbPrice && o.ShopHerbs > 0 || o.Gatherable > 0)) || o.ShopPills > 0 && o.Stones >= r.PillPrice;
            if (remainingTicks <= time || !canAcquire) continue;
            double contribution = a.SocialWeight * Math.Clamp(a.SectAffinity + mission.RelationshipWeight, 0, 1) * mission.UnitPrice;
            double failureRisk = a.AlchemyChance > 0 && o.Pills == 0 ? (1 - chance) * a.Caution * (1 + o.RecentFailures) * r.HerbPrice : 0;
            double utility = ((mission.UnitPrice - cost) * wealth - failureRisk + contribution) / time;
            void MissionChoice(ActionKind kind, string item, string target, double score, string reason) =>
                choices.Add(new(new($"ai:{a.Id}:{o.Tick}", a.Id, target, item, 1, 0, DecisionSource.Npc, kind, MissionId: mission.Id), score, reason));
            if (!mission.Accepted)
            {
                MissionChoice(ActionKind.AcceptMission, "pill", mission.OrganizationId, utility, "比較任務報酬、成功率、成本、期限、宗門關係與時間機會成本");
                continue;
            }
            if (o.Pills > 0)
                MissionChoice(ActionKind.DeliverMission, "pill", mission.OrganizationId, mission.UnitPrice * wealth * need + contribution, "已有成品，交付可得實際報酬及宗門貢獻效用");
            else if (utility > 0)
            {
                if (a.AlchemyChance > 0 && o.Herbs >= r.HerbsPerPill)
                    MissionChoice(ActionKind.Brew, "herb", a.Id, utility + growth, "依任務收益煉丹，失敗風險與期限會重新評估");
                else if (a.AlchemyChance > 0 && o.ShopHerbs > 0 && o.Stones >= r.HerbPrice)
                    MissionChoice(ActionKind.Buy, "herb", "market", utility + growth, "購料後煉丹交付的任務計畫");
                else if (a.AlchemyChance > 0 && o.Gatherable > 0 && a.Health >= 30 && remainingTicks > r.GatherTicks + r.BrewTicks + 2)
                    MissionChoice(ActionKind.Gather, "herb", "grove", utility, "缺資金時採集原料再煉丹交付");
                else if (o.ShopPills > 0 && o.Stones >= r.PillPrice && mission.UnitPrice > r.PillPrice)
                    MissionChoice(ActionKind.Buy, "pill", "market", utility, "比較市場購丹後交付的價差與時間");
            }
        }
        choices = choices.Select(c => c.Intent.Kind is ActionKind.Buy or ActionKind.Sell
            ? c with { Intent = c.Intent with { LimitPrice = c.Intent.Kind == ActionKind.Buy ? (c.Intent.ResourceId == "herb" ? r.HerbPrice : r.PillPrice) : (c.Intent.ResourceId == "herb" ? herbSell : pillSell) } } : c).ToList();
        return choices.Select(c => c.Intent.MissionId is not null || c.Intent.Kind is ActionKind.ExploreDungeon or ActionKind.AcceptSectWork or ActionKind.SectGather or ActionKind.SectBrew
            ? c with { Score = c.Score + o.DutyIncentive, Reason = c.Reason + $"；本人考核留任誘因{o.DutyIncentive:F1}" } : c)
            .Where(c => c.Score > 0).OrderByDescending(c => c.Score).ThenBy(c => c.Intent.Kind).ToArray();
    }
}

