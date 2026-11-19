using Cultivation.Domain;

namespace Cultivation.Simulation;

public static class PlayerControl
{
    private const string InitialReason="scenario:player-control";
    public static bool InCombat(WorldState w,string id)=>BoardBattleActions.Participant(w,id)||CombatActions.Active(w,id) is not null;
    public static bool HasGrant(WorldState w,string id)=>w.Time?.PlayerControl is not {} control?id==w.ControlledCharacterId:control.Grants.TryGetValue(id,out var grant)&&grant.RevokedAt is null;
    public static bool IsControlled(WorldState w,string id)=>id==w.ControlledCharacterId&&HasGrant(w,id);
    // Zero HP in an active battle is incapacitation, not death; existing battle cleanup remains usable.
    public static bool NeedsSelection(WorldState w)=>w.Time?.PlayerControl is not null&&(!HasGrant(w,w.ControlledCharacterId)||w.Characters.Single(a=>a.Id==w.ControlledCharacterId).Health<=0&&!InCombat(w,w.ControlledCharacterId));
    public static bool CanSelect(WorldState w,string id)=>HasGrant(w,id)&&w.Characters.Any(a=>a.Id==id&&a.MonsterKind==MonsterKind.None&&a.Health>0);
    public static void EnableFresh(WorldState w,GameConfiguration? configuration=null)
    {
        if(w.Time?.Content is null||w.Time.PlayerControl is not null||w.Time.DiscreteEvents!.WorldTime!=0||w.Pending.Count!=0||w.Time.Charges.Count!=0)throw new InvalidOperationException("R6b requires a fresh R4+ world; no old-save conversion");
        var rules=(configuration??GameConfiguration.Find()).Read<PlayerControlRules>("characters/player-control.json");ValidateRules(w,rules);
        var control=new PlayerControlState{Rules=rules};
        foreach(string id in rules.InitialCharacters){long sequence=control.NextSequence++;control.Grants.Add(id,new(id,InitialReason,0,sequence));control.Changes.Add(new(sequence,0,PlayerControlChangeKind.Granted,id,InitialReason));}
        w.Time.PlayerControl=control;Validate(w);
    }
    internal static string? Switch(WorldState w,string target,bool paused)
    {
        if(w.Time?.PlayerControl is not {} control)return "R6b控制資格模式未啟用";
        if(!paused)return "請先暫停，再切換角色";
        if(!CanSelect(w,target))return "目標角色沒有有效控制資格或目前失能";
        if(InCombat(w,w.ControlledCharacterId)||InCombat(w,target))return "目前或目標角色仍在戰鬥，無法切換";
        if(target==w.ControlledCharacterId&&!NeedsSelection(w))return "已經控制此角色";
        string previous=w.ControlledCharacterId;w.ControlledCharacterId=target;w.PlayerAutoSchedule=false;
        w.Time.EventDecisions!.Ready.Remove(target);new EventScheduler(w.Time.DiscreteEvents!).Cancel(EventDecisions.RetryKey(target));
        long sequence=control.NextSequence++;control.Changes.Add(new(sequence,w.Time.DiscreteEvents!.WorldTime,PlayerControlChangeKind.Switched,target,"Manual qualified selection",previous));
        EventDecisions.Request(w,previous,DecisionTrigger.StateChanged);
        ActionExecutor.Emit(w,"control-switch:"+sequence,"PlayerControlSwitched",previous+"->"+target);
        return null;
    }
    internal static bool Revoke(WorldState w,string id,string reason)
    {
        var control=w.Time?.PlayerControl??throw new InvalidOperationException("R6b not enabled");
        if(string.IsNullOrWhiteSpace(reason))throw new ArgumentException("撤銷原因不可空白");
        if(!control.Grants.TryGetValue(id,out var grant))throw new ArgumentException("Unknown grant recipient");
        if(grant.RevokedAt is not null)return false;
        long now=w.Time!.DiscreteEvents!.WorldTime,sequence=control.NextSequence++;
        control.Grants[id]=grant with {RevokedAt=now,RevokedSequence=sequence,RevocationReason=reason};control.Changes.Add(new(sequence,now,PlayerControlChangeKind.Revoked,id,reason));
        if(id==w.ControlledCharacterId)w.PlayerAutoSchedule=false;
        ActionExecutor.Emit(w,"control-revoke:"+sequence,"PlayerControlRevoked",id+":"+reason);return true;
    }
    private static void ValidateRules(WorldState w,PlayerControlRules rules)
    {
        if(rules is null||rules.InitialCharacters is not {Length:>0}||rules.InitialCharacters.Distinct(StringComparer.Ordinal).Count()!=rules.InitialCharacters.Length||!rules.InitialCharacters.Contains("player")||
            rules.InitialCharacters.Any(id=>!w.Characters.Any(a=>a.Id==id&&a.MonsterKind==MonsterKind.None))||rules.GrantPolicy!="ScenarioOnly"||rules.UnavailableBehavior!="PauseAndSelect"||rules.SwitchActionPoints!=0||rules.SwitchMinutes!=0||!rules.RequirePaused||rules.AllowDuringCombat||!rules.ResetAutonomyOnSwitch)
            throw new InvalidDataException("config/characters/player-control.json: invalid or unsupported control rules");
    }
    public static void Validate(WorldState w)
    {
        if(w.Time?.PlayerControl is not {} c)return;
        ValidateRules(w,c.Rules);long now=w.Time.DiscreteEvents?.WorldTime??-1;
        if(w.Time.Content is null||c.FormatVersion!=1||c.Grants is null||c.Changes is null||c.NextSequence<1||c.Changes.Count!=c.NextSequence-1||c.Grants.Count!=c.Rules.InitialCharacters.Length||c.Changes.Any(e=>e is null)||
            !c.Changes.Select(e=>e.Sequence).SequenceEqual(Enumerable.Range(1,c.Changes.Count).Select(n=>(long)n))||c.Changes.Any(e=>e.AtSeconds<0||e.AtSeconds>now||!Enum.IsDefined(e.Kind)||!c.Grants.ContainsKey(e.CharacterId)||string.IsNullOrWhiteSpace(e.Reason))||
            c.Changes.Zip(c.Changes.Skip(1)).Any(p=>p.First.AtSeconds>p.Second.AtSeconds))throw new InvalidDataException("Invalid R6b grant state/history");
        foreach(var (id,g) in c.Grants)
        {
            if(g is null||id!=g.CharacterId||!c.Rules.InitialCharacters.Contains(id)||g.EligibilityReason!=InitialReason||g.GrantedAt!=0||g.GrantedSequence<=0||
                c.Changes.Count(e=>e.Sequence==g.GrantedSequence&&e.Kind==PlayerControlChangeKind.Granted&&e.CharacterId==id&&e.Reason==g.EligibilityReason)!=1||
                (g.RevokedAt is null)!=(g.RevokedSequence is null)||(g.RevokedAt is null)!=(g.RevocationReason is null)||
                g.RevokedAt is {} at&&(at<g.GrantedAt||at>now||g.RevokedSequence<=g.GrantedSequence||c.Changes.Count(e=>e.Kind==PlayerControlChangeKind.Revoked&&e.Sequence==g.RevokedSequence&&e.AtSeconds==at&&e.CharacterId==id&&e.Reason==g.RevocationReason)!=1))throw new InvalidDataException("Invalid control grant/revocation");
        }
        string selected="player";
        foreach(var e in c.Changes)
        {
            var grant=c.Grants[e.CharacterId];
            if(e.Kind==PlayerControlChangeKind.Switched){if(e.PreviousCharacterId!=selected||grant.GrantedSequence>=e.Sequence||grant.RevokedSequence is {} revoked&&revoked<=e.Sequence)throw new InvalidDataException("Unauthorized switch history");selected=e.CharacterId;}
            else if(e.PreviousCharacterId is not null||e.Kind==PlayerControlChangeKind.Granted&&e.Sequence!=grant.GrantedSequence||e.Kind==PlayerControlChangeKind.Revoked&&e.Sequence!=grant.RevokedSequence)throw new InvalidDataException("Unbacked control history");
        }
        if(selected!=w.ControlledCharacterId)throw new InvalidDataException("Controlled character has no switch provenance");
    }
}
