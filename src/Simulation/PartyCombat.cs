using Cultivation.Domain;

namespace Cultivation.Simulation;

internal static class PartyCombat
{
    internal static IEnumerable<string> Participants(CombatEncounter b) => new[] { b.InitiatorId }.Concat(b.Allies ?? []).Where(id => b.Withdrawn?.Contains(id) != true);
    internal static string? ValidateCommand(WorldState w, ActionIntent i, CombatEncounter b)
    {
        var a = w.Characters.Single(c => c.Id == i.ActorId);
        if (i.TargetId != b.DefenderId || a.Health <= 1 || i.Quantity != 1 || i.DurationTicks != 0 || i.MissionId is not null ||
            b.AllyCommandTick == w.Clock.Tick && b.CommandedAllies?.Contains(a.Id) == true) return "Invalid party combat command";
        if (i.Kind == ActionKind.Retreat) return i.ResourceId == "combat" ? null : "Invalid retreat";
        return i.Kind is ActionKind.PowerStrike or ActionKind.CombatHeal && i.ResourceId == "pill" && Inventory.Balance(w, a.Id, "pill") > 0 &&
            (i.Kind == ActionKind.PowerStrike ? b.PreparedAllies?.Contains(a.Id) != true : a.Health < 100) ? null : "No available personal combat resource";
    }
    internal static void Command(WorldState w, ActionIntent i, CombatEncounter b)
    {
        var commands = b.AllyCommandTick == w.Clock.Tick ? b.CommandedAllies ?? [] : [];
        var updated = b with { AllyCommandTick = w.Clock.Tick, CommandedAllies = [.. commands, i.ActorId] };
        if (i.Kind == ActionKind.Retreat)
        {
            var random = new SeededRandom(w.RandomState); bool success = random.NextUInt32() / ((double)uint.MaxValue + 1) < w.CombatRules!.RetreatChance; w.RandomState = random.State;
            if (success) updated = updated with { Withdrawn = [.. b.Withdrawn ?? [], i.ActorId] };
            ActionExecutor.Emit(w, i.Id, success ? "PartyMemberRetreated" : "RetreatFailed", i.ActorId);
            if (!Participants(updated).Any()) updated = updated with { Status = CombatStatus.Retreated, EndedTick = w.Clock.Tick };
        }
        else
        {
            ActionExecutor.Transfer(w, i, i.ActorId, "consumed", "CombatConsumed");
            if (i.Kind == ActionKind.PowerStrike) updated = updated with { PreparedAllies = [.. b.PreparedAllies ?? [], i.ActorId] };
            else { int index = w.Characters.FindIndex(c => c.Id == i.ActorId); w.Characters[index] = w.Characters[index] with { Health = Math.Min(100, w.Characters[index].Health + w.Rules.HealingPerPill) }; }
            ActionExecutor.Emit(w, i.Id, i.Kind.ToString(), i.ActorId);
        }
        w.Combats[w.Combats.IndexOf(b)] = updated;
    }
    internal static void Round(WorldState w, CombatEncounter b)
    {
        var r = w.CombatRules!; var fighters = Participants(b).Select(id => w.Characters.Single(c => c.Id == id)).Where(c => c.Health > 1).ToArray();
        var enemy = w.Characters.Single(c => c.Id == b.DefenderId); var enemyStats = CharacterProgression.Stats(enemy, w.Advancement);
        var random = new SeededRandom(w.RandomState); int damage = 0;
        foreach (var a in fighters)
            damage += CombatMath.Damage(CharacterProgression.Stats(a, w.Advancement).Attack, enemyStats.Defense,
                b.PreparedAllies?.Contains(a.Id) == true ? r.PowerMultiplier : 1, (int)(random.NextUInt32() % (uint)(r.DamageVariance + 1)));
        string? hitTarget=null;int effectiveIncoming=0;
        if (fighters.Length > 0)
        {
            var target = fighters[b.Rounds % fighters.Length]; int incoming = CombatMath.Damage(enemyStats.Attack, CharacterProgression.Stats(target, w.Advancement).Defense, 1, (int)(random.NextUInt32() % (uint)(r.DamageVariance + 1)));
            w.Characters[w.Characters.IndexOf(target)] = target with { Health = Math.Max(1, target.Health - incoming) };
            hitTarget=target.Id;effectiveIncoming=Math.Min(incoming,Math.Max(0,target.Health-1));
        }
        w.RandomState = random.State;
        w.Characters[w.Characters.IndexOf(enemy)] = enemy with { Health = Math.Max(1, enemy.Health - damage) };
        foreach(var a in fighters)EquipmentDurabilityActions.CombatHit(w,b.Id,a.Id,enemy.Id,Math.Min(damage,Math.Max(0,enemy.Health-1)));
        if(hitTarget is not null)EquipmentDurabilityActions.CombatHit(w,b.Id,enemy.Id,hitTarget,effectiveIncoming);
        bool defeated = !Participants(b).Any(id => w.Characters.Single(c => c.Id == id).Health > 1), won = damage >= enemy.Health && !defeated;
        int rounds = b.Rounds + 1;
        bool done = won || defeated || damage >= enemy.Health || rounds >= r.MaximumRounds || r.RoundTicks > long.MaxValue - w.Clock.Tick;
        var updated = b with { Rounds = rounds, PreparedAllies = [], NextRoundTick = done ? w.Clock.Tick : w.Clock.Tick + r.RoundTicks,
            Status = !done ? CombatStatus.Active : won || defeated && damage < enemy.Health ? CombatStatus.Victory : CombatStatus.Draw,
            WinnerId = !done ? null : won ? b.InitiatorId : defeated && damage < enemy.Health ? b.DefenderId : null, EndedTick = done ? w.Clock.Tick : null };
        w.Combats[w.Combats.IndexOf(b)] = updated;
        ActionExecutor.Emit(w, b.Id, "PartyCombatRound", $"{rounds}:members={fighters.Length},damage={damage}");
        if (!done) return;
        if (won) Reward(w, updated);
        HuntActions.Record(w, updated);
        ActionExecutor.Emit(w, b.Id, "CombatEnded", $"{updated.Status}:{updated.WinnerId ?? "none"}");
    }
    internal static void Reward(WorldState w, CombatEncounter b)
    {
        var members = Participants(b).Order(StringComparer.Ordinal).ToArray();
        foreach (var id in members)
        { int index = w.Characters.FindIndex(c => c.Id == id); w.Characters[index] = w.Characters[index] with { CombatExperience = CharacterDevelopment.GainExperience(w.Characters[index].CombatExperience, StaminaCalculator.Growth(w, w.Characters[index], w.CombatRules!.VictoryExperience)) }; }
        var loser = w.Characters.Single(c => c.Id == b.DefenderId);
        string[] items = DungeonLoot.Items(w, loser);
        foreach (string item in items)
        {
            long amount = Math.Min(Inventory.Balance(w, loser.Id, item), DungeonLoot.Cap(w, loser, item));
            // Rotate indivisible items between members. Stones are divided with deterministic remainders.
            var order = members.Skip((b.Rounds + (int)(b.StartedTick % members.Length)) % members.Length).Concat(members.Take((b.Rounds + (int)(b.StartedTick % members.Length)) % members.Length)).ToArray();
            for (int n = 0; n < order.Length; n++)
            {
                long share = amount / order.Length + (n < amount % order.Length ? 1 : 0);
                if (share <= 0 || !InventoryCapacity.CanReceive(w, order[n], item, share)) continue;
                string id = $"party-loot:{b.Id}:{item}:{order[n]}";
                ActionExecutor.Transfer(w, new(id, order[n], loser.Id, item, share, 0, DecisionSource.Npc), loser.Id, order[n], "PartyLoot");
                w.PartyLoot.Add(new(id, b.Id, loser.Id, order[n], item, share, w.Clock.Tick));
            }
        }
    }
}


