using Cultivation.Domain;
using Cultivation.AI;

namespace Cultivation.Simulation;

public static class MethodTraining
{
    public static bool IsMethod(ActionIntent i) => i.ResourceId.StartsWith("method:", StringComparison.Ordinal);
    public static MethodSkillDefinition? Skill(WorldState w, ActionIntent i) => IsMethod(i) ?
        w.Methods?.Catalog.Methods.SelectMany(m => m.Skills).SingleOrDefault(s => "method:" + s.Id == i.ResourceId) : null;
    public static string Target(WorldState w, ActionIntent i) => Skill(w, i) is { } s ? s.PreferredTrainingAttribute ?? "qi" : i.ResourceId;
    public static int Layer(WorldState w, string actorId, string skillId)
    {
        if (w.Methods is not { } state || !state.Learners.TryGetValue(actorId, out var learned) || !learned.TryGetValue(skillId, out var xp)) return 0;
        var method = state.Catalog.Methods.Single(m => m.Skills.Any(s => s.Id == skillId));
        var character = w.Characters.Single(a => a.Id == actorId);
        var actor = character.MortalCultivation!;
        long Value(MortalAttribute a) => state.Catalog.UseCurrentAttributesForUpgrade ? CurrentAttributeCalculator.Value(character, a) :
            checked(actor.BaseAttributes.GetValueOrDefault(a) + actor.AttributeGrowth.GetValueOrDefault(a));
        int level = MethodProficiencyCalculator.ProficiencyLayer(method, xp);
        foreach (var gate in method.AdditionalLayerRequirements.OrderBy(g => g.Layer))
            if (level >= gate.Layer && (Value(MortalAttribute.Strength) < gate.Strength || Value(MortalAttribute.Constitution) < gate.Constitution)) return gate.Layer - 1;
        return level;
    }
    internal static string? Validate(WorldState w, ActionIntent i)
    {
        if (w.Methods is not { } state || Skill(w, i) is not { } skill ||
            !state.Learners.TryGetValue(i.ActorId, out var learned) || !learned.ContainsKey(skill.Id)) return "Method skill not learned";
        if ((i.Kind == ActionKind.MeditateQi) != (skill.CultivationTarget == "QiOrAwakeningEntry")) return "Invalid method training kind";
        var method = state.Catalog.Methods.Single(m => m.Skills.Any(s => s.Id == skill.Id));
        var a = w.Characters.Single(a => a.Id == i.ActorId).MortalCultivation!;
        long Value(MortalAttribute t) => checked(a.BaseAttributes.GetValueOrDefault(t) + a.AttributeGrowth.GetValueOrDefault(t));
        if (method.EntryRequirements is { } r && (Value(MortalAttribute.Strength) < r.Strength || Value(MortalAttribute.Constitution) < r.Constitution)) return "Method entry attributes insufficient";
        return null;
    }
    internal static IEnumerable<ScoredAction> Evaluate(WorldState w, CharacterState a, DecisionSource source)
    {
        if (w.Methods is not { } state || !state.Learners.TryGetValue(a.Id, out var learned)) yield break;
        foreach (var method in state.Catalog.Methods)
        foreach (var skill in method.Skills.Where(s => learned.ContainsKey(s.Id)))
        {
            var kind = skill.CultivationTarget == "QiOrAwakeningEntry" ? ActionKind.MeditateQi : ActionKind.TrainMortalAttribute;
            var intent = new ActionIntent($"method:{a.Id}:{w.Clock.Tick}:{skill.Id}", a.Id, a.Id, "method:" + skill.Id, 1, 0, source, kind);
            // Reuse the established growth utility; no extra method preference bonus.
            double growth = skill.PreferredTrainingAttribute is { } target ? a.MortalCultivation!.AttributeGrowth.GetValueOrDefault(Enum.Parse<MortalAttribute>(target)) : a.MortalCultivation!.QiLevel;
            yield return new(intent, w.MortalActionRules!.AiTrainingUtility * a.GrowthWeight / (1 + growth), "功法修練：" + skill.Name + "，兼顧熟練與三功成長");
        }
    }
    internal static void ValidateWorld(WorldState w)
    {
        var work = w.MortalTrainingWork.Where(t => IsMethod(t.Intent)).ToArray();
        if (w.Methods is not { } state) {
            if (work.Length > 0) throw new InvalidDataException("Method work requires catalog");
            return;
        }
        CultivationMethodConfiguration.Validate(state.Catalog);
        foreach (var actor in w.Characters.Where(a => a.MortalCultivation is not null))
        {
            if (actor.CurrentAttributeModifiers is null || actor.CurrentAttributeModifiers.Any(p => string.IsNullOrWhiteSpace(p.Key) || p.Value is null || p.Value.Keys.Any(k => !Enum.IsDefined(k))))
                throw new InvalidDataException("Invalid current attribute modifier source");
            try { foreach (var attribute in Enum.GetValues<MortalAttribute>()) _ = CurrentAttributeCalculator.Value(actor, attribute); }
            catch (OverflowException e) { throw new InvalidDataException("Current attribute total overflow", e); }
        }
        if (w.MortalActionRules is null || w.Time is null || state.Catalog.TrainingRules.ActionPointCost > w.Time.Rules.DailyActionPoints ||
            state.Catalog.TrainingRules.StaminaCost > w.StaminaRules!.Maximum) throw new InvalidDataException("Method training requires mortal/time/stamina rules");
        var ids = state.Catalog.Methods.SelectMany(m => m.Skills).Select(s => s.Id).ToHashSet();
        if (state.Learners.Any(p => !w.Characters.Any(a => a.Id == p.Key && a.MortalCultivation is not null) || p.Value.Any(s => !ids.Contains(s.Key) || s.Value < 0)))
            throw new InvalidDataException("Invalid method learner/proficiency");
        if (state.TrainingFocusActors?.Any(id => !state.Learners.ContainsKey(id) || id == w.ControlledCharacterId) == true)
            throw new InvalidDataException("Invalid method training focus actor");
        if (work.Any(t => Validate(w, t.Intent) is not null || t.DueTick > w.Clock.Tick + state.Catalog.TrainingRules.DurationTicks))
            throw new InvalidDataException("Invalid method training work");
    }
}

