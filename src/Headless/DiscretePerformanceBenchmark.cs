using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Cultivation.Domain;
using Cultivation.Persistence;
using Cultivation.Simulation;

namespace Cultivation.Headless;

public sealed record DiscreteBenchmarkRules(int[] NpcCounts,string TemplateActorId,int MinimumSteps,int MaximumSteps,double MaximumWallSeconds,long MaximumWorkingSetMiB,
    double DecisionCoverage,long ImportantStopSeconds,int SoakNpcCount,int SoakYears,double SoakMaximumWallSeconds);

public static class DiscretePerformanceBenchmark
{
    private static readonly JsonSerializerOptions Json=new(){WriteIndented=true};
    public static void Validate(DiscreteBenchmarkRules r)
    {
        if(r.NpcCounts is not {Length:>0}||r.NpcCounts.Any(n=>n<1||n>20000)||r.NpcCounts.Distinct().Count()!=r.NpcCounts.Length||string.IsNullOrWhiteSpace(r.TemplateActorId)||
            r.MinimumSteps<=0||r.MaximumSteps<r.MinimumSteps||!double.IsFinite(r.MaximumWallSeconds)||r.MaximumWallSeconds<=0||r.MaximumWorkingSetMiB<=0||!double.IsFinite(r.DecisionCoverage)||r.DecisionCoverage is <=0 or >1||
            r.ImportantStopSeconds<=0||r.SoakNpcCount<1||r.SoakNpcCount>20000||r.SoakYears<=0||!double.IsFinite(r.SoakMaximumWallSeconds)||r.SoakMaximumWallSeconds<=0)
            throw new InvalidDataException("config/testing/r7-benchmark.json: invalid measurement rules");
    }
    public static WorldState Create(int count,DiscreteBenchmarkRules rules,GameConfiguration? configuration=null)
    {
        Validate(rules);var config=configuration??GameConfiguration.Find();var w=PlayerControlScenario.Create(configuration:config);var map=w.HexMap!;
        int existing=w.Characters.Count(a=>a.Id!=w.ControlledCharacterId&&a.MonsterKind==MonsterKind.None);
        if(count<existing||count>20000)throw new ArgumentOutOfRangeException(nameof(count),"Requested scale must retain all existing scenario characters");
        var source=w.Characters.Single(a=>a.Id==rules.TemplateActorId);var regions=map.Regional!.Regions;
        var initial=w.Items.Where(i=>i.Id=="spirit-stone"||w.PaidServiceRules!.MaterialPrices.ContainsKey(i.Id)).Select(i=>(i.Id,Amount:Inventory.Balance(w,source.Id,i.Id))).Where(p=>p.Amount>0).ToArray();
        for(int n=existing;n<count;n++)
        {
            string id="r7-npc:"+n.ToString(System.Globalization.CultureInfo.InvariantCulture);var region=regions[n%regions.Count];
            // Deep-copy mutable character DTOs, never share per-character training dictionaries.
            var character=JsonSerializer.Deserialize<CharacterState>(JsonSerializer.Serialize(source))!;
            w.Characters.Add(character with {Id=id,Name="量測角色"+n,OrganizationId=null,Position=default,TestProfession=TestProfession.None,
                RegionId=region.Id,DemoLocationId="town",HexPosition=region.Town,MentorId=null,EquippedWeaponId=null,CraftedWeaponId=null,EquippedArmorId=null,EquipmentAttackBonus=0,EquipmentDefenseBonus=0,LegacyWeaponBroken=false});
            if(w.Methods!.Learners.TryGetValue(source.Id,out var learned))w.Methods.Learners.Add(id,new(learned));
            foreach(var stock in initial)w.Ledger.Add(new(w.NextSequence++,0,"r7-genesis:"+id+":"+stock.Id,"$genesis",id,stock.Id,stock.Amount,"Genesis"));
            EventDecisions.Request(w,id,DecisionTrigger.Startup);
        }
        w.Performance=config.Read<PerformanceRules>("world/performance.json");WorldIntegrity.Validate(w);return w;
    }
    public static void Run(string directory,int? onlyCount=null)
    {
        var config=GameConfiguration.Find();var rules=config.Read<DiscreteBenchmarkRules>("testing/r7-benchmark.json");Validate(rules);
        string root=Path.GetFullPath(directory);Directory.CreateDirectory(root);
        WriteNew(Path.Combine(root,"run-settings.json"),new{Rules=rules,OnlyCount=onlyCount,Scenario="R6b-based synthetic population; actual seconds scheduler and systems",Runtime=RuntimeInformation.FrameworkDescription,OS=RuntimeInformation.OSDescription,
            Architecture=RuntimeInformation.ProcessArchitecture.ToString(),LogicalCores=Environment.ProcessorCount,Processor=Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER"),AvailableGcMemoryBytes=GC.GetGCMemoryInfo().TotalAvailableMemoryBytes,
#if DEBUG
            Build="Debug",
#else
            Build="Release",
#endif
            CapturedUtc=DateTimeOffset.UtcNow,RenderingFps=(double?)null});
        foreach(int count in onlyCount is {} single?new[]{single}:rules.NpcCounts)
            Measure(root,"tier-"+count,count,rules,config,null);
        if(onlyCount is null)Measure(root,"soak-"+rules.SoakYears+"years",rules.SoakNpcCount,rules,config,rules.SoakYears);
    }
    private static void Measure(string root,string label,int count,DiscreteBenchmarkRules rules,GameConfiguration config,int? years)
    {
        var total=Stopwatch.StartNew();var creation=Stopwatch.StartNew();var world=Create(count,rules,config);var engine=new SimulationEngine(world);engine.SetPlayerAutonomy(true);creation.Stop();
        engine.ScheduleWorldSignal(rules.ImportantStopSeconds,"r7-stop-probe","Important event must stop at exact second",blocking:true);
        long target=years is {} span?checked(span*4L*world.Time!.Rules.DaysPerSeason*world.Time.Rules.TicksPerDay*DiscreteTime.SecondsPerStep(world)):long.MaxValue;
        double wallBudget=years is null?rules.MaximumWallSeconds:rules.SoakMaximumWallSeconds;
        var samples=new List<double>();long processed=0,planner=0,evaluations=0,simulated=0;int peakQueue=world.Time!.DiscreteEvents!.Pending.Count,importantStops=0;
        string stop="StepLimit";bool completed=false;long initialPlanner=world.Time.EventDecisions!.PlannerCalls,initialEvaluations=world.Time.EventDecisions.Evaluations;
        Console.WriteLine($"R7_START {label} npc={count} creationMs={creation.Elapsed.TotalMilliseconds:F1}");
        var process=Process.GetCurrentProcess();
        for(int step=0;step<rules.MaximumSteps;step++)
        {
            if(total.Elapsed.TotalSeconds>=wallBudget){stop="WallBudget";break;}process.Refresh();if(process.WorkingSet64>rules.MaximumWorkingSetMiB*1048576L){stop="MemoryBudget";break;}
            if(engine.RequiresControlSelection){stop="ControlRequired";break;}
            var before=engine.Snapshot();
            if(before.BoardBattle is {Phase:BoardBattlePhase.Active} battle&&battle.Fighters.Any(f=>f.ActorId==before.ControlledCharacterId)&&!battle.PlayerAutoCombat&&before.Characters.Single(a=>a.Id==before.ControlledCharacterId).Health>0)
                engine.Submit(new("r7-auto:"+battle.Id,before.ControlledCharacterId,battle.Id,"auto-combat",1,0,DecisionSource.Player,ActionKind.SetBoardAutoCombat));
            int consumedBefore=before.Time!.DiscreteEvents!.ConsumedEventIds.Count;var timer=Stopwatch.StartNew();engine.Step(true);timer.Stop();samples.Add(timer.Elapsed.TotalMilliseconds);
            var after=engine.Snapshot();simulated=engine.WorldTimeSeconds!.Value;processed+=after.Time!.DiscreteEvents!.ConsumedEventIds.Count-consumedBefore;peakQueue=Math.Max(peakQueue,after.Time.DiscreteEvents.Pending.Count);
            planner=after.Time.EventDecisions!.PlannerCalls-initialPlanner;evaluations=after.Time.EventDecisions.Evaluations-initialEvaluations;
            if(engine.LastAdvance is {Reason:AdvanceStopReason.ImportantEvent,WorldTime:var stopped}&&stopped==rules.ImportantStopSeconds)importantStops=1;
            int covered=after.Time.EventDecisions.Traces.Keys.Count(id=>id!=after.ControlledCharacterId);
            if(years is null&&samples.Count>=rules.MinimumSteps&&covered/(double)count>=rules.DecisionCoverage){stop="CoverageReached";completed=true;break;}
            if(years is not null&&simulated>=target){stop="TargetReached";completed=true;break;}
            if(engine.LastAdvance?.Reason==AdvanceStopReason.ControlRequired){stop="ControlRequired";break;}
        }
        var final=engine.Snapshot();WorldIntegrity.Validate(final);double measuredWall=total.Elapsed.TotalMilliseconds;
        string checkpointDirectory=Path.Combine(root,"checkpoints");Directory.CreateDirectory(checkpointDirectory);string file=Path.Combine(checkpointDirectory,label+".sqlite");
        var io=Stopwatch.StartNew();new SqliteWorldStore().Save(file,final);io.Stop();double saveMs=io.Elapsed.TotalMilliseconds;
        io.Restart();var loaded=new SqliteWorldStore().Load(file);io.Stop();double loadMs=io.Elapsed.TotalMilliseconds;
        bool equal=WorldCodec.Digest(final)==WorldCodec.Digest(loaded);if(!equal)throw new InvalidDataException("R7 save changed world");
        var a=new SimulationEngine(WorldCodec.Clone(final));var b=new SimulationEngine(loaded);a.Step(true);b.Step(true);bool continuation=WorldCodec.Digest(a.Snapshot())==WorldCodec.Digest(b.Snapshot());if(!continuation)throw new InvalidDataException("R7 continuation diverged");
        double[] sorted=samples.Order().ToArray();int distinct=final.Time!.EventDecisions!.Traces.Keys.Count(id=>id!=final.ControlledCharacterId);process.Refresh();
        var report=new{Label=label,NpcCount=count,MeasuredSteps=samples.Count,RequestedCoverage=rules.DecisionCoverage,DistinctDecidedNpcs=distinct,Coverage=distinct/(double)count,RequestedYears=years,RequestedWorldSeconds=years is null?(long?)null:target,
            ActualWorldSeconds=simulated,Completed=completed,StopReason=stop,CreationMs=creation.Elapsed.TotalMilliseconds,MeasuredWallMs=measuredWall,MeanStepMs=samples.Count==0?(double?)null:samples.Average(),
            P95StepMs=sorted.Length==0?(double?)null:sorted[Math.Max(0,(int)Math.Ceiling(sorted.Length*0.95)-1)],MaximumStepMs=sorted.Length==0?(double?)null:sorted[^1],
            ProcessedOrDiscardedEvents=processed,PlannerCalls=planner,ActorEvaluations=evaluations,PeakSampledQueue=peakQueue,FinalQueue=final.Time.DiscreteEvents!.Pending.Count,ImportantStopVerified=importantStops==1,
            ManagedMemoryBytes=GC.GetTotalMemory(false),WorkingSetBytes=process.WorkingSet64,ProcessLifetimePeakWorkingSetBytes=process.PeakWorkingSet64,SaveMs=saveMs,LoadMs=loadMs,SnapshotEqual=equal,ContinuationEqual=continuation,
            Seed=final.HexMap!.Regional!.Seed,RegionCount=final.HexMap.Regional.Regions.Count,ContentVersion=final.Time.Content!.ContentVersion,ContentFingerprint=ContentProfile.Fingerprint(final.Time.Content),DecisionBudget=final.Rules.DecisionBudget,
            LedgerRows=final.Ledger.Count,OrderBookFills=final.Time.MarketOrders?.Fills.Count??0,SimulatedSecondsPerWallSecond=simulated/(measuredWall/1000),RenderingFps=(double?)null,Note="Bounded headless measurement with full integrity checks; incomplete coverage/years is not a capacity or soak pass. Per-step timings exclude external snapshot collection; total wall includes it. Peak queue is sampled at step boundaries. Memory sampled after save/load and continuation; lifetime peak includes previous tiers. No warmup; first sample includes JIT."};
        WriteNew(Path.Combine(root,label+".json"),report);Console.WriteLine($"R7_END {label} steps={samples.Count} seconds={simulated} coverage={distinct}/{count} stop={stop} completed={completed}");
    }
    private static void WriteNew(string path,object value){using var stream=new FileStream(path,FileMode.CreateNew,FileAccess.Write);JsonSerializer.Serialize(stream,value,Json);}
}
