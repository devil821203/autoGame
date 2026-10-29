using Cultivation.Domain;
using Godot;

namespace Cultivation.Presentation;

public partial class MissionPanel : VBoxContainer
{
    public Func<WorldState> ReadWorld { get; set; } = null!;
    public event Action<ActionIntent>? Requested;
    private SpinBox quantity = null!;
    private SpinBox price = null!;
    private SpinBox days = null!;
    private SpinBox budget = null!;
    private OptionButton missions = null!;
    private RichTextLabel details = null!;
    private string[] missionIds = [];
    private Label workTasks = null!;
    private MissionTemplate? lastDefaults;
    public string DetailsText => details.Text;
    public void ResetInputs() => lastDefaults = null;

    public override void _Ready()
    {
        AddChild(new Label { Text = "宗門物品徵集｜授權職位可發布／取消，玩家可接單／交付，NPC 自主決策。\nDemo須先旅行到宗門；設定數量、單價、期限與預算後推進Tick。" });
        workTasks = new Label(); AddChild(workTasks);
        var fields = new HBoxContainer(); AddChild(fields);
        quantity = Field(fields, "數量", 20, 10000);
        price = Field(fields, "單價", 120, 1000000);
        days = Field(fields, "天數", 7, 30);
        budget = Field(fields, "預算上限", 2400, 10000000000);
        var publish = new Button { Text = "發布任務（先保留預算）" }; publish.Pressed += Publish; AddChild(publish);
        missions = new OptionButton(); AddChild(missions);
        var cancel = new Button { Text = "取消選中任務並退回未用預算" }; cancel.Pressed += Cancel; AddChild(cancel);
        var replies = new HFlowContainer(); AddChild(replies);
        var accept = new Button { Text = "玩家接受選中任務" }; replies.AddChild(accept); accept.Pressed += () => Respond(ActionKind.AcceptMission);
        var deliver = new Button { Text = "玩家交付1份" }; replies.AddChild(deliver); deliver.Pressed += () => Respond(ActionKind.DeliverMission);
        details = new RichTextLabel { SizeFlagsVertical = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 220) }; AddChild(details);
    }

    private static SpinBox Field(HBoxContainer row, string title, long value, long max)
    {
        row.AddChild(new Label { Text = title });
        var spin = new SpinBox { MinValue = 1, MaxValue = max, Step = 1, Value = value, CustomMinimumSize = new Vector2(110, 0) };
        row.AddChild(spin); return spin;
    }

    public void Publish()
    {
        var w = ReadWorld(); string id = "ui-task:" + w.NextSequence;
        long deadline = checked(w.Clock.Tick + (w.Time is { } time ? (long)days.Value * time.Rules.TicksPerDay :
            ((long)days.Value * 86400 + w.Clock.SimulatedSecondsPerTick - 1) / w.Clock.SimulatedSecondsPerTick));
        Requested?.Invoke(new("publish:" + id, w.ControlledCharacterId, "sect", w.MedicineOnly ? "herb" : "pill", 1, 0, DecisionSource.Player, ActionKind.PublishMission,
            Offer: new(id, (long)quantity.Value, (long)price.Value, deadline, (long)budget.Value)));
    }

    public void Cancel()
    {
        if (missions.Selected < 0 || missions.Selected >= missionIds.Length) return;
        var w = ReadWorld();
        var mission = w.Missions.Single(m => m.Id == missionIds[missions.Selected]);
        Requested?.Invoke(new("cancel:" + w.NextSequence, w.ControlledCharacterId, mission.OrganizationId, mission.ResourceId, 1, 0, DecisionSource.Player, ActionKind.CancelMission, MissionId: mission.Id));
    }

    public void Respond(ActionKind kind)
    {
        if (missions.Selected < 0 || missions.Selected >= missionIds.Length) return;
        var w = ReadWorld(); var mission = w.Missions.Single(m => m.Id == missionIds[missions.Selected]);
        Requested?.Invoke(new("ui-mission:" + w.NextSequence, w.ControlledCharacterId, mission.OrganizationId, mission.ResourceId, 1, 0,
            DecisionSource.Player, kind, MissionId: mission.Id));
    }

    public void Refresh(WorldState w)
    {
        if (w.MissionDefaults is { } defaults && defaults != lastDefaults)
        {
            quantity.MaxValue = Math.Max(quantity.MaxValue, defaults.Quantity); quantity.Value = defaults.Quantity;
            price.MaxValue = Math.Max(price.MaxValue, defaults.UnitPrice); price.Value = defaults.UnitPrice;
            days.MaxValue = Math.Max(days.MaxValue, defaults.DeadlineDays); days.Value = defaults.DeadlineDays;
            budget.MaxValue = Math.Max(budget.MaxValue, defaults.MaximumBudget); budget.Value = defaults.MaximumBudget;
            lastDefaults = defaults;
        }
        workTasks.Text = w.SectFacilities is { } f ? $"不限量貢獻工作：採藥每份{f.GatherContribution}點、煉丹成功每顆{f.BrewContribution}點。請到「宗門設施與兌換」接工作。" : "";
        if (!w.AlchemyEnabled && w.SectFacilities is { } herbs) workTasks.Text = $"採藥貢獻工作：每份{herbs.GatherContribution}點。煉丹停用，製藥請用生活技能（自有材料）。";
        if (w.HuntRules is { } huntRules) workTasks.Text += $"\n清剿狼／山匪：每輪各{huntRules.RequiredKills}隻，每隻{huntRules.RewardPerKill}點，組隊共用進度均分；到「副本組隊與清剿」接取。";
        string[] ids = w.Missions.Select(m => m.Id).ToArray();
        if (!ids.SequenceEqual(missionIds))
        {
            missionIds = ids; missions.Clear();
            foreach (var id in ids) missions.AddItem(id);
            if (ids.Length > 0) missions.Select(ids.Length - 1);
        }
        details.Text = w.Missions.Count == 0 ? "尚無任務。發布後推進 Tick，才能完成預算保留。" : string.Join("\n\n", w.Missions.Select(m =>
            $"{m.Id}｜{m.Status}｜交付 {m.Delivered}/{m.Required}｜接單 {m.Participants.Count} 人\n" +
            $"單價 {m.UnitPrice}｜已付款 {m.Delivered * m.UnitPrice}｜保留款 {Inventory.Balance(w, "$mission:" + m.Id, "spirit-stone")}｜預算 {m.ReservedBudget}\n" +
            $"截止 Tick {m.DeadlineTick}｜剩餘 {Math.Max(0, m.DeadlineTick - w.Clock.Tick)} Tick"));
    }
}