public static class MethodTrainingScenario
{
    public static WorldState Create(string name = "行者", GameConfiguration? configuration = null)
    {
        var config = configuration ?? GameConfiguration.Find();
        var w = MortalCultivationScenario.Create(name, config);
        var npc = config.Read<MethodTestNpcRules>("cultivation/method-test-npc.json");
        var template = w.Characters.SingleOrDefault(a => a.Id == npc.TemplateActorId);
        if (template is null || string.IsNullOrWhiteSpace(npc.ActorId) || string.IsNullOrWhiteSpace(npc.Name) ||
            npc.InitialMoney < 0 || w.Characters.Any(a => a.Id == npc.ActorId))
            throw new InvalidDataException("cultivation/method-test-npc.json: invalid template/id/name/money");
        w.Characters.Add(template with { Id = npc.ActorId, Name = npc.Name, TestProfession = TestProfession.None, Position = OrganizationPosition.Member,
            MortalCultivation = template.MortalCultivation! with { Experience = [], AttributeGrowth = [] } });
        if (npc.InitialMoney > 0) w.Ledger.Add(new(w.NextSequence++, w.Clock.Tick, "method-npc-genesis:" + npc.ActorId,
            "$genesis", npc.ActorId, "spirit-stone", npc.InitialMoney, "Genesis"));
        var catalog = CultivationMethodConfiguration.Read(config);
        var setup = config.Read<MethodScenarioRules>("cultivation/method-scenario.json");
        if (setup.InitialProficiency < 0) throw new InvalidDataException("cultivation/method-scenario.json: InitialProficiency");
        var learners = new Dictionary<string, Dictionary<string, decimal>>();
        foreach (var a in w.Characters.Where(a => a.MortalCultivation is not null))
        {
            var ids = a.Id == w.ControlledCharacterId ? setup.PlayerMethods : setup.OrganizationMethods.GetValueOrDefault(a.OrganizationId ?? "", []);
            if (ids.Any(id => !catalog.Methods.Any(m => m.Id == id))) throw new InvalidDataException("cultivation/method-scenario.json: unknown method");
            if (ids.Length > 0) learners[a.Id] = catalog.Methods.Where(m => ids.Contains(m.Id)).SelectMany(m => m.Skills).ToDictionary(s => s.Id, _ => setup.InitialProficiency);
        }
        w.Methods = new(catalog, learners, [npc.ActorId]); WorldIntegrity.Validate(w); return w;
    }
}
