namespace Cultivation.Domain;

public sealed record PlayerControlRules(string[] InitialCharacters,string GrantPolicy,string UnavailableBehavior,int SwitchActionPoints,int SwitchMinutes,bool RequirePaused,bool AllowDuringCombat,bool ResetAutonomyOnSwitch);
public enum PlayerControlChangeKind { Granted, Revoked, Switched }
public sealed record PlayerControlGrant(string CharacterId,string EligibilityReason,long GrantedAt,long GrantedSequence,long? RevokedAt=null,long? RevokedSequence=null,string? RevocationReason=null);
public sealed record PlayerControlChange(long Sequence,long AtSeconds,PlayerControlChangeKind Kind,string CharacterId,string Reason,string? PreviousCharacterId=null);
public sealed class PlayerControlState
{
    public int FormatVersion { get; set; }=1;
    public PlayerControlRules Rules { get; set; }=null!;
    public long NextSequence { get; set; }=1;
    public Dictionary<string,PlayerControlGrant> Grants { get; set; }=new(StringComparer.Ordinal);
    public List<PlayerControlChange> Changes { get; set; }=[];
}
