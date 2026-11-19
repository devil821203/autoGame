using Cultivation.Domain;
using Godot;

namespace Cultivation.Presentation;

public partial class GovernancePanel : VBoxContainer
{
    public Func<WorldState> ReadWorld { get; set; } = null!;
    public event Action<ActionIntent>? Requested;
    private OptionButton organizations = null!, members = null!;
    private CheckBox applications = null!, privateMissions = null!;
    private SpinBox capacity = null!, reputation = null!, reward = null!;
    private RichTextLabel details = null!;
    private string[] orgIds = [], memberIds = [];
    public override void _Ready()
    {
        AddChild(new Label { Text = "在社交情境使用。政策僅由領導者設定；任務限制於發布時生效。" });
        organizations = new OptionButton(); AddChild(organizations);
        applications = new CheckBox { Text = "開放主動申請（NPC 自主評估）" }; AddChild(applications);
        privateMissions = new CheckBox { Text = "新發布任務僅限成員" }; AddChild(privateMissions);
        capacity = Number("入門人數上限", 1, 10000, 30);
        reputation = Number("最低聲望", 0, 1000000, 0);
        var policies = new HFlowContainer(); AddChild(policies);
        Button(policies, "套用政策", ActionKind.SetOrganizationPolicy);
        Button(policies, "玩家申請加入", ActionKind.ApplyToOrganization);
        Button(policies, "玩家退出", ActionKind.LeaveOrganization);
        members = new OptionButton(); AddChild(members);
        reward = Number("獎勵錢財", 1, 1000000, 10);
        var actions = new HFlowContainer(); AddChild(actions);
        Button(actions, "任命長老", ActionKind.PromoteMember); Button(actions, "降為成員", ActionKind.DemoteMember);
        Button(actions, "逐出組織", ActionKind.ExpelMember); Button(actions, "發放獎勵", ActionKind.RewardMember);
        Button(actions, "提名繼任者（須對方同意）", ActionKind.InviteSuccessor);
        details = new RichTextLabel { CustomMinimumSize = new Vector2(0, 220) }; AddChild(details);
    }
    private SpinBox Number(string title, double min, double max, double value)
    {
        var row = new HBoxContainer(); AddChild(row); row.AddChild(new Label { Text = title });
        var field = new SpinBox { MinValue = min, MaxValue = max, Step = 1, Value = value }; row.AddChild(field); return field;
    }
    private void Button(Container row, string text, ActionKind kind)
    {
        var button = new Button { Text = text }; row.AddChild(button); button.Pressed += () => Command(kind);
    }
    public void Command(ActionKind kind)
    {
        if (organizations.Selected < 0 || organizations.Selected >= orgIds.Length) return;
        var w = ReadWorld(); string org = orgIds[organizations.Selected];
        bool self = kind is ActionKind.SetOrganizationPolicy or ActionKind.ApplyToOrganization or ActionKind.LeaveOrganization;
        if (!self && (members.Selected < 0 || members.Selected >= memberIds.Length)) return;
        Requested?.Invoke(new("ui-governance:" + w.NextSequence, w.ControlledCharacterId, self ? org : memberIds[members.Selected],
            kind == ActionKind.InviteSuccessor ? "social" : kind == ActionKind.RewardMember ? "spirit-stone" : "organization", kind == ActionKind.RewardMember ? (long)reward.Value : 1,
            0, DecisionSource.Player, kind, Policy: kind == ActionKind.SetOrganizationPolicy ? new(org, applications.ButtonPressed, (int)capacity.Value, (long)reputation.Value, privateMissions.ButtonPressed) : null, OrganizationId: org));
    }
    public void Refresh(WorldState w)
    {
        var local = w.Organizations.Where(o => o.RegionId == w.Characters.Single(c => c.Id == w.ControlledCharacterId).RegionId).ToArray();
        var ids = local.Select(o => o.Id).ToArray();
        if (!ids.SequenceEqual(orgIds)) { orgIds = ids; organizations.Clear(); foreach (var o in local) organizations.AddItem(o.Name); }
        string? org = organizations.Selected >= 0 && organizations.Selected < orgIds.Length ? orgIds[organizations.Selected] : null;
        var peers = w.Characters.Where(c => c.OrganizationId == org && c.Id != w.ControlledCharacterId && org is not null).ToArray();
        ids = peers.Select(c => c.Id).ToArray();
        if (!ids.SequenceEqual(memberIds)) { memberIds = ids; members.Clear(); foreach (var c in peers) members.AddItem(c.Name); }
        details.Text = "目前生效政策（上方為待套用設定）\n" + string.Join("\n", w.OrganizationPolicies.Select(p => $"{p.OrganizationId}: 開放 {p.OpenApplications}、上限 {p.MaximumMembers}、聲望 {p.MinimumReputation}、成員任務 {p.MissionsMembersOnly}")) +
            "\n\n成員\n" + string.Join("\n", peers.Select(c => $"{c.Name}: {c.Position}")) +
            (org is null ? "" : $"\n可用錢財：{Inventory.Balance(w, org, "spirit-stone")}");
    }
}
