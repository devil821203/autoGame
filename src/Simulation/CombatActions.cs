using Cultivation.Domain;

namespace Cultivation.Simulation;

internal static class CombatActions
{
    internal static bool Handles(ActionKind kind) => kind is ActionKind.Engage or ActionKind.Retreat or ActionKind.CombatHeal or ActionKind.PowerStrike;
    internal static CombatEncounter? Active(WorldState w, string actor) => w.Combats.SingleOrDefault(c => c.Status == CombatStatus.Active && c.Withdrawn?.Contains(actor) != true && (c.InitiatorId == actor || c.DefenderId == actor || c.Allies?.Contains(actor) == true));

    internal static string? Validate(WorldState w, ActionIntent i)
    {
        var rules = w.CombatRules;
        if (rules is null) return "Combat not enabled";
        if (i.Quantity != 1 || i.DurationTicks != 0 || i.MissionId is not null) return "Invalid combat action";
        var actor = w.Characters.Single(c => c.Id == i.ActorId);
        var battle = Active(w, actor.Id);
        if (battle?.Allies is { Count: > 0 }) return PartyCombat.ValidateCommand(w, i, battle);
        if (i.Kind == ActionKind.Engage)
        {
            PaidLifeServices.Interrupt(w, i.ActorId); PaidLifeServices.Interrupt(w, i.TargetId);
            var target = w.Characters.SingleOrDefault(c => c.Id == i.TargetId);
            if (target is null || target.Id == actor.Id || target.RegionId != actor.RegionId || (!target.Hostile && !actor.Hostile)) return "No visible hostile target";
            if (w.DemoMapEnabled && target.DemoLocationId != actor.DemoLocationId) return "Combat target at another location";
            if (actor.SeverelyInjured || target.SeverelyInjured) return "Severe injury requires medical treatment before combat";
            if (i.ResourceId != "combat" || actor.Health < rules.MinimumEngageHealth || target.Health < rules.MinimumEngageHealth) return "Health insufficient for engagement";
            if (w.LifeSystem?.Work.Any(t=>t.Intent.ActorId==actor.Id||t.Intent.ActorId==target.Id)==true) return "Combat participant busy in life work";
            if (battle is not null || Active(w, target.Id) is not null || w.Scheduled.Any(t => t.Intent.ActorId == actor.Id || t.Intent.ActorId == target.Id)) return "Combat participant busy";
            if (rules.RoundTicks > long.MaxValue - w.Clock.Tick) return "Combat time overflow";
            return null;
        }
        if (battle is null || i.TargetId != (battle.InitiatorId == actor.Id ? battle.DefenderId : battle.InitiatorId)) return "No matching active combat";
        if ((battle.InitiatorId == actor.Id ? battle.InitiatorCommandTick : battle.DefenderCommandTick) == w.Clock.Tick) return "Already issued combat command this tick";
        if (i.Kind == ActionKind.Retreat) return i.ResourceId == "combat" ? null : "Invalid retreat";
        if (i.ResourceId != "pill" || Inventory.Balance(w, actor.Id, "pill") < 1) return "No combat pill";
        if (i.Kind == ActionKind.CombatHeal && actor.Health >= 100) return "No healing needed";
        if (i.Kind == ActionKind.PowerStrike && (battle.InitiatorId == actor.Id ? battle.InitiatorBoost : battle.DefenderBoost)) return "Skill already prepared";
        return null;
    }

    internal static void Execute(WorldState w, ActionIntent i)
    {
        var rules = w.CombatRules!;
        if (i.Kind == ActionKind.Engage)
        {
            w.Combats.Add(new(i.Id, i.ActorId, i.TargetId, w.Clock.Tick, checked(w.Clock.Tick + rules.RoundTicks)));
            w.Plans.RemoveAll(p => p.ActorId == i.ActorId || p.ActorId == i.TargetId);
            ActionExecutor.Emit(w, i.Id, "CombatStarted", $"{i.ActorId}:{i.TargetId}");
            SocialActions.Adjust(w, i.TargetId, i.ActorId, -10, -10, 20);
            return;
        }
        var battle = Active(w, i.ActorId)!;
        if (battle.Allies is { Count: > 0 }) { PartyCombat.Command(w, i, battle); return; }
        int index = w.Combats.IndexOf(battle);
        bool initiator = battle.InitiatorId == i.ActorId;
        battle = initiator ? battle with { InitiatorCommandTick = w.Clock.Tick } : battle with { DefenderCommandTick = w.Clock.Tick };
        if (i.Kind == ActionKind.Retreat)
        {
            var random = new SeededRandom(w.RandomState);
            bool success = random.NextUInt32() / ((double)uint.MaxValue + 1) < rules.RetreatChance;
            w.RandomState = random.State;
            if (success)
            {
                battle = battle with { Status = CombatStatus.Retreated, EndedTick = w.Clock.Tick };
                w.Plans.RemoveAll(p => p.ActorId == battle.InitiatorId || p.ActorId == battle.DefenderId);
            }
            ActionExecutor.Emit(w, i.Id, success ? "Retreated" : "RetreatFailed", i.ActorId);
        }
        else
        {
            ActionExecutor.Transfer(w, i, i.ActorId, "consumed", "CombatConsumed");
            if (i.Kind == ActionKind.CombatHeal)
            {
                int actorIndex = w.Characters.FindIndex(c => c.Id == i.ActorId);
                w.Characters[actorIndex] = w.Characters[actorIndex] with { Health = Math.Min(100, w.Characters[actorIndex].Health + w.Rules.HealingPerPill) };
            }
            else battle = initiator ? battle with { InitiatorBoost = true } : battle with { DefenderBoost = true };
            w.Memories.Add(new(i.ActorId, w.Clock.Tick, i.Kind, true, "pill", 0, -1, "self:combat"));
            ActionExecutor.Emit(w, i.Id, i.Kind.ToString(), i.ActorId);
        }
        w.Combats[index] = battle;
    }

