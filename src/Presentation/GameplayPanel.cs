using Cultivation.Domain;
using Cultivation.Simulation;
using Godot;

namespace Cultivation.Presentation;

public partial class GameplayPanel : VBoxContainer
{
    public Func<WorldState> ReadWorld { get; set; } = null!;
    public event Action<ActionIntent>? Requested;
    private RichTextLabel details = null!;
    private OptionButton item = null!;
    private Label availability = null!;
    private Button buy = null!, sell = null!, rest = null!;
    public string DetailsText => details.Text;
    public override void _Ready()
    {
        AddChild(new Label { Text = "操作對象固定為玩家；角色選單只切換觀察。報價於提交時重新驗證。" });
        var row = new HFlowContainer(); AddChild(row);
        item = new(); item.AddItem("藥材"); item.AddItem("回春丹"); row.AddChild(item);
        buy = new() { Text = "購買1份" }; sell = new() { Text = "出售1份" }; row.AddChild(buy); row.AddChild(sell);
        buy.Pressed += () => Trade(ActionKind.Buy); sell.Pressed += () => Trade(ActionKind.Sell);
        item.ItemSelected += _ => Refresh(ReadWorld(), ReadWorld().ControlledCharacterId);
        rest = new() { Text = "休息（玩家）" }; row.AddChild(rest);
        rest.Pressed += Rest;
        availability = new Label(); AddChild(availability);
        details = new() { CustomMinimumSize = new(0, 360), SizeFlagsVertical = SizeFlags.ExpandFill }; AddChild(details);
    }
    private ActionIntent Intent(WorldState w, ActionKind kind)
    {
        string resource = item.Selected == 1 ? w.RecoveryResourceId : "herb";
        var quote = MarketPricing.Quote(w, resource);
        return new("ui-trade:" + w.NextSequence, w.ControlledCharacterId, "market", resource, 1, 0, DecisionSource.Player, kind,
            LimitPrice: kind == ActionKind.Buy ? quote.BuyPrice : quote.SellPrice);
    }
    private static ActionIntent RestIntent(WorldState w) => new("ui-rest:" + w.NextSequence, w.ControlledCharacterId,
        w.ControlledCharacterId, "stamina", 1, 0, DecisionSource.Player, ActionKind.Rest);
    public void Rest() => Requested?.Invoke(RestIntent(ReadWorld()));
    private static string StaminaText(WorldState world, CharacterState actor)
    {
        var status = StaminaCalculator.Status(world, actor);
        if (status is null) return "體力：本情境未啟用";
        string environment = status.Environment switch { RestEnvironment.HomeBed => "自己住處／有床", RestEnvironment.Ordinary => "一般", _ => "露宿" };
        string condition = status.Unconscious ? "昏厥（超過30甦醒）" : status.Resting ? "休息中" : status.Fatigued ? "疲勞" : "正常";
        var modifiers = CharacterGrowthCalculator.Modifiers(world, actor);
        string mindset = actor.Mindset?.ToString() ?? "未啟用";
        return $"心境 {mindset}｜心境倍率 {modifiers.MindsetMultiplier:P0} × 疲勞倍率 {modifiers.FatigueMultiplier:P0}\n體力 {status.Current}/{status.Maximum}｜{condition}｜環境 {environment}\n休息回復：體力 {status.RecoveryPerTick}／{GameTimeDisplay.Duration(world,1)}、生命 {world.StaminaRules!.HealthRecoveryPerTick}／{GameTimeDisplay.Duration(world,1)}（非戰鬥休息時才生效）｜修為／熟練倍率 {status.GrowthMultiplier:P0}\n行動執行完成後扣體力；歸零後下一個時間步強制休息";
    }
    public void Trade(ActionKind kind) => Requested?.Invoke(Intent(ReadWorld(), kind));
    public void Refresh(WorldState w, string observedId)
    {
        if (details is null) return;
        string Name(string id) => w.Items.SingleOrDefault(i => i.Id == id)?.Name ?? id;
        item.SetItemText(0, Name("herb")); item.SetItemText(1, Name(w.RecoveryResourceId));
        var player = w.Characters.Single(c => c.Id == w.ControlledCharacterId);
        var observed = w.Characters.Single(c => c.Id == observedId);
        string? buyError = ActionValidator.Validate(w, Intent(w, ActionKind.Buy));
        string? sellError = ActionValidator.Validate(w, Intent(w, ActionKind.Sell));
        buy.Disabled = buyError is not null; sell.Disabled = sellError is not null;
        string? restError = ActionValidator.Validate(w, RestIntent(w));
        rest.Visible = w.StaminaRules is not null; rest.Disabled = restError is not null;
        rest.TooltipText = restError ?? "休息會推進時間並恢復生命／體力，新時間模式亦回復AP，直到可恢復項目全滿；不解除重傷。";
        availability.Text = $"購買：{buyError ?? "可提交"}｜出售：{sellError ?? "可提交"}";
        var items = w.Ledger.Select(e => e.ResourceId).Distinct().OrderBy(id => id, StringComparer.Ordinal);
        var quotes = new[] { MarketPricing.Quote(w, "herb"), MarketPricing.Quote(w, w.RecoveryResourceId) };
        var choice = w.Events.LastOrDefault(e => e.Kind == "Decision" && (e.ActionId.StartsWith("ai:" + observed.Id + ":", StringComparison.Ordinal) || e.ActionId.StartsWith("player-auto:" + observed.Id + ":", StringComparison.Ordinal)));
        details.Text = $"玩家：{player.Name}｜自動排程 {(w.PlayerAutoSchedule ? "啟用" : "未啟用")}｜位置 {player.DemoLocationId}｜生命 {player.Health}｜境界 {player.Realm}｜功法 {player.MethodId ?? "無"}\n" +
            StaminaText(w, player) + "\n\n庫存\n" + string.Join("\n", items.Select(id => $"{Name(id)} ({id}): {Inventory.Balance(w, player.Id, id)}")) +
            "\n\n市場\n" + string.Join("\n", quotes.Select(q => $"{q.ResourceId}: 買 {q.BuyPrice}／賣 {q.SellPrice}／庫存 {q.Stock}")) +
            "\n\n玩家成交（最近10筆）\n" + string.Join("\n", w.Trades.Where(t => t.ActorId == player.Id).TakeLast(10).Select(t => $"[{t.Tick}] {t.Kind} {t.ResourceId} ×{t.Quantity} @{t.UnitPrice}")) +
            $"\n\n觀察：{observed.Name}（不變更操作對象）\n{StaminaText(w, observed)}\nAI理由：{choice?.Detail ?? "尚無AI決策"}";
    }
}




