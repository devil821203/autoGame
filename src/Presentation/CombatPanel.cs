using Cultivation.Domain;
using Godot;

namespace Cultivation.Presentation;

public partial class CombatPanel : VBoxContainer
{
	public Func<WorldState> ReadWorld { get; set; } = null!;
	public event Action<ActionIntent>? Requested;
	private OptionButton targets = null!;
	private RichTextLabel details = null!;
	private string[] targetIds = [];
	public string DetailsText => details.Text;

	public override void _Ready()
	{
		AddChild(new Label { Text = "初版戰鬥：固定 Tick 自動互擊；不永久死亡。技能／戰中治療消耗丹藥，撤退可能失敗。" });
		targets = new OptionButton(); AddChild(targets);
		var controls = new HBoxContainer(); AddChild(controls);
		Add(controls, "玩家交戰", () => Command(ActionKind.Engage));
		Add(controls, "強擊（1 丹藥）", () => Command(ActionKind.PowerStrike));
		Add(controls, "戰中治療（1 丹藥）", () => Command(ActionKind.CombatHeal));
		Add(controls, "嘗試撤退", () => Command(ActionKind.Retreat));
		Add(controls, "戰後治療（需時間）", () => Command(ActionKind.Heal));
		details = new RichTextLabel { CustomMinimumSize = new Vector2(0, 300), SizeFlagsVertical = SizeFlags.ExpandFill }; AddChild(details);
	}

	private static void Add(HBoxContainer parent, string text, Action action)
	{
		var button = new Button { Text = text }; button.Pressed += action; parent.AddChild(button);
	}

	public void Command(ActionKind kind)
	{
		var w = ReadWorld(); string actor = w.ControlledCharacterId;
		if (kind == ActionKind.Heal)
		{
			Requested?.Invoke(new("ui-combat:" + w.NextSequence, actor, actor, "pill", 1, 0, DecisionSource.Player, ActionKind.Heal));
			return;
		}
		var battle = w.Combats.SingleOrDefault(c => c.Status == CombatStatus.Active && (c.InitiatorId == actor || c.DefenderId == actor));
		string? target = battle is not null && kind != ActionKind.Engage ? (battle.InitiatorId == actor ? battle.DefenderId : battle.InitiatorId) :
			targets.Selected >= 0 && targets.Selected < targetIds.Length ? targetIds[targets.Selected] : null;
		if (target is null) return;
		Requested?.Invoke(new("ui-combat:" + w.NextSequence, actor, target, kind is ActionKind.PowerStrike or ActionKind.CombatHeal ? "pill" : "combat", 1, 0, DecisionSource.Player, kind));
	}

	public void Refresh(WorldState w)
	{
		var actor = w.Characters.Single(c => c.Id == w.ControlledCharacterId);
		var enemies = w.Characters.Where(c => c.Hostile && c.RegionId == actor.RegionId && (!w.DemoMapEnabled || c.DemoLocationId == actor.DemoLocationId)).ToArray();
		var ids = enemies.Select(c => c.Id).ToArray();
		if (!ids.SequenceEqual(targetIds))
		{
			targetIds = ids; targets.Clear();
			foreach (var enemy in enemies) targets.AddItem(enemy.Name);
		}
		if (w.CombatRules is null) { details.Text = "此世界未啟用戰鬥，請先建立階段 3 戰鬥情境。"; return; }
		details.Text = $"玩家生命 {actor.Health}｜丹藥 {Inventory.Balance(w, actor.Id, "pill")}｜戰鬥經驗 {actor.CombatExperience}\n" +
			$"回合間隔 {w.CombatRules.RoundTicks} Tick｜撤退成功率 {w.CombatRules.RetreatChance:P0}\n\n可見敵人\n" +
			string.Join("\n", enemies.Select(e => { var s = CharacterProgression.Stats(e, w.Advancement); return $"{e.Name}: 生命 {e.Health}, 攻擊 {s.Attack}, 防禦 {s.Defense}"; })) +
			"\n\n近期戰鬥\n" + string.Join("\n", w.Combats.TakeLast(20).Select(c => $"{c.InitiatorId} vs {c.DefenderId}: {c.Status} / {c.Rounds} 回合 / 勝者 {c.WinnerId ?? "無"}" + (c.Status == CombatStatus.Active ? $" / 下回合 Tick {c.NextRoundTick}" : ""))) +
			"\n\n戰利品（來自敗方庫存）\n" + string.Join("\n", w.Loot.TakeLast(15).Select(l => $"[{l.Tick}] {l.LoserId} → {l.WinnerId}: {l.Quantity} {l.ResourceId}")) +
			"\n\n戰鬥事件\n" + string.Join("\n", w.Events.Where(e => e.Kind is "CombatRound" or "CombatEnded" or "Retreated" or "RetreatFailed").TakeLast(15).Select(e => $"[{e.Tick}] {e.Kind}: {e.Detail}"));
	}
}
