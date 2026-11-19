using Cultivation.Domain;
using Cultivation.Simulation;
using Godot;

namespace Cultivation.Presentation;

public partial class BoardBattlePanel : VBoxContainer
{
    public Func<WorldState> ReadWorld { get; set; } = null!;
    public event Action<ActionIntent>? Requested;
    private readonly Dictionary<(BattleSide, int, int), Button> cells = [];
    private Label status = null!, selection = null!;
    private RichTextLabel log = null!;
    private CheckButton autoCombat = null!;
    private Button submit = null!, attack = null!;
    private OptionButton attackTargets = null!;
    private string[] enemyIds = [];
    public bool AttackEnabled => attack is not null && !attack.Disabled;
    private BattleCell? move;
    private string? target;
    private int displayedRound = -1;
    public string StatusText => status.Text;
    public override void _Ready()
    {
        AddChild(new Label { Text = "選擇「普攻目標」並按「普攻」，或點棋盤敵人選目標；可先選移動。提交後推進Tick結算，未下令且未啟用自動戰鬥時等待。" });
        autoCombat = new CheckButton { Text = "玩家自動戰鬥", TooltipText = "啟用後每輪自動移動與普攻，手動提交的當輪指令優先；仍需推進Tick。" };
        AddChild(autoCombat); autoCombat.Toggled += SetAutoCombat;
        status = new(); AddChild(status);
        var boards = new HBoxContainer(); AddChild(boards);
        foreach (var side in Enum.GetValues<BattleSide>())
        {
            var box = new VBoxContainer(); boards.AddChild(box); box.AddChild(new Label { Text = side == BattleSide.Left ? "左方：後排 → 前排" : "右方：前排 ← 後排" });
            var grid = new GridContainer { Columns = 5 }; box.AddChild(grid);
            for (int row = 0; row < 5; row++) for (int column = 0; column < 5; column++)
            {
                int r = row, d = side == BattleSide.Left ? 4 - column : column;
                var button = new Button { CustomMinimumSize = new(82, 58), Text = $"{r},{d}" };
                button.Pressed += () => Click(side, new(r, d)); grid.AddChild(button); cells[(side, r, d)] = button;
            }
        }
        selection = new(); AddChild(selection);
        var controls = new HFlowContainer(); AddChild(controls);
        controls.AddChild(new Label { Text = "普攻目標：" });
        attackTargets = new OptionButton { CustomMinimumSize = new(180, 0) }; controls.AddChild(attackTargets);
        attackTargets.ItemSelected += index => { target = index > 0 && index <= enemyIds.Length ? enemyIds[index - 1] : null; Refresh(ReadWorld()); };
        attack = new Button { Text = "普攻" }; controls.AddChild(attack); attack.Pressed += Attack;
        submit = new() { Text = "提交本輪指令" }; controls.AddChild(submit); submit.Pressed += SubmitTurn;
        var clear = new Button { Text = "清除選擇／原地等待" }; controls.AddChild(clear);
        clear.Pressed += () => { move = null; target = null; Refresh(ReadWorld()); };
        log = new() { CustomMinimumSize = new(0, 140) }; AddChild(log);
    }
    private void Click(BattleSide side, BattleCell cell)
    {
        var w = ReadWorld(); if (w.BoardBattle is not { } b) return;
        var player = b.Fighters.SingleOrDefault(f => f.ActorId == w.ControlledCharacterId); if (player is null) return;
        if (side == player.Side) move = cell;
        else target = b.Fighters.SingleOrDefault(f => f.Side == side && f.Cell == cell)?.ActorId;
        Refresh(w);
    }
    public void ToggleAutoCombat(bool enabled) => autoCombat.ButtonPressed = enabled;
    public bool AutoCombatEnabled => autoCombat.ButtonPressed;
    public void SetAutoCombat(bool enabled)
    {
        var w = ReadWorld(); if (w.BoardBattle is null) return;
        Requested?.Invoke(new("ui-board-auto:" + w.NextSequence, w.ControlledCharacterId, w.BoardBattle.Id,
            "auto-combat", enabled ? 1 : 0, 0, DecisionSource.Player, ActionKind.SetBoardAutoCombat));
    }
    public void SelectMove(BattleCell? cell) { move = cell; }
    public void SelectTarget(string? id) { target = id; }
    private ActionIntent Intent(WorldState w) => new("ui-board:" + w.NextSequence, w.ControlledCharacterId, w.BoardBattle!.Id,
        "board-turn", 1, 0, DecisionSource.Player, ActionKind.BoardTurn,
        BoardTurn: new(w.BoardBattle.Id, w.BoardBattle.Round + 1, move, target));
    public void Attack()
    {
        var w = ReadWorld();
        if (target is null || w.BoardBattle is null || ActionValidator.Validate(w, Intent(w)) is not null) return;
        Requested?.Invoke(Intent(w));
    }
    public void ChooseAttackTarget(string actorId)
    {
        int index = Array.IndexOf(enemyIds, actorId) + 1;
        if (index <= 0) return;
        attackTargets.Select(index); attackTargets.EmitSignal(OptionButton.SignalName.ItemSelected, (long)index);
    }
    public void PressAttackButton() => attack.EmitSignal(Button.SignalName.Pressed);
    public void SubmitTurn()
    {
        var w = ReadWorld(); if (w.BoardBattle is null) return;
        Requested?.Invoke(Intent(w));
    }
    public void Refresh(WorldState w)
    {
        if (status is null) return;
        if (w.BoardBattle is not { } b) { status.Text = "請建立「階段7 雙陣地測試」"; autoCombat.Disabled = true; autoCombat.SetPressedNoSignal(false); submit.Disabled = true; attack.Disabled = true; attackTargets.Disabled = true; return; }
        var pendingAuto = w.Pending.LastOrDefault(p => p.Intent.Kind == ActionKind.SetBoardAutoCombat && p.Intent.ActorId == w.ControlledCharacterId)?.Intent;
        autoCombat.SetPressedNoSignal(pendingAuto is null ? b.PlayerAutoCombat : pendingAuto.Quantity == 1);
        autoCombat.Text = pendingAuto is null ? "玩家自動戰鬥" : "玩家自動戰鬥（設定已排隊）";
        autoCombat.Disabled = b.Phase != BoardBattlePhase.Active || w.Characters.Single(c => c.Id == w.ControlledCharacterId).Health == 0;
        if (displayedRound != b.Round) { displayedRound = b.Round; move = null; target = null; }
        var player = b.Fighters.SingleOrDefault(f => f.ActorId == w.ControlledCharacterId);
        var enemies = b.Fighters.Where(f => player is not null && f.Side != player.Side && w.Characters.Single(c => c.Id == f.ActorId).Health > 0).ToArray();
        var ids = enemies.Select(f => f.ActorId).ToArray();
        if (!ids.SequenceEqual(enemyIds) || attackTargets.ItemCount == 0)
        {
            enemyIds = ids; attackTargets.Clear(); attackTargets.AddItem("選擇普攻目標");
            foreach (var enemy in enemies)
            {
                var character = w.Characters.Single(c => c.Id == enemy.ActorId);
                attackTargets.AddItem($"{character.Name}（HP {character.Health}）");
            }
        }
        for (int n = 0; n < enemyIds.Length; n++)
        {
            var character = w.Characters.Single(c => c.Id == enemyIds[n]);
            attackTargets.SetItemText(n + 1, $"{character.Name}（HP {character.Health}）");
        }
        attackTargets.Select(target is not null && Array.IndexOf(enemyIds, target) >= 0 ? Array.IndexOf(enemyIds, target) + 1 : 0);
        attackTargets.Disabled = b.Phase != BoardBattlePhase.Active || player is null || w.Characters.Single(c => c.Id == w.ControlledCharacterId).Health == 0;
        foreach (var pair in cells)
        {
            var f = b.Fighters.SingleOrDefault(f => f.Side == pair.Key.Item1 && f.Cell == new BattleCell(pair.Key.Item2, pair.Key.Item3));
            var actor = f is null ? null : w.Characters.Single(c => c.Id == f.ActorId);
            pair.Value.Text = actor is null ? $"{pair.Key.Item2},{pair.Key.Item3}" : $"{actor.Name}\nHP {actor.Health}";
            pair.Value.Modulate = actor?.Health == 0 ? Colors.Gray : f?.ActorId == w.ControlledCharacterId ? Colors.LightSkyBlue : f?.ActorId == target ? Colors.LightCoral : Colors.White;
        }
        status.Text = $"Round {b.Round}｜{b.Phase}｜勝方 {b.Winner?.ToString() ?? "未定／平局"}｜上輪先攻 {string.Join(" → ", b.LastInitiative)}";
        string? error = ActionValidator.Validate(w, Intent(w)); submit.Disabled = error is not null;
        attack.Disabled = target is null || error is not null;
        attack.Text = move is null ? "普攻" : "移動後普攻";
        attack.TooltipText = target is null ? "先從選單選擇敵人，或點棋盤上的敵人。" : error ?? "提交玩家本輪的普通攻擊，推進Tick後結算；出手時重驗射程。";
        selection.Text = $"移動 {move?.ToString() ?? "原地"}｜目標 {target ?? "等待"}｜{error ?? "可提交，出手時重驗射程"}";
        log.Text = string.Join("\n", w.Events.Where(e => e.ActionId == b.Id).TakeLast(8).Select(e => $"[{e.Tick}] {e.Kind}: {e.Detail}"));
    }
}





