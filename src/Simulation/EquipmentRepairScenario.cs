using Cultivation.Domain;

namespace Cultivation.Simulation;

public static class EquipmentRepairScenario
{
    public static WorldState Create(string name="行者",GameConfiguration? configuration=null)
    {
        var config=configuration??GameConfiguration.Find();var w=PlayerControlScenario.Create(name,configuration:config);
        var rules=config.Read<RepairRules>("items/repair.json");EquipmentRepairIntegrity.Rules(rules);
        w.Time!.EquipmentRepair=new(){Rules=rules};
        foreach(string actor in rules.DemoActors){
            if(!w.Characters.Any(c=>c.Id==actor))throw new InvalidDataException("config/items/repair.json: unknown DemoActors");
            foreach(var stock in new[]{(w.Advancement!.WeaponId,1L),(rules.WeaponMaterial,rules.DemoMaterialQuantity),(rules.ArmorMaterial,rules.DemoMaterialQuantity)})
                w.Ledger.Add(new(w.NextSequence++,0,"r8-demo-stock:"+actor,"$genesis",actor,stock.Item1,stock.Item2,"Genesis"));
            string action="r8-demo-equip:"+actor;w.ProcessedIntentIds.Add(action);string gear=EquipmentDurabilityActions.EquipLegacy(w,actor,action);
            int n=w.Characters.FindIndex(c=>c.Id==actor);w.Characters[n]=w.Characters[n] with{EquippedWeaponId=gear};
            string other=w.Characters.First(c=>c.Id!=actor&&c.MonsterKind==MonsterKind.None).Id;
            // Explicit audited fixture of historical battle wear; not reported as simulated battles.
            for(int i=0;i<rules.DemoWearBattles;i++){
                string battle="r8-fixture-wear:"+actor+":"+i;w.EquipmentDurability!.BattleParticipants.Add(battle,[actor,other]);
                EquipmentDurabilityActions.CombatHit(w,battle,actor,other,0);
            }
            w.Characters[n]=w.Characters[n] with{EquippedWeaponId=null,LegacyWeaponBroken=false};
        }
        WorldIntegrity.Validate(w);return w;
    }
}
