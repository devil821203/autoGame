using Cultivation.Domain;

namespace Cultivation.Simulation;

public static class GrowthScenario
{
    public static WorldState Create(string name = "行者", ulong seed = 12345, GameConfiguration? configuration = null)
    {
        var config = configuration ?? GameConfiguration.Find();
        var world = HexWorldScenario.Create(name, seed, config);
        world.StaminaRules = config.Read<StaminaRules>("characters/stamina.json");
        world.MindsetRules = config.Read<MindsetRules>("characters/mindset.json");
        world.Characters = world.Characters.Select(c => c with { Stamina = new(world.StaminaRules.Maximum), Mindset = world.MindsetRules.Initial }).ToList();
        WorldIntegrity.Validate(world);
        return world;
    }
}

internal static class StaminaActions
{
    internal static string? Validate(WorldState w, ActionIntent i)
    {
        if (w.StaminaRules is not { } rules) return i.Kind != ActionKind.Rest ? null :
            TimeActions.NeedsApRest(w, i.ActorId) && i.TargetId == i.ActorId && i.ResourceId == "stamina" && i.Quantity == 1 && i.DurationTicks == 0 && i.MissionId is null &&
            !w.Time!.ApRestingActors.Contains(i.ActorId) ? null : "Rest unavailable or AP full";
        var actor = w.Characters.Single(c => c.Id == i.ActorId);
        var state = actor.Stamina!;
        if (i.Kind == ActionKind.Rest)
            return i.TargetId != actor.Id || i.ResourceId != "stamina" || i.Quantity != 1 || i.DurationTicks != 0 || i.MissionId is not null ||
                state.Resting || (state.Current == rules.Maximum && (rules.HealthRecoveryPerTick == 0 || actor.Health == 100) && !TimeActions.NeedsApRest(w, actor.Id)) || w.Scheduled.Any(t => t.Intent.ActorId == actor.Id)
                ? "Cannot rest while busy, already resting or full" : null;
        if (state.Unconscious || state.Resting) return "Actor resting or unconscious";
        return state.Current < StaminaCalculator.Cost(i.Kind, rules) ? "Insufficient stamina; rest first" : null;
    }
    internal static void Start(WorldState w, ActionIntent i)
    {
        if (i.Kind == ActionKind.Rest && w.StaminaRules is null && TimeActions.NeedsApRest(w, i.ActorId)) {
            w.Time!.ApRestingActors.Add(i.ActorId); ActionExecutor.Emit(w, i.Id, "RestStarted", i.ActorId); return;
        }
        if (w.StaminaRules is not { } rules) return;
        int index = w.Characters.FindIndex(c => c.Id == i.ActorId);
        var actor = w.Characters[index]; var state = actor.Stamina!;
        if (i.Kind == ActionKind.Rest)
        {
            w.Characters[index] = actor with { Stamina = state with { Resting = true } };
            ActionExecutor.Emit(w, i.Id, "RestStarted", actor.Id);
            return;
        }
    }
    internal static void Complete(WorldState w, ActionIntent i)
    {
        if (w.StaminaRules is not { } rules || w.Events.Any(e => e.ActionId == i.Id && e.Kind == "StaminaSpent")) return;
        int index = w.Characters.FindIndex(c => c.Id == i.ActorId);
        var actor = w.Characters[index]; var state = actor.Stamina!;
        int cost = StaminaCalculator.Cost(i.Kind, rules);
        if (cost == 0) return;
        int remaining = state.Current - cost;
        w.Characters[index] = actor with { Stamina = state with { Current = remaining, Unconscious = remaining == 0, Resting = remaining == 0 } };
        ActionExecutor.Emit(w, i.Id, "StaminaSpent", $"{actor.Id}:{cost}:{remaining}");
    }
    internal static void Advance(WorldState w, Func<CharacterState, bool>? canRecover = null)
    {
        if (w.StaminaRules is not { } rules) return;
        for (int n = 0; n < w.Characters.Count; n++)
        {
            var actor = w.Characters[n]; var state = actor.Stamina!;
            if (canRecover is not null && !canRecover(actor)) continue;
            if (!state.Resting || actor.Health == 0 || CombatActions.Active(w, actor.Id) is not null || BoardBattleActions.Participant(w, actor.Id)) continue;
            int value = Math.Min(rules.Maximum, state.Current + StaminaCalculator.Recovery(actor, rules));
            bool unconscious = state.Unconscious && value <= rules.FatigueThreshold;
            int health = Math.Min(100, actor.Health + rules.HealthRecoveryPerTick);
            bool resting = rules.HealthRecoveryPerTick > 0 ? unconscious || value < rules.Maximum || health < 100
                : state.Unconscious ? unconscious : value < rules.Maximum;
            resting |= TimeActions.NeedsApRest(w, actor.Id);
            w.Characters[n] = actor with { Health = health, Stamina = state with { Current = value, Unconscious = unconscious, Resting = resting } };
            if (health != actor.Health) ActionExecutor.Emit(w, "stamina:" + actor.Id, "RestHealthRecovered", $"{actor.Id}:{actor.Health}->{health}");
            ActionExecutor.Emit(w, "stamina:" + actor.Id, "RestRecovered", $"{actor.Id}:{state.Current}->{value}");
        }
    }
    internal static void ValidateWorld(WorldState w)
    {
        if (w.StaminaRules is not { } r)
        {
            if (w.Characters.Any(c => c.Stamina is not null)) throw new InvalidDataException("Stamina requires rules");
            return;
        }
        if (r.HealthRecoveryPerTick is < 0 or > 100 || r.Maximum != 100 || r.FatigueThreshold <= 0 || r.FatigueThreshold >= r.Maximum || r.ActivityCost <= 0 || r.ActivityCost >= r.Maximum ||
            !double.IsFinite(r.FatigueMultiplier) || r.FatigueMultiplier is < 0 or > 1 ||
            new[] { r.OutdoorRecovery, r.OrdinaryRecovery, r.HomeBedRecovery, r.UnconsciousRecovery }.Any(v => v <= 0 || v > r.Maximum) ||
            w.Characters.Any(c => c.Stamina is not { } s || s.Current < 0 || s.Current > r.Maximum ||
                (s.Unconscious && (!s.Resting || s.Current > r.FatigueThreshold)) || (s.Current == 0 && !s.Unconscious)))
            throw new InvalidDataException("Invalid stamina state or rules");
    }
}




