using Cultivation.Domain;
using Godot;

namespace Cultivation.Presentation;

public partial class ExpeditionPanel : VBoxContainer
{
    public Func<WorldState> ReadWorld { get; set; } = null!;
    public event Action<ActionIntent>? Requested;
    private OptionButton contacts = null!, invitations = null!, hunts = null!;
    private RichTextLabel details = null!;
    private string[] contactIds = [], invitationIds = [], huntIds = [];
    public override void _Ready()
    {
        AddChild(new Label { Text = "同門須在同地點邀請並各自同意；各自旅行到副本後由隊長探索。\n清剿需先在宗門接取；組隊擊殺只算一次，貢獻點均分，個別角色仍會受傷／可撤退。" });
        var row = new HFlowContainer(); AddChild(row); contacts = new(); row.AddChild(contacts);
        Button(row, "邀請組隊", ActionKind.InviteParty); Button(row, "退出小隊", ActionKind.LeaveParty);
        var replies = new HFlowContainer(); AddChild(replies); invitations = new(); replies.AddChild(invitations);
        Button(replies, "接受邀請", ActionKind.AcceptParty); Button(replies, "拒絕邀請", ActionKind.DeclineParty);
        var missions = new HFlowContainer(); AddChild(missions); hunts = new(); missions.AddChild(hunts); Button(missions, "接取清剿任務", ActionKind.AcceptHunt);
        details = new RichTextLabel { CustomMinimumSize = new(0, 330), SizeFlagsVertical = SizeFlags.ExpandFill }; AddChild(details);
    }
    private void Button(Container row, string text, ActionKind kind) { var b = new Button { Text = text }; row.AddChild(b); b.Pressed += () => Command(kind); }
    public void Command(ActionKind kind)
    {
        var w = ReadWorld(); string actor = w.ControlledCharacterId;
        string? target = kind switch
        {
            ActionKind.InviteParty => contacts.Selected >= 0 && contacts.Selected < contactIds.Length ? contactIds[contacts.Selected] : null,
            ActionKind.AcceptParty or ActionKind.DeclineParty => invitations.Selected >= 0 && invitations.Selected < invitationIds.Length ? invitationIds[invitations.Selected] : null,
            ActionKind.AcceptHunt => hunts.Selected >= 0 && hunts.Selected < huntIds.Length ? huntIds[hunts.Selected] : null,
            _ => w.Parties.SingleOrDefault(p => p.Members.Contains(actor))?.Id
        };
        if (target is not null) Requested?.Invoke(new("ui-expedition:" + w.NextSequence, actor, target, kind == ActionKind.AcceptHunt ? "combat" : "party", 1, 0, DecisionSource.Player, kind));
    }
    public void SelectContact(string id) { int index = Array.IndexOf(contactIds, id); if (index >= 0) contacts.Select(index); }
    private static void Fill(OptionButton box, string[] ids, Func<string, string> name)
    {
        string selected = box.Selected >= 0 && box.Selected < box.ItemCount ? box.GetItemText(box.Selected) : "";
        box.Clear(); foreach (var id in ids) box.AddItem(name(id));
        for (int n = 0; n < box.ItemCount; n++) if (box.GetItemText(n) == selected) { box.Select(n); return; }
        if (box.ItemCount > 0) box.Select(0);
    }
    public void Refresh(WorldState w)
    {
        var a = w.Characters.Single(c => c.Id == w.ControlledCharacterId);
        contactIds = w.Characters.Where(c => c.Id != a.Id && !c.Hostile && c.OrganizationId is not null && c.OrganizationId == a.OrganizationId && c.RegionId == a.RegionId && c.DemoLocationId == a.DemoLocationId).Select(c => c.Id).ToArray();
        invitationIds = w.PartyInvitations.Where(v => v.ToId == a.Id && v.Status == PartyInviteStatus.Pending && v.ExpiresTick > w.Clock.Tick).Select(v => v.Id).ToArray();
        huntIds = w.Hunts.Where(h => h.OrganizationId == a.OrganizationId && h.Kills < h.Required && h.DeadlineTick > w.Clock.Tick && !h.Participants.Contains(a.Id)).Select(h => h.Id).ToArray();
        string Name(string id) => w.Characters.Single(c => c.Id == id).Name;
        Fill(contacts, contactIds, Name); Fill(invitations, invitationIds, id => Name(w.PartyInvitations.Single(v => v.Id == id).FromId));
        Fill(hunts, huntIds, id => w.Hunts.Single(h => h.Id == id).Target == MonsterKind.Wolf ? "清剿狼" : "清剿山匪");
        details.Text = "小隊\n" + string.Join("\n", w.Parties.Select(p => $"隊長{Name(p.LeaderId)}：" + string.Join("、", p.Members.Select(id => $"{Name(id)}（{w.Characters.Single(c => c.Id == id).Health}血／{w.Characters.Single(c => c.Id == id).DemoLocationId}）")))) +
            "\n\n宗門清剿\n" + string.Join("\n", w.Hunts.TakeLast(6).Select(h => $"{(h.Target == MonsterKind.Wolf ? "狼" : "山匪")} {h.Kills}/{h.Required}，每隻{h.RewardPerKill}點，期限Tick {h.DeadlineTick} {(h.DeadlineTick <= w.Clock.Tick ? "已到期" : "")}")) +
            "\n\n近期事件\n" + string.Join("\n", w.Events.Where(e => e.Kind.StartsWith("Party") || e.Kind.StartsWith("Hunt")).TakeLast(20).Select(e => $"[{e.Tick}] {e.Kind} {e.Detail}"));
    }
}
