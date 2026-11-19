using System.Diagnostics;
using System.Text.Json;
using Cultivation.Domain;
using Cultivation.Simulation;
using Cultivation.Persistence;

namespace Cultivation.Headless;

public static class SpecialistLongRunReport
{
    public sealed record Settings(int Years, ulong Seed, int ProgressEveryTicks, int SamplesPerYear, int BatchSteps, bool ValidateEveryTick, PerformanceRules Performance);
    public static void Run(string output, long? tickLimit = null, string? resumePath = null)
    {
        var config = GameConfiguration.Find(); var settings = config.Read<Settings>("testing/specialists-long-run.json");
        if (settings.Years <= 0 || settings.ProgressEveryTicks <= 0 || settings.SamplesPerYear <= 0 || settings.BatchSteps <= 0 ||
            settings.Performance.NearbyIntervalTicks != 1 || settings.Performance.RegionalIntervalTicks != 1 || settings.Performance.RemoteIntervalTicks != 1) throw new ArgumentException("Invalid long run settings or changed AI frequency");
        var initial = MortalCultivationScenario.Create(configuration: config); initial.RandomState = settings.Seed;
        initial.Performance = settings.Performance;
        long yearTicks = checked(initial.Time!.Rules.TicksPerDay * initial.Time.Rules.DaysPerSeason * 4L);
        long requested = checked(yearTicks * settings.Years), target = tickLimit ?? requested;
        if (target <= 0 || target > requested) throw new ArgumentOutOfRangeException(nameof(tickLimit));
        string root = Path.GetFullPath(output); Directory.CreateDirectory(root);
        if (File.Exists(Path.Combine(root, "report.json"))) throw new IOException("Report already exists; select another output directory");
        var samples = new List<object>(); var timer = Stopwatch.StartNew();
        var resumed = resumePath is null ? initial : new JsonWorldStore().Load(resumePath);
        if (resumed.Clock.Tick >= target || resumed.MortalActionRules is null) throw new ArgumentException("Checkpoint is not before the target or not a mortal world");
        var engine = new SimulationEngine(resumed);
        var lastTraining = initial.Characters.Where(a=>a.TestProfession!=TestProfession.None).ToDictionary(a=>a.Id,_=>-1L);
        using var archive = new StreamWriter(Path.Combine(root,"archived-events.jsonl"),append:true);
        long archivedEvents=0;
        long tick = resumed.Clock.Tick, steps = 0; string? failure = null;
        object Sample(WorldState w) => new { Tick = w.Clock.Tick, ElapsedYears = w.Clock.Tick / (decimal)yearTicks,
            Date = CalendarCalculator.Date(w.Clock.Tick, w.Time!.Rules), SectStones = Inventory.Balance(w, "sect", "spirit-stone"),
            ExternalTestFunding = w.Ledger.Where(e => e.From == "$test-procurement").Sum(e => e.Quantity),
            Characters = w.Characters.Where(a => a.TestProfession != TestProfession.None).Select(a => {
                var s = a.MortalCultivation!; var r = w.MortalTrainingRules!;
                decimal spent = 0;
                foreach (var aspect in Enum.GetValues<CultivationAspect>())
                    for (long level = 0; level < MortalCultivationCalculator.Level(s, aspect); level++) spent += MortalCultivationCalculator.Threshold(level, r);
                var services = w.PaidServiceReceipts.Where(x => x.Contract.ProviderId == a.Id).ToArray();
                return new { a.Id, a.Name, Profession = a.TestProfession.ToString(), a.Health, a.SeverelyInjured, a.OrganizationId,
                    Stamina = a.Stamina?.Current, Stones = Inventory.Balance(w,a.Id,"spirit-stone"),
                    Items = new[] {"herb","ointment","bleeding-powder","iron-material","beast-hide","contribution"}.ToDictionary(i=>i,i=>Inventory.Balance(w,a.Id,i)),
                    CashIn = w.Ledger.Where(e=>e.ResourceId=="spirit-stone"&&e.To==a.Id&&e.From!="$genesis").Sum(e=>e.Quantity),
                    CashOut = w.Ledger.Where(e=>e.ResourceId=="spirit-stone"&&e.From==a.Id).Sum(e=>e.Quantity),
                    ServiceRevenue = services.Where(x=>x.Success).Sum(x=>x.Contract.Fee),
                    ServicesCompleted = services.Count(x=>x.Success), ServicesRefunded = services.Count(x=>!x.Success),
                    LastServiceTick = services.Where(x=>x.Success).Select(x=>x.Tick).DefaultIfEmpty(-1).Max(),
                    ClientServiceSpending = w.PaidServiceReceipts.Where(x=>x.Success&&x.Contract.ClientId==a.Id).Sum(x=>x.Contract.Fee),
                    a.LifeSkills, s.SpiritLevel, s.BodyLevel, s.QiLevel, Awakening = s.Awakening.ToString(), s.AwakeningAttempts,
                    s.QiInitiationExperience, s.QiExperience, AttributeExperience = s.Experience.ToDictionary(p=>p.Key.ToString(),p=>p.Value),
                    AttributeGrowth = s.AttributeGrowth.ToDictionary(p=>p.Key.ToString(),p=>p.Value),
                    EarnedTrainingExperience = spent+s.Experience.Values.Sum()+s.QiExperience+s.QiInitiationExperience,
                    LastTrainingTick = Math.Max(lastTraining[a.Id],w.Events.Where(e=>e.Kind=="MortalTrainingCompleted"&&e.Detail.StartsWith(a.Id+":",StringComparison.Ordinal)).Select(e=>e.Tick).DefaultIfEmpty(-1).Max())
                };
            }).ToArray(), LedgerRows = w.Ledger.Count, EventRows = w.Events.Count, ManagedMemoryBytes = GC.GetTotalMemory(false) };
        void Record()
        {
            var w=engine.Snapshot();var row=Sample(w);samples.Add(row);
            File.AppendAllText(Path.Combine(root,"samples.jsonl"),JsonSerializer.Serialize(row)+Environment.NewLine);
            if(w.Clock.Tick==0||w.Clock.Tick>=target)new JsonWorldStore().Save(Path.Combine(root,$"checkpoint-{w.Clock.Tick}.json"),w);
        }
        if(resumePath is not null)
        {
            var baseline=Sample(initial);samples.Add(baseline);File.AppendAllText(Path.Combine(root,"samples.jsonl"),JsonSerializer.Serialize(baseline)+Environment.NewLine);
            new JsonWorldStore().Save(Path.Combine(root,"checkpoint-0.json"),initial);
        }
        Record(); long sampleInterval=Math.Max(1,yearTicks/settings.SamplesPerYear);
        long nextSample=(tick/sampleInterval+1)*sampleInterval, nextProgress=tick+settings.ProgressEveryTicks;
        try
        {
            while(tick<target)
            {
                int advanced = engine.StepValidatedBatch(Math.Min(target, Math.Min(nextSample,nextProgress)),settings.BatchSteps,settings.ValidateEveryTick);
                steps+=advanced;long next=engine.LastDiagnostics?.Tick??tick;
                if(next==tick)
                {
                    var w=engine.Snapshot();
                    if(w.BoardBattle is {Phase:BoardBattlePhase.Active,PlayerAutoCombat:false} b && b.Fighters.Any(f=>f.ActorId==w.ControlledCharacterId))
                    {
                        engine.Submit(new("report-auto:"+b.Id,w.ControlledCharacterId,b.Id,"auto-combat",1,0,DecisionSource.Player,ActionKind.SetBoardAutoCombat));
                        engine.Step(true);steps++;next=engine.LastDiagnostics!.Tick;
                    }
                    else if(w.BoardBattle is not {Phase:BoardBattlePhase.Active})throw new InvalidOperationException("No world tick progress");
                }
                tick=next;
                foreach(var e in engine.ArchiveMeasurementEvents(tick-settings.Performance.DiagnosticRetentionTicks))
                {
                    archive.WriteLine(JsonSerializer.Serialize(e));archivedEvents++;
                    if(e.Kind=="MortalTrainingCompleted")foreach(string id in lastTraining.Keys.ToArray())if(e.Detail.StartsWith(id+":",StringComparison.Ordinal))lastTraining[id]=Math.Max(lastTraining[id],e.Tick);
                }
                if(tick>=nextProgress)
                {archive.Flush();Console.WriteLine($"LONG_RUN Tick={tick}/{target} elapsedSeconds={timer.Elapsed.TotalSeconds:F1} tickMs={engine.LastDiagnostics!.TotalMilliseconds:F1}");nextProgress=tick+settings.ProgressEveryTicks;}
                if(tick>=nextSample){Record();nextSample+=Math.Max(1,yearTicks/settings.SamplesPerYear);}
            }
        }
        catch(Exception e){failure=e.ToString();Console.Error.WriteLine("LONG_RUN_FAILURE "+failure);}
        var final=engine.Snapshot();if(tick%Math.Max(1,yearTicks/settings.SamplesPerYear)!=0)Record();
        File.WriteAllText(Path.Combine(root,"report.json"),JsonSerializer.Serialize(new { Settings=settings,RequestedTicks=requested,TargetTicks=target,CompletedTicks=final.Clock.Tick,
            ElapsedYears=final.Clock.Tick/(decimal)yearTicks,Completed=failure is null&&final.Clock.Tick>=requested,ResumeCheckpoint=resumePath,ResumeTick=resumed.Clock.Tick,Steps=steps,RunSeconds=timer.Elapsed.TotalSeconds,Failure=failure,
            ArchivedEvents=archivedEvents,Validation="Shared action validation every action; complete world integrity every batch (or every Tick if configured)",
            AutoPlayerBattle="Existing shared auto-combat enabled on player battles; world-time freezing retained",Samples=samples },new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"LONG_RUN_DONE Tick={final.Clock.Tick} years={final.Clock.Tick/(decimal)yearTicks} failure={failure is not null}");
        if(failure is not null)throw new InvalidOperationException("Long run failed; actual partial result saved");
    }
}
