using Cultivation.Domain;
using Godot;

namespace Cultivation.Presentation;

public partial class SectFacilityPanel : VBoxContainer
{
    public Func<WorldState> ReadWorld { get; set; } = null!;
    public event Action<ActionIntent>? Requested;
    private SpinBox points = null!;
    private readonly List<(Button Button, ActionKind Kind, string Resource)> availabilityControls = [];
    private SpinBox reservedPills = null!;
    private SpinBox reservedHerbs = null!;
    private SectStockReservation? lastReservation;
    private RichTextLabel details = null!;
    public void ResetInputs() { lastReservation = null; points.Value = 30; }
    public override void _Ready()
    {
        AddChild(new Label { Text = "先到宗門接採藥工作，再到藥園工作；材料與產出屬宗門。製藥請到生活技能頁。" });
        var row = new HFlowContainer(); AddChild(row);
        Add(row, "接採藥工作（不限量）", ActionKind.AcceptSectWork, "sect", "herb");
        Add(row, "接煉丹工作（不限量）", ActionKind.AcceptSectWork, "sect", "pill");
        Add(row, "宗門藥園採藥", ActionKind.SectGather, "grove", "herb");
        Add(row, "煉丹房協助煉丹", ActionKind.SectBrew, "alchemy", "herb");
        var exchange = new HFlowContainer(); AddChild(exchange);
        points = new SpinBox { MinValue = 1, MaxValue = 1000000, Value = 30, Step = 1 }; exchange.AddChild(points);
        Add(exchange, "錢財1:1換貢獻點", ActionKind.BuyContribution, "sect", "spirit-stone");
        Add(exchange, "兌換1份回復物品", ActionKind.RedeemContribution, "sect", "pill");
        Add(exchange, "兌換1份藥草", ActionKind.RedeemContribution, "sect", "herb");
        var reserves = new HFlowContainer(); AddChild(reserves);
        reserves.AddChild(new Label { Text = "宗主設定：保留回復品／藥草" });
        reservedPills = new SpinBox { MinValue = 0, MaxValue = 1000000, Value = 10, Step = 1 }; reserves.AddChild(reservedPills);
        reservedHerbs = new SpinBox { MinValue = 0, MaxValue = 1000000, Value = 10, Step = 1 }; reserves.AddChild(reservedHerbs);
        Add(reserves, "套用保留量", ActionKind.SetSectStockReservation, "sect", "sect-stock");
        details = new RichTextLabel { CustomMinimumSize = new(0, 330), SizeFlagsVertical = SizeFlags.ExpandFill }; AddChild(details);
    }
    private void Add(Container row, string text, ActionKind kind, string target, string resource)
    {
        var button = new Button { Text = text }; row.AddChild(button); availabilityControls.Add((button,kind,resource)); button.Pressed += () => Command(kind, target, resource);
    }
    public void Command(ActionKind kind, string target, string resource)
    {
        var w = ReadWorld();
        if (kind == ActionKind.RedeemContribution && resource == "pill") resource = w.RecoveryResourceId;
        Requested?.Invoke(new("ui-facility:" + w.NextSequence, w.ControlledCharacterId, target, resource,
            kind == ActionKind.BuyContribution ? (long)points.Value : 1, 0, DecisionSource.Player, kind,
            StockReservation: kind == ActionKind.SetSectStockReservation ? new((long)reservedPills.Value, (long)reservedHerbs.Value) : null));
    }
    public void Refresh(WorldState w)
    {
        foreach(var control in availabilityControls) control.Button.Disabled = !w.AlchemyEnabled && (control.Kind == ActionKind.SectBrew || control.Kind == ActionKind.AcceptSectWork && control.Resource == "pill");
        if (w.SectFacilities is not { } r) { details.Text = "請建立宗門經濟／副本測試情境。"; return; }
        var reservation = new SectStockReservation(r.ReservedPills, r.ReservedHerbs);
        if (reservation != lastReservation)
        {
            reservedPills.Value = reservation.Pills; reservedHerbs.Value = reservation.Herbs;
            lastReservation = reservation;
        }
        string medicineName = w.Items.SingleOrDefault(i=>i.Id==w.RecoveryResourceId)?.Name ?? w.RecoveryResourceId;
        var player = w.Characters.Single(c => c.Id == w.ControlledCharacterId);
        details.Text = $"煉丹：{(w.AlchemyEnabled?"啟用":"停用；請用生活技能製藥")}\n設施所有者：{r.OrganizationId}｜宗門藥園 grove／煉丹房 alchemy\n玩家：{player.Name}｜位置{player.DemoLocationId}｜貢獻點 {Inventory.Balance(w, player.Id, "contribution")}\n" +
            (w.AlchemyEnabled ? $"成功煉丹每顆{r.BrewContribution}點，採藥每份{r.GatherContribution}點；不限總量，失敗不給點\n" : $"採藥每份{r.GatherContribution}點；製藥改用生活技能，不提供煉丹工作\n") +
            $"兌換處：{medicineName}{r.PillExchangePrice}點／藥草{r.HerbExchangePrice}點；錢財1:1換點，不可反向，離開／被逐出清零\n" +
            $"宗門藥草 {Inventory.Balance(w, r.OrganizationId, "herb")}｜{medicineName} {Inventory.Balance(w, r.OrganizationId, w.RecoveryResourceId)}\n" +
            $"保留{medicineName} {r.ReservedPills}／藥草 {r.ReservedHerbs}（保護收購及供料；成員可兌換）\n" +
            "玩家已接工作：" + string.Join("、", w.SectWorkAssignments.Where(t => t.ActorId == player.Id).Select(t => t.ProductId)) +
            "\n\n近期工作與兌換\n" + string.Join("\n", w.Events.Where(e => e.Kind.StartsWith("SectWork") || e.Kind.StartsWith("Contribution") || e.Kind.StartsWith("DungeonGather")).TakeLast(25).Select(e => $"[{e.Tick}] {e.Kind}: {e.Detail}"));
    }
}
