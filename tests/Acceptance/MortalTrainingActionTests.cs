using Cultivation.Domain;
using Cultivation.Simulation;

internal static class MortalTrainingActionTests
{
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    private static WorldState World()
    {
        var w = MortalCultivationScenario.Create(); w.PlayerAutoSchedule = false; return w;
    }
    private static CharacterState Player(WorldState w) => w.Characters.Single(c => c.Id == w.ControlledCharacterId);
    private static void Player(WorldState w, Func<CharacterState, CharacterState> change)
    { int n = w.Characters.FindIndex(c => c.Id == w.ControlledCharacterId); w.Characters[n] = change(w.Characters[n]); }
    private static ActionIntent Train(WorldState w, string id = "train", bool meditate = false) =>
        new(id, w.ControlledCharacterId, w.ControlledCharacterId, meditate ? "qi" : "Intelligence", 1, 0, DecisionSource.Player,
            meditate ? ActionKind.MeditateQi : ActionKind.TrainMortalAttribute);
    private static WorldState Run(WorldState w, string id = "train", bool meditate = false)
    { var e = new SimulationEngine(w); e.Submit(Train(w, id, meditate)); Check(e.Step().Single().Accepted, "Training rejected"); return e.Snapshot(); }
    public static IEnumerable<(string Name, Action Body)> Cases => new (string, Action)[]
    {
        ("Mortal actions pay AP once and award on completion", () =>
        {
            var w = Run(World()); var a = Player(w);
            Check(a.MortalCultivation!.Experience[MortalAttribute.Intelligence] == 1 && a.Stamina!.Current == 99, "1 stamina/XP missing");
            Check(w.Time!.Charges.Single(c => c.ActorId == a.Id).Cost == 1 && w.MortalTrainingWork.Count == 0, "AP/due state wrong");
            var e = new SimulationEngine(w); e.Submit(Train(w)); Check(!e.Step().Single().Accepted && Player(e.Snapshot()).Stamina!.Current == 99, "Duplicate reward");
            Check(a.MortalCultivation.BaseAttributes.Values.All(v => v == 10) && a.MortalCultivation.SpiritLevel == 0, "Initial values wrong");
        }),
        ("Mortal fractional fatigue persists and zero stamina rests next tick", () =>
        {
            var w = World(); Player(w, a => a with { Stamina = a.Stamina! with { Current = 2 } });
            w = Run(w); Check(Player(w).MortalCultivation!.Experience[MortalAttribute.Intelligence] == 0.5m, "Fraction rounded away");
            w = Run(WorldCodec.Clone(w), "train2"); var a = Player(w);
            Check(a.MortalCultivation!.Experience[MortalAttribute.Intelligence] == 1 && a.Stamina is { Current: 0, Unconscious: true }, "Fraction/stamina lost");
            var e = new SimulationEngine(w); e.Step();
            Check(Player(e.Snapshot()).Stamina!.Current == w.StaminaRules!.UnconsciousRecovery, "Not forced rest next tick");
        }),
        ("Mortal multi tick busy and cross day save continuation", () =>
        {
            var w = World(); w.MortalActionRules = w.MortalActionRules! with { DurationTicks = 3, ActionPoints = 3 };
            var e = new SimulationEngine(w); e.Submit(Train(w)); e.Step();
            Check(Player(e.Snapshot()).Stamina!.Current == 100 && e.Snapshot().MortalTrainingWork.Count == 1, "Early completion");
            var resumed = new SimulationEngine(WorldCodec.Clone(e.Snapshot()));
            e.Submit(Train(w, "busy")); Check(!e.Step().Single().Accepted, "Busy training allowed");
            resumed.Step(); resumed.Step(); e.Step();
            Check(Player(resumed.Snapshot()).MortalCultivation!.Experience[MortalAttribute.Intelligence] == 1 && Player(e.Snapshot()).Stamina!.Current == 99, "Resume lost reward");
            var daily = World(); var clock = new SimulationEngine(daily); for (int n = 0; n < 29; n++) clock.Step();
            daily = clock.Snapshot(); daily.MortalActionRules = daily.MortalActionRules! with { DurationTicks = 3, ActionPoints = 3 };
            clock = new(daily); clock.Submit(Train(daily)); clock.Step(); clock.Step(); clock.Step();
            Check(clock.Snapshot().Time!.Charges.Count(c => c.ActorId == daily.ControlledCharacterId) == 1 && Player(clock.Snapshot()).Stamina!.Current == 99, "Cross-day recharged");
        }),
        ("Mortal awakening starts after initiation and awards no extra level", () =>
        {
            var w = World(); w.QiAwakeningRules = w.QiAwakeningRules! with { SuccessProbabilityPerCompletedMeditation = 1 };
            Player(w, a => a with { MortalCultivation = a.MortalCultivation! with { QiInitiationExperience = 99 } });
            w = Run(w, meditate: true); var s = Player(w).MortalCultivation!;
            Check(s.Awakening == QiAwakeningStage.Eligible && s.AwakeningAttempts == 0, "Rolled on threshold completion");
            w = Run(w, "awaken", true); s = Player(w).MortalCultivation!;
            Check(s.Awakening == QiAwakeningStage.Awakened && s.AwakeningAttempts == 1 && s.QiLevel == 0 && s.QiExperience == 0 && s.AttributeGrowth.Count == 0, "Awakening double reward");
            w = Run(w, "qi-xp", true); Check(Player(w).MortalCultivation!.QiExperience == 1, "Awakened training missing");
        }),
        ("Mortal failed awakening retains eligibility and seeded qi growth repeats", () =>
        {
            var w = World(); w.QiAwakeningRules = w.QiAwakeningRules! with { SuccessProbabilityPerCompletedMeditation = 0 };
            Player(w, a => a with { MortalCultivation = a.MortalCultivation! with { QiInitiationExperience = 100, Awakening = QiAwakeningStage.Eligible } });
            w = Run(w, meditate: true); Check(Player(w).MortalCultivation!.Awakening == QiAwakeningStage.Eligible && Player(w).MortalCultivation!.QiInitiationExperience == 100, "Failure cleared progress");
            Player(w, a => a with { MortalCultivation = a.MortalCultivation! with { Awakening = QiAwakeningStage.Awakened, QiExperience = 99 } });
            var x = Run(w, "qi-level", true); var y = Run(WorldCodec.Clone(w), "qi-level", true);
            Check(WorldCodec.Digest(x) == WorldCodec.Digest(y), "Seeded continuation diverged");
            var s = Player(x).MortalCultivation!;
            Check(s.QiLevel == 1 && s.AttributeGrowth.Values.Sum() == 1 && s.AttributeGrowth.Keys.All(k => MortalCultivationCalculator.Aspect(k) == CultivationAspect.Qi), "Qi level wrong");
        }),
        ("Mortal shared validation rejects injured target and spoofed actor", () =>
        {
            var w = World(); Player(w, a => a with { SeverelyInjured = true });
            Check(ActionValidator.Validate(w, Train(w)) is not null, "Severe injury permitted");
            w = World(); Check(ActionValidator.Validate(w, Train(w) with { ResourceId = "Luck" }) is not null, "Direct qi attribute training");
            Check(ActionValidator.Validate(w, Train(w) with { ActorId = w.Characters.First(c => c.Id != w.ControlledCharacterId).Id }) is not null, "Player controls NPC");
            w.MortalActionRules = w.MortalActionRules! with { ActionPoints = 0 };
            bool rejected = false; try { WorldIntegrity.Validate(w); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, "Invalid AP accepted");
        }),
        ("Mortal attacked training is interrupted without stamina reward or AP refund", () =>
        {
            var w = World(); w.MortalActionRules = w.MortalActionRules! with { DurationTicks = 3, ActionPoints = 3 };
            string enemy = w.Characters.First(c => c.Id != w.ControlledCharacterId && c.MonsterKind == MonsterKind.None).Id;
            var player = Player(w);
            w.Characters = w.Characters.Select(c => c.Id == player.Id ? c with { Hostile = true } :
                c.Id == enemy ? c with { DemoLocationId = player.DemoLocationId, HexPosition = player.HexPosition, RegionId = player.RegionId } : c).ToList();
            var e = new SimulationEngine(w); e.Submit(Train(w)); e.Step();
            e.Submit(new("assault", enemy, player.Id, "combat", 1, 0, DecisionSource.Npc, ActionKind.Engage));
            Check(e.Step().Single().Accepted, "Attack on training defender rejected");
            var final = e.Snapshot();
            Check(final.MortalTrainingWork.Count == 0 && Player(final).Stamina!.Current == 100 && Player(final).MortalCultivation!.Experience.Count == 0, "Interrupted reward paid");
            Check(final.Time!.Charges.Single(c => c.ActorId == player.Id).Cost == 3 && final.Events.Any(ev => ev.Kind == "MortalTrainingInterrupted"), "AP refunded or interruption unlogged");
        }),
        ("Mortal player battle freezes training and catchup completes it once", () =>
        {
            var w = World(); w.MortalActionRules = w.MortalActionRules! with { DurationTicks = 3, ActionPoints = 3 };
            var others = w.Characters.Where(c => c.Id != w.ControlledCharacterId && c.MonsterKind == MonsterKind.None).Take(2).ToArray();
            var player = Player(w); string enemy = others[0].Id, student = others[1].Id;
            w.Characters = w.Characters.Select(c => c.Id == player.Id ? c with { BaseAttack = 100 } :
                c.Id == enemy ? c with { Hostile = true, Health = 40, DemoLocationId = player.DemoLocationId, HexPosition = player.HexPosition, RegionId = player.RegionId } : c).ToList();
            var e = new SimulationEngine(w);
            e.Submit(new("student-training", student, student, "Strength", 1, 0, DecisionSource.Npc, ActionKind.TrainMortalAttribute));
            e.Submit(new("player-battle", player.Id, enemy, "combat", 1, 0, DecisionSource.Player, ActionKind.Engage));
            Check(e.Step().All(r => r.Accepted), "Battle/training commands rejected");
            w = e.Snapshot(); Check(w.Clock.Tick == 0 && w.MortalTrainingWork.Count == 1, "World training not frozen");
            w.BoardBattle!.PlayerAutoCombat = true; e = new(w);
            for (int n = 0; n < 30 && e.Snapshot().BoardBattle!.Phase == BoardBattlePhase.Active; n++) e.Step(true);
            w = e.Snapshot();
            Check(w.BoardBattle!.Phase == BoardBattlePhase.Finished && w.Clock.Tick == w.Time!.Rules.BattleActionPoints, "Battle catchup clock wrong");
            Check(w.Events.Count(ev => ev.ActionId == "student-training" && ev.Kind == "MortalTrainingCompleted") == 1 &&
                w.Characters.Single(c => c.Id == student).MortalCultivation!.Experience[MortalAttribute.Strength] >= 1, "Training catchup missing/duplicated");
        }),
        ("Mortal independent targets keep leftovers and configured multi level qi gains", () =>
        {
            var w = World(); var r = w.MortalTrainingRules!;
            var s = MortalCultivationCalculator.GainAttributeExperience(new(), MortalAttribute.Intelligence, 99, r);
            s = MortalCultivationCalculator.GainAttributeExperience(s, MortalAttribute.Willpower, 100, r);
            Check(s.SpiritLevel == 1 && s.Experience[MortalAttribute.Intelligence] == 99 && s.AttributeGrowth.GetValueOrDefault(MortalAttribute.Intelligence) == 0, "Target experience mixed");
            Player(w, a => a with { MortalCultivation = a.MortalCultivation! with { Awakening = QiAwakeningStage.Awakened, QiInitiationExperience = 100 } });
            w.MortalTrainingRules = r with { ExperiencePerAction = 2200 };
            w = Run(w, meditate: true); s = Player(w).MortalCultivation!;
            Check(s.QiLevel == 20 && s.AttributeGrowth.Values.Sum() == 20 && s.QiExperience == 0, "Multi level qi point accounting wrong");
        }),
        ("Mortal autonomous growth and new save preserve mode", () =>
        {
            var w = MortalCultivationScenario.Create(); var e = new SimulationEngine(w);
            for (int n = 0; n < 120; n++) e.Step(true);
            var final = e.Snapshot();
            Check(final.Clock.Tick > 67, "Simulation did not pass reported tick 67");
            Check(final.PartyLoot.Any(l => l.ResourceId == final.RecoveryResourceId), "Regression did not exercise party medicine loot");
            Console.WriteLine($"MORTAL_LONG_RUN Tick={final.Clock.Tick} PartyMedicineLoot={final.PartyLoot.Count(l => l.ResourceId == final.RecoveryResourceId)} FirstMedicineLootTick={final.PartyLoot.First(l => l.ResourceId == final.RecoveryResourceId).Tick}");
            Check(final.Events.Any(ev => ev.Kind == "MortalTrainingCompleted" && ev.Detail.StartsWith(w.ControlledCharacterId + ":")), "Player did not auto train");
            Check(final.Events.Any(ev => ev.Kind == "MortalTrainingCompleted" && !ev.Detail.StartsWith(w.ControlledCharacterId + ":")), "NPC did not auto train");
            Check(final.Time!.Charges.Any(c => c.Day == 1), "AP day did not progress");
            var fresh = NewGameFactory.CreateLike(final, "新凡人");
            Check(fresh.MortalActionRules is not null && fresh.Characters.Where(c => c.MortalCultivation is not null).All(c => c.MortalCultivation!.AttributeGrowth.Count == 0), "New save reset/mode failed");
        })
    };
}
