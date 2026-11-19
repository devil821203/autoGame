using Cultivation.Domain;
using Cultivation.Headless;
using Cultivation.Persistence;
using Cultivation.Simulation;

internal static class DiscreteBenchmarkTests
{
    private static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    public static IEnumerable<(string Name,Action Body)> Cases=>new (string,Action)[]{
        ("R7 scaled fixture retains real regions and independent deterministic actors",()=>{
            var config=GameConfiguration.Find();var rules=config.Read<DiscreteBenchmarkRules>("testing/r7-benchmark.json");
            var a=DiscretePerformanceBenchmark.Create(100,rules,config);var b=DiscretePerformanceBenchmark.Create(100,rules,config);
            Check(a.Characters.Count(c=>c.Id!=a.ControlledCharacterId&&c.MonsterKind==MonsterKind.None)==100,"Incorrect requested population");
            Check(a.HexMap!.Regional!.Regions.Count==3&&a.Time!.PlayerControl is not null&&a.Time.DiscreteEvents is not null,"Replaced real scenario with legacy fixture");
            Check(WorldCodec.Digest(a)==WorldCodec.Digest(b),"Fixture is not reproducible");
            var clones=a.Characters.Where(c=>c.Id.StartsWith("r7-npc:",StringComparison.Ordinal)).ToArray();
            Check(clones.Select(c=>c.RegionId).Distinct().Count()==3&&clones.All(c=>c.OrganizationId is null),"Incorrect region or membership distribution");
            Check(!ReferenceEquals(clones[0],clones[1]),"Shared actor state");WorldIntegrity.Validate(a);
        }),
        ("R7 refuses invalid measurement rules and never drops original NPCs",()=>{
            var rules=GameConfiguration.Find().Read<DiscreteBenchmarkRules>("testing/r7-benchmark.json");
            bool invalid=false;try{DiscretePerformanceBenchmark.Validate(rules with{DecisionCoverage=0});}catch(InvalidDataException){invalid=true;}
            Check(invalid,"Invalid coverage accepted");invalid=false;try{DiscretePerformanceBenchmark.Create(1,rules);}catch(ArgumentOutOfRangeException){invalid=true;}
            Check(invalid,"Original NPCs silently removed");
        })
    };
}
