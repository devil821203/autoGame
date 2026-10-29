using Cultivation.Domain;

namespace Cultivation.Simulation;

internal static class EquipmentDurabilityActions
{
    internal const string Source="$equipment-instance";
    internal static long LegacyStock(WorldState w,string actor,string definition)=>Inventory.Balance(w,actor,definition)+
        (w.EquipmentDurability?.LegacyWeapons.Count(g=>g.DefinitionId==definition&&Inventory.Balance(w,actor,g.Id)==1)??0);
    internal static string EquipLegacy(WorldState w,string actor,string action,string? battle=null)
    {
        if(w.EquipmentDurability is not { } d)return w.Advancement!.WeaponId;
        string definition=w.Advancement!.WeaponId;
        var existing=d.LegacyWeapons.Where(g=>g.DefinitionId==definition&&Inventory.Balance(w,actor,g.Id)==1)
            .OrderByDescending(g=>EquipmentCondition.Remaining(w,g.Id)).ThenBy(g=>g.Id,StringComparer.Ordinal).FirstOrDefault();
        if(existing is not null&&(EquipmentCondition.Intact(w,existing.Id)||Inventory.Balance(w,actor,definition)==0))return existing.Id;
        string id="legacy-gear:"+action;
        w.Ledger.Add(new(w.NextSequence++,w.Clock.Tick,action,actor,"consumed",definition,1,"EquipmentInstanced"));
        w.Ledger.Add(new(w.NextSequence++,w.Clock.Tick,action,Source,actor,id,1,"EquipmentInstanceCreated"));
        d.LegacyWeapons.Add(new(id,definition,action,actor,w.Clock.Tick,battle));
        w.Items.Add(new(id,w.Items.Single(i=>i.Id==definition).Name,"武器"));
        d.Remaining[id]=d.Rules.Maximum;return id;
    }
    internal static void CombatHit(WorldState w,string battle,string attacker,string defender,int effectiveDamage)
    {
        if(w.EquipmentDurability is null)return;
        if(!w.EquipmentDurability.BattleParticipants.ContainsKey(battle))
        {
            var participants=w.BoardBattle is { } board&&board.Id==battle?board.Fighters.Select(f=>f.ActorId):
                w.Combats.Where(b=>b.Id==battle).SelectMany(b=>new[]{b.InitiatorId,b.DefenderId}.Concat(b.Allies??[]));
            w.EquipmentDurability.BattleParticipants.Add(battle,participants.Distinct().ToList());
        }
        int n=w.Characters.FindIndex(c=>c.Id==attacker);var actor=w.Characters[n];
        if(actor.EquippedWeaponId is { } raw&&raw==w.Advancement?.WeaponId)
        {
            string id=EquipLegacy(w,actor.Id,"durability-instance:"+battle+":"+actor.Id,battle);
            w.Characters[n]=actor with{EquippedWeaponId=id,LegacyWeaponBroken=!EquipmentCondition.Intact(w,id)};
            actor=w.Characters[n];
        }
        if((actor.CraftedWeaponId??actor.EquippedWeaponId) is { } weapon)Wear(w,battle,weapon,attacker,"Attack");
        if(effectiveDamage>0&&w.Characters.Single(c=>c.Id==defender).EquippedArmorId is { } armor)Wear(w,battle,armor,defender,"Damaged");
    }
    private static void Wear(WorldState w,string battle,string equipment,string actor,string trigger)
    {
        var d=w.EquipmentDurability!;
        if(d.Wear.Any(r=>r.BattleId==battle&&r.EquipmentId==equipment))return;
        int before=d.Remaining.GetValueOrDefault(equipment,d.Rules.Maximum);
        if(before==0)return;
        int after=Math.Max(0,before-d.Rules.LossPerBattle);d.Remaining[equipment]=after;
        d.Wear.Add(new(battle,equipment,actor,w.Clock.Tick,before,after,trigger));
        int n=w.Characters.FindIndex(c=>c.Id==actor);var a=w.Characters[n];
        if(a.EquippedWeaponId==equipment)w.Characters[n]=a with{LegacyWeaponBroken=after==0};
        if(w.LifeSystem is not null)LifeActions.RecomputeEquipment(w,actor);
        ActionExecutor.Emit(w,battle,"EquipmentDurabilityChanged",$"{actor}:{equipment}:{before}->{after}:{trigger}");
    }
    internal static void Validate(WorldState w)
    {
        if(w.EquipmentDurability is not { } d)
        {if(w.Characters.Any(c=>c.LegacyWeaponBroken))throw new InvalidDataException("Legacy weapon damage flag without durability rules");return;}
        if(d.Rules is not { } r||r.Maximum<=0||r.LossPerBattle<=0||r.LossPerBattle>r.Maximum)
            throw new InvalidDataException("config/items/durability.json: Maximum and LossPerBattle must be positive, loss <= maximum");
        if(d.LegacyWeapons.Select(g=>g.Id).Distinct().Count()!=d.LegacyWeapons.Count||d.Wear.Select(v=>(v.BattleId,v.EquipmentId)).Distinct().Count()!=d.Wear.Count)
            throw new InvalidDataException("Duplicate durability instance or battle wear");
        if(d.BattleParticipants.Any(p=>string.IsNullOrWhiteSpace(p.Key)||p.Value.Count<2||p.Value.Distinct().Count()!=p.Value.Count||p.Value.Any(id=>!w.Characters.Any(c=>c.Id==id))))
            throw new InvalidDataException("Invalid durability battle participant history");
        var ledger=w.Ledger.ToLookup(e=>e.ActionId);
        foreach(var g in d.LegacyWeapons)
        {
            if(w.Advancement is null||g.DefinitionId!=w.Advancement.WeaponId||g.Id!="legacy-gear:"+g.CreationId||g.Tick<0||g.Tick>w.Clock.Tick||
                (g.BattleId is null?!w.ProcessedIntentIds.Contains(g.CreationId):!BattleExists(w,g.BattleId,g.OwnerId))||
                !w.Items.Any(i=>i.Id==g.Id)||!w.Characters.Any(c=>c.Id==g.OwnerId)||
                ledger[g.CreationId].Count(e=>e.Tick==g.Tick&&e.From==g.OwnerId&&e.To=="consumed"&&e.ResourceId==g.DefinitionId&&e.Quantity==1&&e.Reason=="EquipmentInstanced")!=1||
                ledger[g.CreationId].Count(e=>e.Tick==g.Tick&&e.From==Source&&e.To==g.OwnerId&&e.ResourceId==g.Id&&e.Quantity==1&&e.Reason=="EquipmentInstanceCreated")!=1||
                w.Characters.Select(c=>Inventory.Balance(w,c.Id,g.Id)).Concat(w.Organizations.Select(o=>Inventory.Balance(w,o.Id,g.Id))).Sum()!=1)
                throw new InvalidDataException("Invalid legacy equipment instance provenance");
        }
        bool Known(string id)=>d.LegacyWeapons.Any(g=>g.Id==id)||w.LifeSystem?.Equipment.Any(g=>g.Id==id)==true;
        foreach(var (id,left)in d.Remaining)
        {
            if(!Known(id)||left<0||left>r.Maximum)throw new InvalidDataException("Invalid equipment durability value or ID");
            int previous=r.Maximum;
            foreach(var wear in d.Wear.Where(v=>v.EquipmentId==id))
            {
                if(wear.Tick<0||wear.Tick>w.Clock.Tick||wear.Before!=previous||previous<=0||wear.After!=Math.Max(0,previous-r.LossPerBattle)||
                    !BattleExists(w,wear.BattleId,wear.ActorId)||wear.Trigger is not ("Attack" or "Damaged")||
                    wear.Trigger=="Damaged"&&w.LifeSystem?.Equipment.Any(g=>g.Id==id&&g.Slot=="armor")!=true||
                    wear.Trigger=="Attack"&&!(d.LegacyWeapons.Any(g=>g.Id==id)||w.LifeSystem?.Equipment.Any(g=>g.Id==id&&g.Slot=="weapon")==true))
                    throw new InvalidDataException("Invalid durability battle wear history");
                previous=wear.After;
            }
            if(left!=previous)throw new InvalidDataException("Unbacked durability change");
        }
        if(d.Wear.Any(v=>!d.Remaining.ContainsKey(v.EquipmentId))||w.Ledger.Any(e=>e.From==Source&&!d.LegacyWeapons.Any(g=>g.CreationId==e.ActionId)))throw new InvalidDataException("Unbacked equipment instance or wear");
        foreach(var c in w.Characters)
            if(c.LegacyWeaponBroken!=(c.EquippedWeaponId is { } id&&!EquipmentCondition.Intact(w,id)))throw new InvalidDataException("Equipped weapon broken flag mismatch");
    }
    private static bool BattleExists(WorldState w,string battle,string actor)=>
        w.BoardBattle is { } b&&b.Id==battle&&b.Fighters.Any(f=>f.ActorId==actor)||
        w.Combats.Any(b=>b.Id==battle&&(b.InitiatorId==actor||b.DefenderId==actor||b.Allies?.Contains(actor)==true))||
        w.EquipmentDurability?.BattleParticipants.TryGetValue(battle,out var members)==true&&members.Contains(actor);
}
