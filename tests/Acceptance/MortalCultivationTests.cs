using Cultivation.Domain;
using Cultivation.Simulation;

internal static class MortalCultivationTests
{
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    private static void Reject(Action action, string? field = null)
    {
        try { action(); }
        catch (InvalidDataException e) { Check(field is null || e.Message.Contains(field), "Missing field diagnostic"); return; }
        throw new Exception("Invalid mortal state/rules accepted");
    }
    private static MortalTrainingRules Rules => MortalCultivationConfiguration.Read(GameConfiguration.Find()).Training;
    private static WorldState World()
    {
        var w = DemoWorld.Create();
        var r = MortalCultivationConfiguration.Read(GameConfiguration.Find());
        w.MortalTrainingRules = r.Training; w.QiAwakeningRules = r.Awakening;
        w.Characters = w.Characters.Select(c => c with { MortalCultivation = new() }).ToList();
        return w;
    }

    public static IEnumerable<(string Name, Action Body)> Cases => new (string, Action)[]
    {
        ("Mortal configured rules and independent layer thresholds", () =>
        {
            var r = Rules;
            Check(r.StaminaCostPerAction == 1 && r.ExperiencePerAction == 1 && r.FreePointsPerLevel == 0, "Confirmed values missing");
            foreach (var (level, expected) in new[] { (0L,100L),(9L,100L),(10L,120L),(19L,120L),(20L,144L),(30L,173L) })
                Check(MortalCultivationCalculator.Threshold(level, r) == expected, "Layer threshold wrong");
            Check(MortalCultivationCalculator.Threshold(10, r with { BaseExperiencePerLevel = 200 }) == 240, "Config ignored");
        }),
        ("Mortal target growth crosses layers without duplicated points", () =>
        {
            var r = Rules;
            var s = MortalCultivationCalculator.GainAttributeExperience(new(), MortalAttribute.Intelligence, 990, r);
            Check(s.SpiritLevel == 9 && s.Experience[MortalAttribute.Intelligence] == 90, "Pre-layer state wrong");
            var crossed = MortalCultivationCalculator.GainAttributeExperience(s, MortalAttribute.Intelligence, 20, r);
            Check(crossed.SpiritLevel == 10 && crossed.BodyLevel == 0 && crossed.QiLevel == 0 &&
                crossed.AttributeGrowth[MortalAttribute.Intelligence] == 10 && crossed.Experience[MortalAttribute.Intelligence] == 10, "Cross-layer reward wrong");
            Check(s.SpiritLevel == 9 && s.AttributeGrowth[MortalAttribute.Intelligence] == 9, "Pure calculator mutated input");
            var continued = MortalCultivationCalculator.GainAttributeExperience(crossed, MortalAttribute.Strength, 100, r);
            Check(continued.BodyLevel == 1 && continued.SpiritLevel == 10 && continued.AttributeGrowth[MortalAttribute.Strength] == 1, "Aspects mixed");
        }),
        ("Mortal missing and illegal config fields rejected with paths", () =>
        {
            var rules = Rules;
            Reject(() => MortalCultivationConfiguration.ValidateTraining(rules with { LevelsPerLayer = 0 }), "LevelsPerLayer");
            Reject(() => MortalCultivationConfiguration.ValidateTraining(rules with { ExperiencePerAction = 0 }), "ExperiencePerAction");
            Reject(() => MortalCultivationConfiguration.ValidateTraining(rules with { FreePointsPerLevel = 1 }), "FreePointsPerLevel");
            var qi = MortalCultivationConfiguration.Read(GameConfiguration.Find()).Awakening;
            Reject(() => MortalCultivationConfiguration.ValidateAwakening(qi with { SuccessProbabilityPerCompletedMeditation = 1.01m }), "SuccessProbability");
            Reject(() => MortalCultivationConfiguration.ValidateAwakening(qi with { PityEnabled = true }), "PityEnabled");
            string folder = Path.Combine(Path.GetTempPath(), "mortal-config-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(folder, "cultivation"));
            try
            {
                File.WriteAllText(Path.Combine(folder, "cultivation", "mortal-training.json"), "{}");
                Reject(() => MortalCultivationConfiguration.Read(new(folder)), "StaminaCostPerAction");
                File.WriteAllText(Path.Combine(folder, "cultivation", "mortal-training.json"), "{\"Unexpected\":1}");
                Reject(() => MortalCultivationConfiguration.Read(new(folder)), "mortal-training.json");
                File.Copy(Path.Combine(GameConfiguration.Find().DirectoryPath, "cultivation", "mortal-training.json"), Path.Combine(folder, "cultivation", "mortal-training.json"), true);
                File.WriteAllText(Path.Combine(folder, "cultivation", "qi-awakening.json"), "{}");
                Reject(() => MortalCultivationConfiguration.Read(new(folder)), "qi-awakening.json");
            }
            finally { Directory.Delete(folder, true); }
        }),
        ("Mortal snapshot isolation and legacy mode preservation", () =>
        {
            var legacy = DemoWorld.Create();
            var old = WorldCodec.Clone(legacy);
            Check(old.MortalTrainingRules is null && old.Characters.All(c => c.MortalCultivation is null), "Legacy mode changed");
            var w = World();
            var actor = w.Characters[0];
            w.Characters[0] = actor with { MortalCultivation = MortalCultivationCalculator.GainAttributeExperience(actor.MortalCultivation!, MortalAttribute.Intelligence, 123, w.MortalTrainingRules!) };
            var clone = WorldCodec.Clone(w);
            WorldIntegrity.Validate(clone);
            Check(clone.Characters[0].MortalCultivation!.Experience[MortalAttribute.Intelligence] == 23, "Experience lost");
            w.MortalTrainingRules = w.MortalTrainingRules! with { BaseExperiencePerLevel = 200 };
            w.Characters[0].MortalCultivation!.Experience[MortalAttribute.Intelligence] = 99;
            Check(clone.MortalTrainingRules!.BaseExperiencePerLevel == 100 && clone.Characters[0].MortalCultivation!.Experience[MortalAttribute.Intelligence] == 23, "Snapshot shares mutable state");
        }),
        ("Mortal integrity rejects orphan growth and hostile overflow", () =>
        {
            var w = World();
            w.Characters[0] = w.Characters[0] with { MortalCultivation = new() { SpiritLevel = 1 } };
            Reject(() => WorldIntegrity.Validate(w));
            w = World(); w.MortalTrainingRules = null;
            Reject(() => WorldIntegrity.Validate(w));
            w = World();
            w.Characters[0] = w.Characters[0] with { MortalCultivation = new() { SpiritLevel = long.MaxValue } };
            Reject(() => WorldIntegrity.Validate(w));
            w = World();
            w.Characters[0] = w.Characters[0] with { MortalCultivation = new() { Experience = new() { [MortalAttribute.Strength] = -1 } } };
            Reject(() => WorldIntegrity.Validate(w));
        })
    };
}
