using Cultivation.Domain;

namespace Cultivation.AI;

public sealed record OpponentObservation(string Id, int Health, int Attack, int Defense, string Source, long ObservedTick);
public sealed record BattleObservation(string CombatId, OpponentObservation Opponent, bool SkillPrepared);

internal static class CombatUtility
{
    internal static IReadOnlyList<ScoredAction> Evaluate(LocalObservation o)
    {
        if (o.CombatRules is not { } rules) return [];
        var choices = new List<ScoredAction>();
        var a = o.Actor;
        double combatPreference = o.Aspirations?.SingleOrDefault(d => d.Id == a.Aspiration)?.CombatMultiplier ?? (a.Aspiration == Aspiration.Combat ? 1.5 : 1);
        var stats = CharacterProgression.Stats(a, o.Advancement);
        bool Visible(OpponentObservation target) => target.Source == "sight:" + a.RegionId && target.ObservedTick == o.Tick;
        void Add(ActionKind kind, OpponentObservation target, string item, double value, string reason) =>
            choices.Add(new(new($"ai:{a.Id}:{o.Tick}", a.Id, target.Id, item, 1, 0, DecisionSource.Npc, kind), value, reason));
        if (o.Battle is { } battle)
        {
            if (!Visible(battle.Opponent)) return [];
            int incoming = CombatMath.Damage(battle.Opponent.Attack, stats.Defense);
            if (o.Pills > 0 && a.Health < 80) Add(ActionKind.CombatHeal, battle.Opponent, "pill", (100 - a.Health) * (1 + a.Caution), "戰中治療，避免下一回合無力繼續");
            if (a.Health <= incoming * (1 + a.Caution) || a.Health < rules.MinimumEngageHealth)
                Add(ActionKind.Retreat, battle.Opponent, "combat", (100 - a.Health) * (1 + a.Caution) + 10, "生命與可見敵方傷害顯示風險過高，評估撤退");
            if (o.Pills > 0 && !battle.SkillPrepared && a.Health > incoming)
                Add(ActionKind.PowerStrike, battle.Opponent, "pill", a.CombatDrive * combatPreference * Math.Max(1, CombatMath.Damage(stats.Attack, battle.Opponent.Defense, rules.PowerMultiplier) - CombatMath.Damage(stats.Attack, battle.Opponent.Defense)), $"消耗丹藥準備強擊，以縮短受傷時間；戰鬥志向倍率{combatPreference}");
            return choices.Where(c => c.Score > 0).OrderByDescending(c => c.Score).ToArray();
        }
        if (a.Hostile || a.Health < rules.MinimumEngageHealth) return [];
        foreach (var opponent in o.Opponents ?? [])
        {
            if (!Visible(opponent) || opponent.Health < rules.MinimumEngageHealth) continue;
            int outgoing = CombatMath.Damage(stats.Attack, opponent.Defense), incoming = CombatMath.Damage(opponent.Attack, stats.Defense);
            double rounds = Math.Ceiling(opponent.Health / (double)outgoing);
            double harm = rounds * incoming;
            if (harm >= a.Health * Math.Clamp(1 - a.Caution * 0.15, 0.1, 0.9) && o.Pills == 0) continue;
            double value = ((rules.MaximumLootStones * a.WealthWeight + a.CombatDrive * a.GrowthWeight * rules.VictoryExperience) * combatPreference - harm * a.Caution) / Math.Max(1, rounds * rules.RoundTicks);
            Add(ActionKind.Engage, opponent, "combat", value, $"比較可見攻防、預期回合、受傷風險、現有丹藥與可能戰利品；戰鬥志向倍率{combatPreference}，並非保證收益");
        }
        return choices.Where(c => c.Score > 0).ToArray();
    }
}
