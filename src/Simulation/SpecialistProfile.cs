using Cultivation.Domain;
using Cultivation.AI;

namespace Cultivation.Simulation;

internal static class SpecialistProfile
{
    internal static void ApplyFresh(WorldState w, GameConfiguration config)
    {
        var rules = config.Read<SpecialistRules>("characters/specialists.json");
        w.SpecialistRules = rules;
        Validate(w);
        w.Aspirations.RemoveAll(a => a.Id == Aspiration.Alchemy);
        w.Characters = w.Characters.Select(a => a with {
            Aspiration = a.Aspiration == Aspiration.Alchemy ? Aspiration.Balanced : a.Aspiration,
            Identity = a.Identity == CharacterIdentity.Alchemist ? CharacterIdentity.Wanderer : a.Identity,
            Name = a.Id != w.ControlledCharacterId && (a.Aspiration == Aspiration.Alchemy || a.Identity == CharacterIdentity.Alchemist)
                ? "散修（" + a.Id + "）" : a.Name }).ToList();
        foreach (var definition in rules.Characters)
        {
            int n = w.Characters.FindIndex(a => a.Id == definition.ActorId && a.Id != w.ControlledCharacterId && a.MonsterKind == MonsterKind.None);
            if (n < 0) continue; // Small historical demos do not synthesize extra people or alter population size.
            var a = w.Characters[n];
            var tile = w.HexMap?.Tiles.SingleOrDefault(t => t.PlaceId == definition.LocationId);
            w.Characters[n] = a with { Name = definition.Name, TestProfession = definition.Profession,
                Aspiration = definition.Profession is TestProfession.SpiritTraining or TestProfession.BodyTraining or TestProfession.QiTraining ? Aspiration.Cultivation : Aspiration.Balanced,
                DemoLocationId = definition.LocationId, HexPosition = tile?.Coord ?? a.HexPosition };
            foreach (var stock in definition.InitialStock)
                w.Ledger.Add(new(w.NextSequence++, 0, $"specialist-genesis:{a.Id}:{stock.Key}", "$genesis", a.Id, stock.Key, stock.Value, "Genesis"));
        }
    }
    internal static void Validate(WorldState w)
    {
        if (w.Characters.Any(a => !Enum.IsDefined(a.TestProfession))) throw new InvalidDataException("Invalid test profession");
        if (w.SpecialistRules is not { } r) return;
        if (!double.IsFinite(r.WorkUtility) || r.WorkUtility <= 0 || !double.IsFinite(r.SupplyUtility) || r.SupplyUtility <= 0 || r.MedicineStockTarget < 1 ||
            r.Characters is null || r.Characters.Select(c => c.ActorId).Distinct().Count() != r.Characters.Count ||
            r.Characters.Any(c => string.IsNullOrWhiteSpace(c.ActorId) || string.IsNullOrWhiteSpace(c.Name) || !Enum.IsDefined(c.Profession) || c.Profession == TestProfession.None ||
                string.IsNullOrWhiteSpace(c.LocationId) || c.InitialStock is null || c.InitialStock.Any(p => p.Value <= 0 || !w.Items.Any(i => i.Id == p.Key))))
            throw new InvalidDataException("characters/specialists.json: invalid roles, stock or utility");
    }
}

internal static class SpecialistDecisions
{
    internal static IEnumerable<ScoredAction> Evaluate(WorldState w, CharacterState a, DecisionSource source)
    {
        if (w.SpecialistRules is not { } rules || a.TestProfession == TestProfession.None) yield break;
        if (a.TestProfession is TestProfession.SpiritTraining or TestProfession.BodyTraining or TestProfession.QiTraining)
        {
            foreach (var candidate in MortalTrainingActions.Evaluate(w, a, source))
            {
                bool match = a.TestProfession switch {
                    TestProfession.QiTraining => candidate.Intent.Kind == ActionKind.MeditateQi,
                    TestProfession.SpiritTraining => candidate.Intent.Kind == ActionKind.TrainMortalAttribute && MortalCultivationCalculator.Aspect(Enum.Parse<MortalAttribute>(MethodTraining.Target(w, candidate.Intent))) == CultivationAspect.Spirit,
                    TestProfession.BodyTraining => candidate.Intent.Kind == ActionKind.TrainMortalAttribute && MortalCultivationCalculator.Aspect(Enum.Parse<MortalAttribute>(MethodTraining.Target(w, candidate.Intent))) == CultivationAspect.Body,
                    _ => false };
                if (match) yield return candidate with { Score = rules.WorkUtility + candidate.Score, Reason = "專職修練：" + candidate.Reason };
            }
        }
        // Survival remains shared; professions do not permit ignoring injury or rest.
        foreach (var candidate in LifeDecisions.Evaluate(w, a, source).Where(c => c.Intent.TargetId == a.Id && c.Intent.Kind is ActionKind.MedicalTreatment or ActionKind.TreatSevereInjury or ActionKind.UseMedicine or ActionKind.EquipCraftedItem))
            yield return candidate;
    }
}
