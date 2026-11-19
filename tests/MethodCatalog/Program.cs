using System.Text.Json;
using Cultivation.Domain;
using Cultivation.Simulation;

var config = JsonSerializer.Deserialize<CultivationMethodCatalog>(File.ReadAllText(
    System.IO.Path.Combine(AppContext.BaseDirectory, "../../../../../config/cultivation/methods-proposal.json")))!;
CultivationMethodConfiguration.Validate(config);
int count = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception(name); Console.WriteLine("PASS " + name); count++; }
void Reject(CultivationMethodCatalog invalid, string name)
{
    try { CultivationMethodConfiguration.Validate(invalid); }
    catch (InvalidDataException e) { Check(e.Message.Contains("cultivation/methods-proposal.json"), name); return; }
    throw new Exception("Accepted invalid " + name);
}
var q = config.Methods[0];
Check(MethodProficiencyCalculator.ProficiencyLayer(q, 99.9m) == 0 &&
    MethodProficiencyCalculator.ProficiencyLayer(q, 100) == 1 &&
    MethodProficiencyCalculator.ProficiencyLayer(q, 200) == 2 &&
    MethodProficiencyCalculator.ProficiencyLayer(q, 300) == 3, "Cumulative proficiency boundaries");
var fist = new MethodSkillProgress("QingyunFist", 99.5m);
var brick = new MethodSkillProgress("MightyBrick", 0);
var after = MethodProficiencyCalculator.Gain(fist, .5m);
Check(after.Proficiency == 100 && fist.Proficiency == 99.5m && brick.Proficiency == 0 &&
    q.Skills[2].Training == "修練力大飛磚", "Independent skill training and fractional proficiency");
var restored = JsonSerializer.Deserialize<MethodSkillProgress>(JsonSerializer.Serialize(after))!;
Check(restored == after && MethodProficiencyCalculator.Gain(restored, .25m).Proficiency == 100.25m,
    "Progress round trip and continuation");
Reject(config with { TrainingRules = config.TrainingRules with { StaminaCost = 0 } }, "Reject zero stamina");
Reject(config with { Methods = [q with { CumulativeProficiencyThresholds = [100, 99, 300] }] }, "Reject unordered thresholds");
Reject(config with { Methods = [q with { Skills = [q.Skills[0], q.Skills[0]] }] }, "Reject duplicate skill IDs");
Reject(config with { Methods = [q with { Skills = [q.Skills[1] with { PhysicalMultiplierByLayer = [1.2m] }] }] }, "Reject incomplete damage series");
Check(config.Methods[1].Skills[0].PhysicalAttackMultiplier == 1.1m &&
    config.TrainingRules.BaseCultivationExperience == 1 && q.AdditionalLayerRequirements.All(r => r.Strength == 20 && r.Constitution == 20),
    "Confirmed rules loaded without runtime defaults");
Check(q.Skills[1].UseStaminaCost == 0 && q.Skills[1].CooldownRounds == 1, "Qingyun fist uses one battle round cooldown instead of stamina");
Reject(config with { Methods = [q with { Skills = [q.Skills[1] with { CooldownRounds = -1 }] }] }, "Reject negative battle cooldown");
Console.WriteLine($"{count}/{count} passed; isolated catalog only, world integration not tested");
