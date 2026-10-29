using Cultivation.Domain;
using Cultivation.Simulation;

internal static class LifeSkillTests
{
    static void Check(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
    public static IEnumerable<(string Name,Action Body)> Cases => new (string,Action)[] {
        ("Life skill foundation snapshots confirmed rules and preserves legacy modes",()=>{
            var w=LifeSkillScenario.Create();var r=w.LifeSkillRules!;
            Check(r.ExperiencePerLevel==100&&r.TreatmentHealing==20&&r.HealingBonusPerLevel==.05&&r.OintmentHerbs==2&&r.OintmentHealing==40,"Confirmed rules not loaded");
            Check(r.BleedingPowderHealing==50&&r.BleedingPowderRounds==5,"Powder total/rounds incorrect");
            Check(w.Characters.All(c=>c.LifeSkills==new LifeSkills()),"Initial skills incorrect");
            Check(TimeScenario.Create().LifeSkillRules is null,"Old mode changed");
            Check(WorldCodec.Digest(w)==WorldCodec.Digest(WorldCodec.Deserialize(WorldCodec.Serialize(w))),"Save lost skill state");
            Check(NewGameFactory.CreateLike(w,"新行者").LifeSkillRules==r,"New game lost skill mode");
        }),
        ("Life skill levels remain independent and medical healing obeys cap and incapacity",()=>{
            var w=LifeSkillScenario.Create();var r=w.LifeSkillRules!;var healer=w.Characters.Single(c=>c.Id=="player") with {LifeSkills=new(100,299,99)};
            Check(LifeSkillCalculator.Level(healer.LifeSkills!,LifeSkillKind.Medicine,r)==1&&LifeSkillCalculator.Level(healer.LifeSkills!,LifeSkillKind.Pharmacy,r)==2&&LifeSkillCalculator.Level(healer.LifeSkills!,LifeSkillKind.Smithing,r)==0,"Skill thresholds mixed");
            Check(LifeSkillCalculator.TreatmentHealing(healer,healer with {Health=50},r)==21,"Medical level bonus not applied");
            Check(LifeSkillCalculator.TreatmentHealing(healer,healer with {Health=95},r)==5,"Overheal");
            Check(LifeSkillCalculator.TreatmentHealing(healer,healer with {Health=0},r)==0,"Ordinary medicine revives incapacitated");
            Check(LifeSkillCalculator.TreatmentHealing(healer,healer with {Health=50},r with {TreatmentHealing=40})==42,"Config ignored");
        }),
        ("Life skill load rejects invalid skill state and preserves independent experience",()=>{
            var w=LifeSkillScenario.Create();w.Characters=w.Characters.Select(c=>c.Id=="player"?c with {LifeSkills=new(123,456,789)}:c).ToList();
            var saved=WorldCodec.Deserialize(WorldCodec.Serialize(w));
            Check(saved.Characters.Single(c=>c.Id=="player").LifeSkills==new LifeSkills(123,456,789),"Experience reset on load");
            saved.LifeSkillRules=saved.LifeSkillRules! with {ExperiencePerLevel=0};
            bool rejected=false;try{WorldIntegrity.Validate(saved);}catch(InvalidDataException){rejected=true;}Check(rejected,"Zero level divisor accepted");
            saved=WorldCodec.Clone(w);saved.Characters=saved.Characters.Select(c=>c.Id=="player"?c with {LifeSkills=new(-1,0,0)}:c).ToList();
            rejected=false;try{WorldIntegrity.Validate(saved);}catch(InvalidDataException){rejected=true;}Check(rejected,"Negative experience accepted");
        })
    };
}
