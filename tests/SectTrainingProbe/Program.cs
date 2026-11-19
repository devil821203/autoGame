using System.Text.Json;
using Cultivation.Domain;
using Cultivation.Simulation;

string output = args.Length > 0 ? args[0] : throw new ArgumentException("Output JSON required");
if (File.Exists(output)) throw new IOException("Refusing to overwrite existing evidence");
bool methods = args.Contains("--methods");
bool playerMode = args.Contains("--player");
int daysIndex = Array.IndexOf(args, "--days");
int days = daysIndex >= 0 && daysIndex + 1 < args.Length ? int.Parse(args[daysIndex + 1]) : 30;
if (days <= 0) throw new ArgumentException("Days must be positive");
var initial = playerMode ? TestScenarioCatalog.Create("methods") : methods ? MethodTrainingScenario.Create() : MortalCultivationScenario.Create();
if (playerMode) initial.PlayerAutoSchedule = true;
var ids = initial.Characters.Where(a => playerMode ? a.Id == initial.ControlledCharacterId : a.OrganizationId == "sect" && a.Id != initial.ControlledCharacterId).Select(a => a.Id).ToHashSet();
var engine = new SimulationEngine(initial);
object Sample(WorldState w) => new { w.Clock.Tick, Members = w.Characters.Where(a => ids.Contains(a.Id)).Select(a => new {
    a.Id, a.Name, a.OrganizationId, Profession = a.TestProfession.ToString(), a.Health, a.SeverelyInjured,
    Stamina = a.Stamina?.Current, Money = Inventory.Balance(w, a.Id, "spirit-stone"),
    ActionPoints = w.Time?.Budgets.SingleOrDefault(b => b.ActorId == a.Id)?.Remaining,
    ApRestRecoveries = w.Events.Count(e => e.Kind == "RestActionPointsRecovered" && e.Detail.StartsWith(a.Id + ":", StringComparison.Ordinal)),
    Cultivation = a.MortalCultivation,
    MethodSkills = w.Methods?.Learners.GetValueOrDefault(a.Id)?.ToDictionary(p => p.Key, p => new { Proficiency = p.Value, Layer = MethodTraining.Layer(w, a.Id, p.Key) }),
    CompletedMethodTraining = w.Events.Count(e => e.Kind == "MethodTrainingCompleted" && e.Detail.StartsWith(a.Id + ":", StringComparison.Ordinal)),
    Attributes = a.MortalCultivation?.BaseAttributes.ToDictionary(p => p.Key.ToString(), p => (long)p.Value + a.MortalCultivation.AttributeGrowth.GetValueOrDefault(p.Key)),
    CompletedTraining = w.Events.Where(e => e.Kind == "MortalTrainingCompleted" && e.Detail.StartsWith(a.Id + ":", StringComparison.Ordinal)).Count(),
    TrainingTargets = w.Events.Where(e => e.Kind == "MortalTrainingCompleted" && e.Detail.StartsWith(a.Id + ":", StringComparison.Ordinal))
        .GroupBy(e => e.Detail[(a.Id.Length + 1)..].Split(':')[0]).ToDictionary(g => g.Key, g => g.Count())
}).ToArray() };
var samples = new List<object> { Sample(initial) };
long target = checked(initial.Time!.Rules.TicksPerDay * days);
string? failure = null;
try {
    while (engine.Snapshot().Clock.Tick < target) {
        var w = engine.Snapshot(); long before = w.Clock.Tick;
        if (w.BoardBattle is { Phase: BoardBattlePhase.Active, PlayerAutoCombat: false } b && b.Fighters.Any(f => f.ActorId == w.ControlledCharacterId)) {
            engine.Submit(new("probe-auto:" + b.Id, w.ControlledCharacterId, b.Id, "auto-combat", 1, 0, DecisionSource.Player, ActionKind.SetBoardAutoCombat));
            engine.Step(true);
        }
        engine.StepValidatedBatch(target, initial.Time.Rules.TicksPerDay, validateEveryTick: true);
        w = engine.Snapshot();
        if (w.Clock.Tick == before && w.BoardBattle is not { Phase: BoardBattlePhase.Active }) throw new Exception("No progress");
        if (w.Clock.Tick % initial.Time.Rules.TicksPerDay == 0) {
            samples.Add(Sample(w)); Console.WriteLine($"DAY {w.Clock.Tick / initial.Time.Rules.TicksPerDay} Tick {w.Clock.Tick}");
        }
    }
} catch (Exception e) { failure = e.ToString(); }
var final = engine.Snapshot();
File.WriteAllText(output, JsonSerializer.Serialize(new {
    Scenario = playerMode ? "Current methods scenario; player autonomy enabled" : methods ? "MethodTrainingScenario" : "MortalCultivationScenario", DiagnosticBuild = "Standard project references using Domain.csproj local build selection",
    RequestedDays = days, TargetTick = target, CompletedTick = final.Clock.Tick, Failure = failure,
    Validation = "Full world integrity every tick; no forced NPC actions or resource changes; only existing player battle auto toggle",
    Initial = Sample(initial), Final = Sample(final), Samples = samples,
    TrainingEvents = final.Events.Where(e => e.Kind is "MortalTrainingCompleted" or "MortalTrainingInterrupted" && ids.Any(id => e.Detail.StartsWith(id + ":", StringComparison.Ordinal))).ToArray()
}, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"DONE Tick={final.Clock.Tick} Members={ids.Count} Failure={failure}");
return failure is null ? 0 : 1;
