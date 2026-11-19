using Cultivation.Domain;
using Cultivation.Simulation;

internal static class EquipmentDurabilityTests
{
    private static void Check(bool value,string message){if(!value)throw new Exception(message);}
    private static ActionIntent I(WorldState w,ActionKind kind,string item,string? target=null)=>
        new("durability-test:"+w.NextSequence,w.ControlledCharacterId,target??w.ControlledCharacterId,item,1,0,DecisionSource.Player,kind);
    private static WorldState Act(SimulationEngine e,ActionKind kind,string item,string? target=null)
    {
        var i=I(e.Snapshot(),kind,item,target);Check(ActionValidator.Validate(e.Snapshot(),i) is null,"Invalid setup action: "+kind);
        e.Submit(i);e.Step();e.Step();return e.Snapshot();
    }
    private static WorldState GearWorld(int maximum=100)
    {
        var w=LifeSkillScenario.Create();w.PlayerAutoSchedule=false;w.EquipmentDurability!.Rules=w.EquipmentDurability.Rules with{Maximum=maximum};
        var e=new SimulationEngine(w);
        foreach(var recipe in new[]{"iron-sword","leather-armor"})
        {
            Act(e,ActionKind.CraftLifeItem,recipe);for(int t=0;t<4;t++)e.Step();
            var g=e.Snapshot().LifeSystem!.Equipment.Single(g=>g.DefinitionId==recipe);Act(e,ActionKind.EquipCraftedItem,g.Id);
        }
        w=e.Snapshot();w.Characters=w.Characters.Select(c=>c.Id is "player" or "npc:00"?c with{BaseAttack=1,BaseDefense=100}:c).ToList();
        Board(w,"durability-board");return w;
    }
    private static void Board(WorldState w,string id,int depth=0)
    {
        w.BoardRules=new(100);w.BoardBattle=new(){Id=id,Fighters=[new("player",BattleSide.Left,new(0,0),10,1),new("npc:00",BattleSide.Right,new(0,depth),9,1)]};
    }
    private static void Round(SimulationEngine e,bool attack=true)
    {
        var w=e.Snapshot();var b=w.BoardBattle!;
        foreach(var (actor,target)in new[]{("player","npc:00"),("npc:00","player")})
            e.Submit(new($"durability-turn:{b.Id}:{b.Round}:{actor}",actor,b.Id,"board-turn",1,0,actor=="player"?DecisionSource.Player:DecisionSource.Npc,ActionKind.BoardTurn,
                BoardTurn:new(b.Id,b.Round+1,AttackTargetId:attack?target:null)));
        e.Step();
    }
    public static IEnumerable<(string Name,Action Body)> Cases=>new (string,Action)[]{
        ("Board weapon attacks and armor damage each wear once per battle and survive save continuation",()=>{
            var w=GearWorld();var e=new SimulationEngine(w);Round(e);Round(e);Round(e);
            w=e.Snapshot();var a=w.Characters.Single(c=>c.Id=="player");
            Check(EquipmentCondition.Remaining(w,a.CraftedWeaponId!)==99&&EquipmentCondition.Remaining(w,a.EquippedArmorId!)==99&&w.EquipmentDurability!.Wear.Count==2,"Repeated board hits wore equipment more than once");
            var resumed=new SimulationEngine(WorldCodec.Deserialize(WorldCodec.Serialize(w)));Round(e);Round(resumed);
            Check(WorldCodec.Digest(e.Snapshot())==WorldCodec.Digest(resumed.Snapshot()),"Midbattle save reset wear limit");
            w=e.Snapshot();Board(w,"durability-board-second");e=new(w);Round(e);w=e.Snapshot();
            Check(EquipmentCondition.Remaining(w,a.CraftedWeaponId!)==98&&EquipmentCondition.Remaining(w,a.EquippedArmorId!)==98,"New battle did not wear existing gear");
        }),
        ("Waiting and out of range rejected attacks do not consume durability",()=>{
            var w=GearWorld();Board(w,"out-of-range",4);var e=new SimulationEngine(w);Round(e,false);Round(e,true);w=e.Snapshot();
            Check(w.EquipmentDurability!.Wear.Count==0,"Rejected or absent attack consumed durability");
        }),
        ("Broken equipment is retained with no bonuses and transfers retain its durability",()=>{
            var e=new SimulationEngine(GearWorld(1));Round(e);var w=e.Snapshot();var a=w.Characters.Single(c=>c.Id=="player");
            Check(a.EquipmentAttackBonus==0&&a.EquipmentDefenseBonus==0&&Inventory.Balance(w,a.Id,a.CraftedWeaponId!)==1,"Broken gear retained combat bonus or disappeared");
            Round(e);w=e.Snapshot();Check(w.EquipmentDurability!.Wear.Count==2,"Broken gear consumed extra durability");
            w.BoardBattle=null;w.BoardRules=null;e=new(w);string gear=a.CraftedWeaponId!;
            Act(e,ActionKind.UnequipCraftedItem,gear);w=Act(e,ActionKind.Transfer,gear,"npc:00");
            Check(EquipmentCondition.Remaining(w,gear)==0&&Inventory.Balance(w,"npc:00",gear)==1,"Transfer reset broken equipment");
        }),
        ("Legacy weapons become individual items and solo combat wears only one point",()=>{
            var w=CombatScenario.Create();w.CombatRules=w.CombatRules! with{DamageVariance=0};
            w.Characters=w.Characters.Select(c=>c.Id is "player" or "bandit:weak"?c with{BaseAttack=1,BaseDefense=100}:c).ToList();
            long raw=Inventory.Balance(w,"player",w.Advancement!.WeaponId);var e=new SimulationEngine(w);
            w=Act(e,ActionKind.EquipWeapon,w.Advancement!.WeaponId);string id=w.Characters.Single(c=>c.Id=="player").EquippedWeaponId!;
            Check(id.StartsWith("legacy-gear:",StringComparison.Ordinal)&&Inventory.Balance(w,"player",w.Advancement!.WeaponId)==raw-1,"Stackable weapon was not instanced");
            Act(e,ActionKind.Engage,"combat","bandit:weak");for(int t=0;t<6;t++)e.Step();w=e.Snapshot();
            Check(EquipmentCondition.Remaining(w,id)==99&&w.EquipmentDurability!.Wear.Count(v=>v.EquipmentId==id)==1,"Solo combat wear was not capped");
        }),
        ("Party combat also applies equipment wear only once",()=>{
            var w=GearWorld();w.BoardBattle=null;w.BoardRules=null;
            w.Time=null; // Exercise the existing party resolver independently of timed player-board conversion.
            var leader=w.Characters.Single(c=>c.Id=="player");
            w.Characters=w.Characters.Select(c=>c.Id is "npc:00" or "npc:01"?c with{RegionId=leader.RegionId,DemoLocationId=leader.DemoLocationId,HexPosition=leader.HexPosition}:c).ToList();
            w.Combats.Add(new("durability-party","player","npc:00",w.Clock.Tick,w.Clock.Tick+1,Allies:["npc:01"]));
            var e=new SimulationEngine(w);for(int t=0;t<6;t++)e.Step();w=e.Snapshot();var a=w.Characters.Single(c=>c.Id=="player");
            Check(EquipmentCondition.Remaining(w,a.CraftedWeaponId!)==99&&EquipmentCondition.Remaining(w,a.EquippedArmorId!)==99,"Party attack or hit omitted wear");
        }),
        ("Old snapshots without durability retain equipment bonuses and forged wear is rejected",()=>{
            var w=GearWorld();w.EquipmentDurability=null;var e=new SimulationEngine(w);Round(e);w=e.Snapshot();
            Check(w.EquipmentDurability is null&&w.Characters.Single(c=>c.Id=="player").EquipmentAttackBonus>0,"Old snapshot enabled durability");
            w=GearWorld();w.EquipmentDurability!.Remaining[w.Characters.Single(c=>c.Id=="player").CraftedWeaponId!]=99;
            try{_ = new SimulationEngine(w);}catch(InvalidDataException){return;}
            throw new Exception("Unbacked durability loss accepted");
        })
    };
}
