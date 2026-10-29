using Cultivation.Domain;
using Godot;

namespace Cultivation.Presentation;

public partial class SocialPanel : VBoxContainer
{
    public Func<WorldState> ReadWorld { get; set; } = null!;
    public event Action<ActionIntent>? Requested;
    private OptionButton targets = null!;
    private OptionButton incoming = null!;
    private RichTextLabel details = null!;
    private string[] targetIds = [], offerIds = [];
    public string DetailsText => details.Text;
    public override void _Ready()
    {
        AddChild(new Label { Text = "邀請不會強制建立關係；NPC 自行評估接受或拒絕。援助消耗藥材，授課須付費。" });
        targets = new OptionButton(); AddChild(targets);
        var buttons = new HBoxContainer(); AddChild(buttons);
        Add(buttons, "邀請合作", ActionKind.InviteCooperation); Add(buttons, "邀請收徒", ActionKind.InviteApprentice); Add(buttons, "邀請加入宗門", ActionKind.InviteRecruit);
        Add(buttons, "援助1藥材", ActionKind.AssistPartner); Add(buttons, "向師父學習", ActionKind.StudyWithMentor);
        incoming = new OptionButton(); AddChild(incoming);
        var replies = new HBoxContainer(); AddChild(replies); Add(replies, "接受發給玩家的邀請", ActionKind.AcceptSocialOffer); Add(replies, "拒絕發給玩家的邀請", ActionKind.DeclineSocialOffer);
        details = new RichTextLabel { CustomMinimumSize = new Vector2(0, 280), SizeFlagsVertical = SizeFlags.ExpandFill }; AddChild(details);
    }
    private void Add(HBoxContainer row, string text, ActionKind kind)
    {
        var button = new Button { Text = text }; button.Pressed += () => Command(kind); row.AddChild(button);
    }
    public void SelectTarget(string id) { int n = Array.IndexOf(targetIds, id); if (n >= 0) targets.Select(n); }
    public void Command(ActionKind kind)
    {
        var w = ReadWorld(); string? target = targets.Selected >= 0 && targets.Selected < targetIds.Length ? targetIds[targets.Selected] : null;
        string? offerId = null;
        if (kind is ActionKind.AcceptSocialOffer or ActionKind.DeclineSocialOffer)
        {
            if (incoming.Selected < 0 || incoming.Selected >= offerIds.Length) return;
            offerId = offerIds[incoming.Selected]; target = w.SocialOffers.Single(o => o.Id == offerId).FromId;
        }
        if (target is null) return;
        Requested?.Invoke(new("ui-social:" + w.NextSequence, w.ControlledCharacterId, target, "social", 1, 0, DecisionSource.Player, kind, SocialOfferId: offerId));
    }
    public void Refresh(WorldState w)
    {
        var actor = w.Characters.Single(c => c.Id == w.ControlledCharacterId);
        var peers = w.Characters.Where(c => c.Id != actor.Id && c.RegionId == actor.RegionId && !c.Hostile).ToArray();
        var ids = peers.Select(p => p.Id).ToArray();
        if (!ids.SequenceEqual(targetIds)) { targetIds = ids; targets.Clear(); foreach (var peer in peers) targets.AddItem(peer.Name); }
        var offers = w.SocialOffers.Where(o => o.ToId == actor.Id && o.Status == SocialOfferStatus.Pending).ToArray();
        ids = offers.Select(o => o.Id).ToArray();
        if (!ids.SequenceEqual(offerIds)) { offerIds = ids; incoming.Clear(); foreach (var offer in offers) incoming.AddItem(offer.Kind + ":" + offer.FromId); }
        details.Text = w.SocialRules is null ? "此世界未啟用社交；請建立社交新情境。" :
            "邀請\n" + string.Join("\n", w.SocialOffers.TakeLast(20).Select(o => $"{o.FromId} → {o.ToId}: {o.Kind} / {o.Status} / 截止 {o.ExpiresTick}")) +
            "\n\n稀疏關係（僅保存已互動關係）\n" + string.Join("\n", w.Relationships.TakeLast(30).Select(r => $"{r.FromId} → {r.ToId}: 好感 {r.Affinity}, 信任 {r.Trust}, 仇恨 {r.Grudge}, 合作 {r.Cooperating}")) +
            "\n\n師徒與成員\n" + string.Join("\n", w.Characters.Where(c => c.MentorId is not null || c.OrganizationId is not null).Select(c => $"{c.Name}: 師父 {c.MentorId ?? "無"}, 組織 {c.OrganizationId ?? "無"}, 職位 {c.Position}")) +
            $"\n\n授課原價 {w.SocialRules!.LessonPrice} 錢財；玩家師父目前報價：" +
            (actor.MentorId is null ? "無師父" : SocialPricing.Lesson(w.SocialRules, w.Relationships.FirstOrDefault(r => r.FromId == actor.MentorId && r.ToId == actor.Id)?.Trust ?? 0).ToString()) +
            $"；互動冷卻 {w.SocialRules.CooldownTicks} Tick。任免、政策及獎勵請至「組織治理」。";
    }
}