    internal static void Advance(WorldState w)
    {
        if (w.CombatRules is not { } rules) return;
        foreach (var initial in w.Combats.Where(c => c.Status == CombatStatus.Active && c.NextRoundTick <= w.Clock.Tick).OrderBy(c => c.StartedTick).ThenBy(c => c.Id, StringComparer.Ordinal).ToArray())
        {
            if (RemoteCombat.Deferred(w, initial)) continue;
            var battle = initial;
            bool remote = RemoteCombat.IsFar(w, initial) || w.Performance is not null && initial.NextRoundTick < w.Clock.Tick;
            for (int cycle = 0; cycle < (remote ? rules.MaximumRounds : 1); cycle++)
            {
            if (battle.Status != CombatStatus.Active || battle.NextRoundTick > w.Clock.Tick) break;
            if (battle.Allies is { Count: > 0 }) { PartyCombat.Round(w, battle); continue; }
            int aIndex = w.Characters.FindIndex(c => c.Id == battle.InitiatorId), bIndex = w.Characters.FindIndex(c => c.Id == battle.DefenderId);
            var a = w.Characters[aIndex]; var b = w.Characters[bIndex];
            var aStats = CharacterProgression.Stats(a, w.Advancement); var bStats = CharacterProgression.Stats(b, w.Advancement);
            var random = new SeededRandom(w.RandomState);
            int aDamage = CombatMath.Damage(aStats.Attack, bStats.Defense, battle.InitiatorBoost ? rules.PowerMultiplier : 1, (int)(random.NextUInt32() % (uint)(rules.DamageVariance + 1)));
            int bDamage = CombatMath.Damage(bStats.Attack, aStats.Defense, battle.DefenderBoost ? rules.PowerMultiplier : 1, (int)(random.NextUInt32() % (uint)(rules.DamageVariance + 1)));
            w.RandomState = random.State;
            bool aLost = a.Health <= bDamage, bLost = b.Health <= aDamage;
            w.Characters[aIndex] = a with { Health = Math.Max(1, a.Health - bDamage) };
            w.Characters[bIndex] = b with { Health = Math.Max(1, b.Health - aDamage) };
            EquipmentDurabilityActions.CombatHit(w,battle.Id,a.Id,b.Id,Math.Min(aDamage,Math.Max(0,b.Health-1)));
            EquipmentDurabilityActions.CombatHit(w,battle.Id,b.Id,a.Id,Math.Min(bDamage,Math.Max(0,a.Health-1)));
            int rounds = battle.Rounds + 1;
            string? winner = aLost == bLost ? null : aLost ? b.Id : a.Id;
            bool done = aLost || bLost || rounds >= rules.MaximumRounds || rules.RoundTicks > long.MaxValue - w.Clock.Tick;
            var updated = battle with { Rounds = rounds, InitiatorBoost = false, DefenderBoost = false,
                NextRoundTick = done ? w.Clock.Tick : checked((remote ? battle.NextRoundTick : w.Clock.Tick) + rules.RoundTicks),
                Status = done ? winner is null ? CombatStatus.Draw : CombatStatus.Victory : CombatStatus.Active,
                WinnerId = done ? winner : null, EndedTick = done ? w.Clock.Tick : null };
            w.Combats[w.Combats.IndexOf(battle)] = updated;
            battle = updated;
            ActionExecutor.Emit(w, battle.Id, "CombatRound", $"{rounds}: {a.Id}傷害{aDamage} / {b.Id}傷害{bDamage}");
            if (!done) continue;
            w.Plans.RemoveAll(p => p.ActorId == a.Id || p.ActorId == b.Id);
            if (winner is not null) Reward(w, updated, winner, winner == a.Id ? b.Id : a.Id);
            HuntActions.Record(w, updated);
            ActionExecutor.Emit(w, battle.Id, "CombatEnded", $"{updated.Status}:{winner ?? "none"}");
            }
        }
    }

    internal static void Reward(WorldState w, CombatEncounter battle, string winner, string loser)
    {
        int index = w.Characters.FindIndex(c => c.Id == winner);
        w.Characters[index] = w.Characters[index] with { CombatExperience = CharacterDevelopment.GainExperience(w.Characters[index].CombatExperience, StaminaCalculator.Growth(w, w.Characters[index], w.CombatRules!.VictoryExperience)) };
        var loserState = w.Characters.Single(c => c.Id == loser);
        string[] items = DungeonLoot.Items(w, loserState);
        foreach (var item in items)
        {
            long amount = Math.Min(Inventory.Balance(w, loser, item), DungeonLoot.Cap(w, loserState, item));
            if (amount <= 0 || !InventoryCapacity.CanReceive(w, winner, item, amount)) continue;
            ActionExecutor.Transfer(w, new("$combat-loot:" + battle.Id, winner, loser, item, amount, 0, DecisionSource.Npc), loser, winner, "CombatLoot");
            w.Loot.Add(new(battle.Id, w.Clock.Tick, winner, loser, item, amount));
        }
    }
}

