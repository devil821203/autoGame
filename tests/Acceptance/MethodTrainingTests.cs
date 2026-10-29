using Cultivation.Domain;
using Cultivation.Simulation;

internal static class MethodTrainingTests
{
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    private static ActionIntent Train(string id, string skill) => new(id, "player", "player", "method:" + skill, 1, 0, DecisionSource.Player,
        skill == "QingyunMind" ? ActionKind.MeditateQi : ActionKind.TrainMortalAttribute);
    public static IEnumerable<(string Name, Action Body)> Cases => new (string, Action)[] {
        ("Dedicated method NPC is added only to new method worlds and focus survives saves", () => {
            var w = MethodTrainingScenario.Create(); var npc = w.Characters.Single(a => a.Id == "npc:method-trainee");
            Check(npc.Name == "功法修練者01" && w.Methods!.TrainingFocusActors!.Contains(npc.Id) &&
                w.Methods.Learners[npc.Id].Values.All(x => x == 0), "Missing initial NPC or focus");
            var clone = WorldCodec.Clone(w); WorldIntegrity.Validate(clone);
            Check(clone.Methods!.TrainingFocusActors!.Contains(npc.Id) && !MortalCultivationScenario.Create().Characters.Any(a => a.Id == npc.Id), "Focus lost or old profile changed");
        }),
        ("Method training pays once and awards only its own skill and attribute", () => {
            var w = MethodTrainingScenario.Create(); var e = new SimulationEngine(w);
            e.Submit(Train("method-test", "MightyBrick")); Check(e.Step().Single().Accepted, "Rejected training");
            w = e.Snapshot(); var p = w.Characters.Single(a => a.Id == "player");
            Check(w.Methods!.Learners["player"]["MightyBrick"] == 1 && w.Methods.Learners["player"]["QingyunFist"] == 0 &&
                p.MortalCultivation!.Experience[MortalAttribute.Strength] == 1 && p.Stamina!.Current == 99, "Incorrect shared gains");
            Check(w.Time!.Charges.Single(c => c.ActorId == "player").Cost == 1, "Wrong AP");
            e.Submit(Train("method-test", "MightyBrick")); e.Step();
            Check(e.Snapshot().Methods!.Learners["player"]["MightyBrick"] == 1, "Duplicate gain");
        }),
        ("Method upgrades use current totals while historical snapshots keep permanent checks", () => {
            var w = MethodTrainingScenario.Create(); w.Methods!.Learners["player"]["QingyunFist"] = 300;
            Check(MethodTraining.Layer(w, "player", "QingyunFist") == 1, "Bypassed attribute gate");
            int n = w.Characters.FindIndex(a => a.Id == "player"); var a = w.Characters[n];
            w.Characters[n] = a with { CurrentAttributeModifiers = new() { ["test-active-effect"] = new() {
                [MortalAttribute.Strength] = 10, [MortalAttribute.Constitution] = 10 } } };
            Check(MethodTraining.Layer(w, "player", "QingyunFist") == 3, "Current bonuses ignored");
            var saved = WorldCodec.Clone(w); WorldIntegrity.Validate(saved);
            Check(MethodTraining.Layer(saved, "player", "QingyunFist") == 3, "Current totals lost in save");
            int savedPlayer = saved.Characters.FindIndex(c => c.Id == "player");
            var buffed = saved.Characters[savedPlayer];
            saved.Characters[savedPlayer] = buffed with { CurrentAttributeModifiers = [] };
            Check(MethodTraining.Layer(saved, "player", "QingyunFist") == 1 &&
                saved.Methods!.Learners["player"]["QingyunFist"] == 300, "Lost bonus must lower usable layer without losing proficiency");
            saved.Characters[savedPlayer] = buffed;
            Check(MethodTraining.Layer(saved, "player", "QingyunFist") == 3, "Restored bonus must restore usable layer");
            w.Methods = w.Methods with { Catalog = w.Methods.Catalog with { UseCurrentAttributesForUpgrade = false } };
            Check(MethodTraining.Layer(w, "player", "QingyunFist") == 1, "Historical policy overwritten");
            w.Methods = w.Methods with { Catalog = w.Methods.Catalog with { UseCurrentAttributesForUpgrade = true } };
            var basis = new Dictionary<MortalAttribute, int>(a.MortalCultivation!.BaseAttributes) { [MortalAttribute.Strength] = 20, [MortalAttribute.Constitution] = 20 };
            w.Characters[n] = a with { MortalCultivation = a.MortalCultivation with { BaseAttributes = basis } };
            Check(MethodTraining.Layer(w, "player", "QingyunFist") == 3, "Auto layer not updated");
        }),
        ("Method training snapshot continues identically and old worlds reject new skills", () => {
            var w = MethodTrainingScenario.Create(); var e = new SimulationEngine(w);
            e.Submit(Train("mind", "QingyunMind")); e.Step();
            var saved = WorldCodec.Clone(e.Snapshot()); var resumed = new SimulationEngine(saved);
            e.Submit(Train("mind2", "QingyunMind")); resumed.Submit(Train("mind2", "QingyunMind")); e.Step(); resumed.Step();
            Check(WorldCodec.Digest(e.Snapshot()) == WorldCodec.Digest(resumed.Snapshot()), "Save divergence");
            Check(e.Snapshot().Characters.Single(a => a.Id == "player").MortalCultivation!.QiInitiationExperience == 2, "Missing qi progress");
            var old = new SimulationEngine(MortalCultivationScenario.Create()); old.Submit(Train("no-method", "QingyunFist"));
            Check(!old.Step().Single().Accepted && old.Snapshot().Methods is null, "Old world enabled methods");
        }),
        ("Method fatigue keeps fractions and unknown skills are rejected", () => {
            var w = MethodTrainingScenario.Create(); int n = w.Characters.FindIndex(a => a.Id == "player");
            w.Characters[n] = w.Characters[n] with { Stamina = w.Characters[n].Stamina! with { Current = 20 } };
            var e = new SimulationEngine(w); e.Submit(Train("half", "QingyunFist")); e.Step();
            Check(e.Snapshot().Methods!.Learners["player"]["QingyunFist"] == .5m, "Fraction lost");
            e.Submit(Train("bad", "Unknown")); Check(!e.Step().Single().Accepted, "Unknown skill admitted");
        })
    };
}
