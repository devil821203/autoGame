using Cultivation.Domain;

namespace Cultivation.Simulation;

internal static class LifeSystemIntegrity
{
    internal static void Validate(WorldState w)
    {
        if(w.LifeSystem is not {} s)
        {
            if(w.Characters.Any(a=>a.SeverelyInjured||a.CraftedWeaponId is not null||a.EquippedArmorId is not null||a.EquipmentAttackBonus!=0||a.EquipmentDefenseBonus!=0))throw new InvalidDataException("Life equipment/injury requires life rules");
            return;
        }
        var o=s.Options;
        if(w.LifeSkillRules is null||o.TreatmentTicks is <1 or >30||o.SevereTreatmentTicks is <1 or >30||o.SevereHerbs<1||o.SevereOintment<1||o.SevereHealing is <1 or >100||o.ExperiencePerCompletion<1||
            new[]{o.FineChance,o.SuperiorChance,o.QualityChancePerLevel}.Any(v=>!double.IsFinite(v)||v<0||v>1)||o.FineChance+o.SuperiorChance>1||
            !double.IsFinite(o.FineMultiplier)||!double.IsFinite(o.SuperiorMultiplier)||o.FineMultiplier<1||o.SuperiorMultiplier<o.FineMultiplier||o.SuperiorMultiplier>10)
            throw new InvalidDataException("Invalid life runtime rules");
        if(s.Recipes.Select(r=>r.Id).Distinct().Count()!=s.Recipes.Count||s.Recipes.Any(r=>string.IsNullOrWhiteSpace(r.Id)||!Enum.IsDefined(r.Skill)||r.Skill==LifeSkillKind.Medicine||r.Ticks is <1 or >30||r.Inputs.Count==0||
            r.Inputs.Any(p=>p.Value<1||p.Value>1000000||!w.Items.Any(i=>i.Id==p.Key))||!w.Items.Any(i=>i.Id==r.Output)||r.Slot is not (null or "weapon" or "armor")||r.Attack is <0 or >1000000||r.Defense is <0 or >1000000))throw new InvalidDataException("Invalid life recipes");
        if(s.Work.Select(t=>t.Intent.Id).Distinct().Count()!=s.Work.Count||s.Work.Select(t=>t.Intent.ActorId).Distinct().Count()!=s.Work.Count||s.Work.Any(t=>t.DueTick<=w.Clock.Tick||!w.ProcessedIntentIds.Contains(t.Intent.Id)||!w.Characters.Any(a=>a.Id==t.Intent.ActorId)||!LifeActions.IsWork(t.Intent.Kind)||
            !t.Inputs.OrderBy(p=>p.Key).SequenceEqual(LifeActions.Inputs(w,t.Intent).OrderBy(p=>p.Key))||t.Inputs.Any(p=>Inventory.Balance(w,ActionExecutor.Escrow(t.Intent.Id),p.Key)!=p.Value)))throw new InvalidDataException("Invalid life work reservation");
        if(s.Receipts.Select(r=>r.ActionId).Distinct().Count()!=s.Receipts.Count||s.Receipts.Any(r=>r.Tick<0||r.Tick>w.Clock.Tick||!w.ProcessedIntentIds.Contains(r.ActionId)||!s.Recipes.Any(d=>d.Id==r.RecipeId && r.Output==(d.Slot is null?d.Output:"gear:"+r.ActionId))||!w.Characters.Any(a=>a.Id==r.ActorId)||
            w.Ledger.Count(l=>l.ActionId==r.ActionId&&l.From=="$life-production"&&l.To==r.ActorId&&l.ResourceId==r.Output&&l.Quantity==1&&l.Tick==r.Tick)!=1))throw new InvalidDataException("Invalid life production receipt");
        if(s.Equipment.Select(g=>g.Id).Distinct().Count()!=s.Equipment.Count||s.Equipment.Any(g=>!Enum.IsDefined(g.Quality)||g.Slot is not ("weapon" or "armor")||g.Id!="gear:"+g.ActionId||
            (!s.Receipts.Any(r=>r.ActionId==g.ActionId&&r.Output==g.Id&&r.RecipeId==g.DefinitionId)&&!(g.Quality==EquipmentQuality.Ordinary&&w.ExternalMarket?.Receipts.Any(r=>r.Kind==ActionKind.ExternalMarketBuy&&r.Id==g.ActionId&&r.DeliveredItemId==g.Id&&r.ItemId==g.DefinitionId&&r.Quantity==1)==true))||!w.Items.Any(i=>i.Id==g.Id)))throw new InvalidDataException("Invalid crafted equipment provenance");
        foreach(var g in s.Equipment)
        {
            var r=s.Recipes.Single(r=>r.Id==g.DefinitionId);double m=g.Quality==EquipmentQuality.Superior?o.SuperiorMultiplier:g.Quality==EquipmentQuality.Fine?o.FineMultiplier:1;
            if(g.Slot!=r.Slot||g.Attack!=(int)Math.Floor(r.Attack*m)||g.Defense!=(int)Math.Floor(r.Defense*m)||
                w.Characters.Select(a=>Inventory.Balance(w,a.Id,g.Id)).Concat(w.Organizations.Select(a=>Inventory.Balance(w,a.Id,g.Id))).Sum()!=1)throw new InvalidDataException("Invalid equipment stats or ownership");
        }
        foreach(var a in w.Characters)
        {
            var gear=s.Equipment.Where(g=>g.Id==a.CraftedWeaponId||g.Id==a.EquippedArmorId).ToArray();
            if(a.CraftedWeaponId is not null&&(!gear.Any(g=>g.Id==a.CraftedWeaponId&&g.Slot=="weapon")||a.EquippedWeaponId is not null)||a.EquippedArmorId is not null&&!gear.Any(g=>g.Id==a.EquippedArmorId&&g.Slot=="armor")||
                gear.Any(g=>Inventory.Balance(w,a.Id,g.Id)!=1)||a.EquipmentAttackBonus!=gear.Where(g=>EquipmentCondition.Intact(w,g.Id)).Sum(g=>g.Attack)||a.EquipmentDefenseBonus!=gear.Where(g=>EquipmentCondition.Intact(w,g.Id)).Sum(g=>g.Defense))throw new InvalidDataException("Invalid equipped life item");
        }
        if(s.Effects.Select(e=>e.Id).Distinct().Count()!=s.Effects.Count||s.Effects.Select(e=>e.ActorId).Distinct().Count()!=s.Effects.Count||s.Effects.Any(e=>e.Remaining<1||e.Remaining>w.LifeSkillRules.BleedingPowderRounds||e.Healing!=w.LifeSkillRules.BleedingPowderHealing/w.LifeSkillRules.BleedingPowderRounds||!w.Characters.Any(a=>a.Id==e.ActorId)||
            e.BattleId is null&&e.NextTick<=w.Clock.Tick||e.BattleId is not null&&(w.BoardBattle?.Id!=e.BattleId||w.BoardBattle.Phase!=BoardBattlePhase.Active||e.NextRound<=w.BoardBattle.Round)))throw new InvalidDataException("Invalid medicine effect timing");
        if(s.CombatUses.Select(u=>(u.BattleId,u.ActorId,u.Round)).Distinct().Count()!=s.CombatUses.Count||s.CombatUses.Any(u=>u.Round<1||!w.Characters.Any(a=>a.Id==u.ActorId)))throw new InvalidDataException("Invalid combat medicine use");
    }
}
