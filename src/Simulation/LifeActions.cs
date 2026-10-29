using Cultivation.Domain;

namespace Cultivation.Simulation;

internal static class LifeActions
{
    internal static bool Handles(ActionKind k) => k is ActionKind.MedicalTreatment or ActionKind.TreatSevereInjury or ActionKind.CraftLifeItem or ActionKind.UseMedicine or ActionKind.UseCombatMedicine or ActionKind.EquipCraftedItem or ActionKind.UnequipCraftedItem;
    internal static bool IsWork(ActionKind k) => k is ActionKind.MedicalTreatment or ActionKind.TreatSevereInjury or ActionKind.CraftLifeItem;
    internal static int Cost(WorldState w, ActionIntent i) => i.Kind switch {
        ActionKind.MedicalTreatment => w.LifeSystem!.Options.TreatmentTicks,
        ActionKind.TreatSevereInjury => w.LifeSystem!.Options.SevereTreatmentTicks,
        ActionKind.CraftLifeItem => w.LifeSystem!.Recipes.Single(r=>r.Id==i.ResourceId).Ticks,
        ActionKind.UseCombatMedicine => 0, _=>1 };
    internal static Dictionary<string,long> Inputs(WorldState w,ActionIntent i) => i.Kind switch {
        ActionKind.MedicalTreatment => new() {{"herb",w.LifeSkillRules!.TreatmentHerbs}},
        ActionKind.TreatSevereInjury => new() {{"herb",w.LifeSystem!.Options.SevereHerbs},{"ointment",w.LifeSystem.Options.SevereOintment}},
        _ => new(w.LifeSystem!.Recipes.Single(r=>r.Id==i.ResourceId).Inputs) };
    internal static string? Validate(WorldState w,ActionIntent i)
    {
        if(w.LifeSystem is not {} life || w.LifeSkillRules is null) return "Life skills not enabled";
        if(i.Quantity!=1||i.DurationTicks!=0||i.MissionId is not null||i.BoardTurn is not null)return "Invalid life action";
        var a=w.Characters.Single(c=>c.Id==i.ActorId);
        if(a.LifeSkills is null)return "Life skill unavailable";
        if (i.TargetId != a.Id && a.TestProfession == TestProfession.Physician && w.PaidServiceRules is not null && IsWork(i.Kind)) return "Use paid service commission for specialist treatment";
        bool battle=BoardBattleActions.Participant(w,a.Id);
        if(i.Kind==ActionKind.UseCombatMedicine)
        {
            var b=w.BoardBattle;
            return !battle||i.TargetId!=a.Id||i.ResourceId!="bleeding-powder"||a.Health>=100||
                Inventory.Balance(w,a.Id,i.ResourceId)<1||life.Effects.Any(e=>e.ActorId==a.Id)||
                life.CombatUses.Any(u=>u.BattleId==b!.Id&&u.ActorId==a.Id&&u.Round==b.Round+1)||b!.Orders.Any(o=>o.ActorId==a.Id)
                ? "Combat medicine unavailable, turn ordered or effect already active" : null;
        }
        if(battle || CombatActions.Active(w,a.Id) is not null)return "Life work unavailable in battle";
        if(IsWork(i.Kind))
        {
            if(w.Scheduled.Any(t=>t.Intent.ActorId==a.Id)||life.Work.Any(t=>t.Intent.ActorId==a.Id))return "Life worker busy";
            if(i.Kind==ActionKind.CraftLifeItem)
            {
                if(i.TargetId!=a.Id||!life.Recipes.Any(r=>r.Id==i.ResourceId))return "Unknown life recipe";
                var r=life.Recipes.Single(r=>r.Id==i.ResourceId);
                if(r.Slot is null&&!InventoryCapacity.CanReceive(w,a.Id,r.Output,1))return "Output capacity exceeded";
            }
            else
            {
                var patient=w.Characters.SingleOrDefault(c=>c.Id==i.TargetId);
                if(patient is null||patient.RegionId!=a.RegionId||patient.DemoLocationId!=a.DemoLocationId||patient.HexPosition!=a.HexPosition||BoardBattleActions.Participant(w,patient.Id)||CombatActions.Active(w,patient.Id) is not null)return "Patient must be present and out of battle";
                if(i.Kind==ActionKind.MedicalTreatment ? i.ResourceId!="herb"||patient.Health is <=0 or >=100 : i.ResourceId!="severe-injury"||!patient.SeverelyInjured)return "No applicable injury";
            }
            return Inputs(w,i).Any(p=>Inventory.Balance(w,a.Id,p.Key)<p.Value)?"Missing life materials":null;
        }
        if(i.TargetId!=a.Id)return "Life item target must be self";
        if(i.Kind==ActionKind.UseMedicine)
            return i.ResourceId is not ("ointment" or "bleeding-powder")||a.Health is <=0 or >=100||Inventory.Balance(w,a.Id,i.ResourceId)<1||i.ResourceId=="bleeding-powder"&&life.Effects.Any(e=>e.ActorId==a.Id)?"Medicine unavailable or no healing needed":null;
        var gear=life.Equipment.SingleOrDefault(g=>g.Id==i.ResourceId);
        if(gear is null||Inventory.Balance(w,a.Id,gear.Id)!=1)return "Equipment not owned";
        if(i.Kind==ActionKind.EquipCraftedItem)
            return gear.Slot=="weapon"?(a.EquippedWeaponId is not null||a.CraftedWeaponId is not null?"Unequip weapon first":null):a.EquippedArmorId is not null?"Unequip armor first":null;
        return a.CraftedWeaponId!=gear.Id&&a.EquippedArmorId!=gear.Id?"Equipment not equipped":null;
    }
    internal static void Execute(WorldState w,ActionIntent i)
    {
        var life=w.LifeSystem!; int index=w.Characters.FindIndex(c=>c.Id==i.ActorId);var a=w.Characters[index];
        if(IsWork(i.Kind))
        {
            var inputs=Inputs(w,i);foreach(var p in inputs)ActionExecutor.Transfer(w,i with {ResourceId=p.Key,Quantity=p.Value},a.Id,ActionExecutor.Escrow(i.Id),"LifeReserved");
            life.Work.Add(new(i,w.Clock.Tick+Cost(w,i),inputs));ActionExecutor.Emit(w,i.Id,"LifeWorkStarted",$"{a.Id}:{i.Kind}:{i.ResourceId}");return;
        }
        if(i.Kind is ActionKind.UseMedicine or ActionKind.UseCombatMedicine)
        {
            ActionExecutor.Transfer(w,i,a.Id,"consumed","MedicineConsumed");
            if(i.ResourceId=="ointment")w.Characters[index]=a with {Health=Math.Min(100,a.Health+w.LifeSkillRules!.OintmentHealing)};
            else
            {
                int count=w.LifeSkillRules!.BleedingPowderRounds;
                life.Effects.Add(new(i.Id,a.Id,count,w.LifeSkillRules.BleedingPowderHealing/count,
                    i.Kind==ActionKind.UseCombatMedicine?w.BoardBattle!.Id:null,w.BoardBattle?.Round+1??0,w.Clock.Tick+1));
                if(i.Kind==ActionKind.UseCombatMedicine)life.CombatUses.Add(new(w.BoardBattle!.Id,a.Id,w.BoardBattle.Round+1));
            }
            ActionExecutor.Emit(w,i.Id,"MedicineUsed",$"{a.Id}:{i.ResourceId}");return;
        }
        var g=life.Equipment.Single(g=>g.Id==i.ResourceId);bool equip=i.Kind==ActionKind.EquipCraftedItem;
        w.Characters[index]=a with {CraftedWeaponId=g.Slot=="weapon"?(equip?g.Id:null):a.CraftedWeaponId,EquippedArmorId=g.Slot=="armor"?(equip?g.Id:null):a.EquippedArmorId};
        RecomputeEquipment(w,a.Id);ActionExecutor.Emit(w,i.Id,"LifeEquipmentChanged",$"{a.Id}:{i.Kind}:{g.Id}");
    }
    internal static void RecomputeEquipment(WorldState w,string id)
    {
        int n=w.Characters.FindIndex(c=>c.Id==id);var a=w.Characters[n];var gear=w.LifeSystem!.Equipment.Where(g=>(g.Id==a.CraftedWeaponId||g.Id==a.EquippedArmorId)&&EquipmentCondition.Intact(w,g.Id)).ToArray();
        w.Characters[n]=a with {EquipmentAttackBonus=gear.Sum(g=>g.Attack),EquipmentDefenseBonus=gear.Sum(g=>g.Defense)};
    }
    private static void Gain(WorldState w,string id,LifeSkillKind kind)
    {
        int n=w.Characters.FindIndex(c=>c.Id==id);var a=w.Characters[n];var s=a.LifeSkills??new();
        long value=CharacterDevelopment.GainExperience(LifeSkillCalculator.Experience(s,kind),CharacterGrowthCalculator.Growth(w,a,w.LifeSystem!.Options.ExperiencePerCompletion));
        w.Characters[n]=a with {LifeSkills=kind switch {LifeSkillKind.Medicine=>s with {MedicineExperience=value},LifeSkillKind.Pharmacy=>s with {PharmacyExperience=value},_=>s with {SmithingExperience=value}}};
    }
    internal static List<ActionIntent> Complete(WorldState w)
    {
        var done=new List<ActionIntent>();if(w.LifeSystem is not {} life)return done;
        foreach(var task in life.Work.Where(t=>t.DueTick<=w.Clock.Tick).ToArray())
        {
            var i=task.Intent;var a=w.Characters.Single(c=>c.Id==i.ActorId);var patient=w.Characters.SingleOrDefault(c=>c.Id==i.TargetId);
            bool medicine=i.Kind!=ActionKind.CraftLifeItem;
            bool valid=patient is not null&&(!medicine||patient.RegionId==a.RegionId&&patient.DemoLocationId==a.DemoLocationId&&patient.HexPosition==a.HexPosition&&!BoardBattleActions.Participant(w,patient.Id)&&
                (i.Kind==ActionKind.TreatSevereInjury?patient.SeverelyInjured:patient.Health is >0 and <100));
            var recipe=medicine?null:life.Recipes.Single(r=>r.Id==i.ResourceId);
            valid &= recipe?.Slot is not null||recipe is null||InventoryCapacity.CanReceive(w,a.Id,recipe.Output,1);
            valid &= PaidLifeServices.ValidCompletion(w, task);
            foreach(var p in task.Inputs)ActionExecutor.Transfer(w,i with {ResourceId=p.Key,Quantity=p.Value},ActionExecutor.Escrow(i.Id),valid?"consumed":a.Id,valid?"LifeConsumed":"LifeRefund");
            if(valid)
            {
                if(medicine)
                {
                    int n=w.Characters.FindIndex(c=>c.Id==patient!.Id);int heal=i.Kind==ActionKind.TreatSevereInjury?life.Options.SevereHealing:LifeSkillCalculator.TreatmentHealing(a,patient!,w.LifeSkillRules!);
                    w.Characters[n]=patient! with {Health=Math.Min(100,patient!.Health+heal),SeverelyInjured=i.Kind==ActionKind.TreatSevereInjury?false:patient.SeverelyInjured};
                    Gain(w,a.Id,LifeSkillKind.Medicine);ActionExecutor.Emit(w,i.Id,"MedicalTreatmentCompleted",$"{a.Id}->{patient.Id}:{heal}:severe:{w.Characters[n].SeverelyInjured}");
                }
                else
                {
                    string output=recipe!.Output;
                    if(recipe.Slot is not null)
                    {
                        long level=LifeSkillCalculator.Level(a.LifeSkills??new(),LifeSkillKind.Smithing,w.LifeSkillRules!);
                        var rng=new SeededRandom(w.RandomState);double roll=rng.NextUInt32()/((double)uint.MaxValue+1);w.RandomState=rng.State;
                        double superior=Math.Min(1,life.Options.SuperiorChance+level*life.Options.QualityChancePerLevel);
                        double fine=Math.Min(1-superior,life.Options.FineChance);
                        var quality=roll<superior?EquipmentQuality.Superior:roll<superior+fine?EquipmentQuality.Fine:EquipmentQuality.Ordinary;
                        double mult=quality==EquipmentQuality.Superior?life.Options.SuperiorMultiplier:quality==EquipmentQuality.Fine?life.Options.FineMultiplier:1;
                        output="gear:"+i.Id;life.Equipment.Add(new(output,recipe.Id,quality,recipe.Slot,(int)Math.Floor(recipe.Attack*mult),(int)Math.Floor(recipe.Defense*mult),i.Id));
                        w.Items.Add(new(output,$"{quality switch {EquipmentQuality.Superior=>"優秀",EquipmentQuality.Fine=>"精良",_=>"普通"}}{w.Items.Single(it=>it.Id==recipe.Output).Name}",recipe.Slot=="weapon"?"武器":"防具"));
                    }
                    life.Receipts.Add(new(i.Id,a.Id,w.Clock.Tick,recipe.Id,output));
                    ActionExecutor.Transfer(w,i with {ResourceId=output},"$life-production",a.Id,"LifeProduced");Gain(w,a.Id,recipe.Skill);
                    ActionExecutor.Emit(w,i.Id,"LifeCrafted",$"{a.Id}:{output}");
                }
            }
            else ActionExecutor.Emit(w,i.Id,"LifeWorkCancelled","Patient changed location/state or capacity exceeded; materials refunded, AP retained");
            PaidLifeServices.Settle(w, task, valid);
            life.Work.Remove(task);done.Add(i);
        }
        return done;
    }
    internal static void CancelWork(WorldState w, LifeWork task)
    {
        foreach (var p in task.Inputs) ActionExecutor.Transfer(w, task.Intent with { ResourceId = p.Key, Quantity = p.Value }, ActionExecutor.Escrow(task.Intent.Id), task.Intent.ActorId, "LifeRefund");
        PaidLifeServices.Settle(w, task, false);
        w.LifeSystem!.Work.Remove(task);
        ActionExecutor.Emit(w, task.Intent.Id, "LifeWorkCancelled", "Service interrupted; materials and fee refunded");
    }
    internal static void Effects(WorldState w,string? battleId=null,int round=0)
    {
        if(w.LifeSystem is not {} life)return;
        foreach(var e in life.Effects.ToArray())
        {
            bool due=battleId is null?e.BattleId is null&&e.NextTick<=w.Clock.Tick:e.BattleId==battleId&&e.NextRound<=round;
            if(!due)continue;int n=w.Characters.FindIndex(c=>c.Id==e.ActorId);var a=w.Characters[n];
            int amount=e.Healing+(e.Remaining==1?w.LifeSkillRules!.BleedingPowderHealing%w.LifeSkillRules.BleedingPowderRounds:0);
            int heal=a.Health>0?Math.Min(amount,100-a.Health):0;w.Characters[n]=a with {Health=a.Health+heal};
            life.Effects.Remove(e);if(e.Remaining>1&&a.Health>0)life.Effects.Add(e with {Remaining=e.Remaining-1,NextTick=w.Clock.Tick+1,NextRound=round+1});
            ActionExecutor.Emit(w,e.Id,"MedicineEffectTick",$"{a.Id}:+{heal};remaining:{e.Remaining-1}");
        }
    }
    internal static void EndBattle(WorldState w,BoardBattle b)
    {
        if(w.LifeSystem is not {} life || life.FinishedBattles.Contains(b.Id))return;
        life.FinishedBattles.Add(b.Id);
        for(int n=0;n<life.Effects.Count;n++)if(life.Effects[n].BattleId==b.Id)life.Effects[n]=life.Effects[n] with {BattleId=null,NextTick=w.Clock.Tick+1};
        foreach(var f in b.Fighters)
        {
            int n=w.Characters.FindIndex(c=>c.Id==f.ActorId);if(w.Characters[n].Health==0)w.Characters[n]=w.Characters[n] with {SeverelyInjured=true};
        }
    }
    internal static void QueueCombatMedicine(WorldState w)
    {
        if(w.LifeSystem is null||w.BoardBattle is not {Phase:BoardBattlePhase.Active} b)return;
        foreach(var f in b.Fighters)
        {
            if(f.ActorId==w.ControlledCharacterId&&!b.PlayerAutoCombat)continue;
            var i=new ActionIntent($"auto-medicine:{b.Id}:{f.ActorId}:{b.Round+1}",f.ActorId,f.ActorId,"bleeding-powder",1,0,f.ActorId==w.ControlledCharacterId?DecisionSource.Player:DecisionSource.Npc,ActionKind.UseCombatMedicine);
            if(w.Characters.Single(c=>c.Id==f.ActorId).Health is >0 and <=60&&!w.Pending.Any(p=>p.Intent.ActorId==f.ActorId)&&ActionValidator.Validate(w,i) is null)
                w.Pending.Add(new(w.NextSequence++,i));
        }
    }
}
