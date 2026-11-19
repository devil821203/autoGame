using System.Text.Json.Nodes;
using Cultivation.AI;
using Cultivation.Domain;
using Cultivation.Simulation;

internal static class BatchMeasurementTests
{
    private sealed class FailingPlanner : IActionPlanner
    {
        public IReadOnlyList<ScoredAction> Evaluate(LocalObservation observation) => observation.Tick >= 1
            ? throw new InvalidOperationException("Intentional batch rollback probe") : [];
    }
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    static string Projection(WorldState w)
    {
        var copy=WorldCodec.Clone(w);copy.Performance=null;copy.AiUpdates.Clear();
        copy.Events.RemoveAll(e=>e.Kind is "Decision" or "DecisionBudget" or "PlanInvalidated" or "PartyWaiting");
        var node=JsonNode.Parse(WorldCodec.Serialize(copy))!;
        void Clean(JsonNode? n){if(n is JsonObject o){o.Remove("DecisionDetail");foreach(var p in o.ToArray())Clean(p.Value);}else if(n is JsonArray a)foreach(var c in a)Clean(c);}
        Clean(node);return node.ToJsonString();
    }
    public static IEnumerable<(string Name,Action Body)> Cases=>new(string,Action)[]{
        ("Validated batch rollback restores all state and diagnostics on a failed later tick",()=>{
            var e=new SimulationEngine(MortalCultivationScenario.Create(),new FailingPlanner());string before=WorldCodec.Digest(e.Snapshot());bool failed=false;
            try{e.StepValidatedBatch(10,10);}catch(InvalidOperationException error)when(error.Message.Contains("rollback probe")){failed=true;}
            Check(failed&&WorldCodec.Digest(e.Snapshot())==before&&e.LastDiagnostics is null,"Batch exposed partially committed state");
        }),
        ("Validated batches match original per tick world exactly",()=>{
            var w=MortalCultivationScenario.Create();var ordinary=new SimulationEngine(w);var batched=new SimulationEngine(w);
            for(int n=0;n<120;n++)ordinary.Step(true);
            Check(batched.StepValidatedBatch(120,120)==120,"Batch step count wrong");
            Check(WorldCodec.Digest(ordinary.Snapshot())==WorldCodec.Digest(batched.Snapshot()),"Batch changed world outcomes");
        }),
        ("Measurement diagnostics and full rate LOD preserve economic and growth state",()=>{
            var w=MortalCultivationScenario.Create();var baseline=new SimulationEngine(w);
            var settings=GameConfiguration.Find().Read<Cultivation.Headless.SpecialistLongRunReport.Settings>("testing/specialists-long-run.json");
            var instrumented=WorldCodec.Clone(w);instrumented.Performance=settings.Performance;var measured=new SimulationEngine(instrumented);
            for(int n=0;n<240;n++)baseline.Step(true);
            var archived=new List<WorldEvent>();
            measured.StepValidatedBatch(120,120,validateEveryTick:false);archived.AddRange(measured.ArchiveMeasurementEvents(60));
            measured.StepValidatedBatch(240,120,validateEveryTick:false);archived.AddRange(measured.ArchiveMeasurementEvents(120));
            var result=measured.Snapshot();result.Events=result.Events.Concat(archived).OrderBy(e=>e.Sequence).ToList();
            Check(Projection(baseline.Snapshot())==Projection(result),"Measurement changed simulation outcomes");
        })
    };
}
