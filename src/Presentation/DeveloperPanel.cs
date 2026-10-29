using Cultivation.Domain;
using Cultivation.Simulation;
using Godot;

namespace Cultivation.Presentation;

public partial class DeveloperPanel : VBoxContainer
{
    private sealed record Sample(long Tick, long Stones, long Herbs, long Pills, long Growth, int Health, long Alchemy, long Gathering, long Reputation);
    private readonly Dictionary<string, List<Sample>> history = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Sample> previous = new(StringComparer.Ordinal);
    private Tree characters = null!;
    private RichTextLabel world = null!;
    private RichTextLabel reasoning = null!;
    private RichTextLabel economy = null!;
    private RichTextLabel growth = null!;
    public string GrowthText => growth.Text;
    private OptionButton metric = null!;
    private ValueChart chart = null!;
    private Label caption = null!;
    private string selectedId = "player";
    public event Action<string>? ActorSelected;
    public int SampleCount => history.GetValueOrDefault(selectedId)?.Count ?? 0;
    public string WorldText => world.Text;

    public override void _Ready()
    {
        Name = "開發者";
        AddChild(new Label { Text = "觀察模式｜Δ 為前次取樣差值｜歷史最多 120 點，讀檔重設；不寫入遊戲存檔" });
        var tabs = new TabContainer { SizeFlagsVertical = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 350) };
        AddChild(tabs);
        characters = new Tree { Name = "角色總表", Columns = 8, HideRoot = true, ColumnTitlesVisible = true, SizeFlagsVertical = SizeFlags.ExpandFill };
        string[] columns = ["角色", "錢財 / Δ", "藥材 / Δ", "丹藥 / Δ", "修為 / Δ", "生命", "聲望", "行動／剩餘 Tick"];
        for (int n = 0; n < columns.Length; n++) characters.SetColumnTitle(n, columns[n]);
        characters.ItemSelected += () => { var item = characters.GetSelected(); if (item is not null) ActorSelected?.Invoke(item.GetMetadata(0).AsString()); };
        tabs.AddChild(characters);
        var trend = new VBoxContainer { Name = "數值趨勢" }; tabs.AddChild(trend);
        caption = new Label(); trend.AddChild(caption);
        metric = new OptionButton();
        foreach (string text in new[] { "錢財", "藥材", "丹藥", "修為", "生命", "煉丹熟練", "採集熟練", "聲望" }) metric.AddItem(text);
        trend.AddChild(metric); metric.ItemSelected += _ => DrawHistory();
        chart = new ValueChart { CustomMinimumSize = new Vector2(0, 220), SizeFlagsVertical = SizeFlags.ExpandFill }; trend.AddChild(chart);
        world = TextTab(tabs, "世界與排程");
        reasoning = TextTab(tabs, "AI 與記憶");
        economy = TextTab(tabs, "任務與成交");
        growth = TextTab(tabs, "養成與裝備");
    }

    private static RichTextLabel TextTab(TabContainer parent, string name)
    {
        var label = new RichTextLabel { Name = name, BbcodeEnabled = false, SelectionEnabled = true };
        parent.AddChild(label); return label;
    }

    public void ResetHistory() { history.Clear(); previous.Clear(); }

    public void Refresh(WorldState w, string actorId)
    {
        selectedId = actorId;
        foreach (var actor in w.Characters)
        {
            if (!history.TryGetValue(actor.Id, out var values)) history[actor.Id] = values = [];
            if (values.Count > 0 && values[^1].Tick == w.Clock.Tick) continue;
            if (values.Count > 0) previous[actor.Id] = values[^1];
            values.Add(new(w.Clock.Tick, Inventory.Balance(w, actor.Id, "spirit-stone"), Inventory.Balance(w, actor.Id, "herb"), Inventory.Balance(w, actor.Id, "pill"), actor.Cultivation, actor.Health, actor.AlchemyExperience, actor.GatheringExperience, actor.Reputation));
            if (values.Count > 120) values.RemoveAt(0);
        }
        characters.Clear(); var root = characters.CreateItem();
        static string Value(long current, long old) => $"{current} ({(current - old).ToString("+0;-0;0")})";
        foreach (var actor in w.Characters)
        {
            var sample = history[actor.Id][^1]; var old = previous.GetValueOrDefault(actor.Id) ?? sample;
            var row = characters.CreateItem(root); row.SetMetadata(0, actor.Id);
            string[] cells = [actor.Name, Value(sample.Stones, old.Stones), Value(sample.Herbs, old.Herbs), Value(sample.Pills, old.Pills), Value(sample.Growth, old.Growth), sample.Health.ToString(), sample.Reputation.ToString(),
                string.Join(", ", w.Scheduled.Where(t => t.Intent.ActorId == actor.Id).Select(t => $"{t.Intent.Kind} / {t.DueTick - w.Clock.Tick}"))];
            for (int n = 0; n < cells.Length; n++) row.SetText(n, cells[n]);
            if (actor.Id == actorId) for (int n = 0; n < cells.Length; n++) row.SetCustomBgColor(n, new Color("243f50"));
        }
        var selected = w.Characters.Single(c => c.Id == actorId);
        var stats = CharacterProgression.Stats(selected, w.Advancement);
        growth.Text = $"{selected.Name}｜身分 {selected.Identity}\n組織 {selected.OrganizationId ?? "無"}｜職位 {selected.Position}\n" +
            $"生命 {selected.Health}｜狀態 {stats.Condition}\n境界 {selected.Realm}｜功法 {selected.MethodId ?? "未學習"}｜功法等級 {stats.MethodLevel}（熟練 {selected.MethodExperience}）\n" +
            $"修為 {selected.Cultivation}｜攻擊 {stats.Attack}｜防禦 {stats.Defense}\n武器 {selected.EquippedWeaponId ?? "未裝備"}\n" +
            $"煉丹等級 {stats.AlchemyLevel}（熟練 {selected.AlchemyExperience}）｜採集等級 {stats.GatheringLevel}（熟練 {selected.GatheringExperience}）\n" +
            (w.Advancement is null ? "此世界尚未啟用階段 3 養成。" :
                $"下一境界門檻 {CharacterProgression.Threshold(selected, w.Advancement)}｜境界上限 {w.Advancement.MaximumRealm}\n" +
                $"學習費用 {w.Advancement.LearningCost} 錢財／{w.Advancement.LearningTicks} Tick｜突破 {w.Advancement.BreakthroughPills} 丹藥／{w.Advancement.BreakthroughTicks} Tick\n" +
                $"目前每次修煉成長 {CharacterProgression.GrowthPerPill(selected, w.Rules, w.Advancement)}；數值為初版預設。\n戰鬥經驗 {selected.CombatExperience}；社交／招募詳見外層社交頁，任免及政策尚未開發。");
        caption.Text = $"{selected.Name}｜觀察 Tick {w.Clock.Tick}｜目前顯示 {SampleCount} 點（依畫面取樣，可能跨 Tick）";
        DrawHistory();
        world.Text = $"Tick {w.Clock.Tick}｜種子狀態 {w.RandomState}｜待提交 {w.Pending.Count}｜排程 {w.Scheduled.Count}\n" +
            $"藥材報價 買 {MarketPricing.Quote(w, "herb").BuyPrice}／賣 {MarketPricing.Quote(w, "herb").SellPrice}｜丹藥報價 買 {MarketPricing.Quote(w, "pill").BuyPrice}／賣 {MarketPricing.Quote(w, "pill").SellPrice}｜決策預算 {w.Rules.DecisionBudget}\n\n" +
            string.Join("\n", w.Organizations.Select(o => $"{o.Name}: 錢財 {Inventory.Balance(w, o.Id, "spirit-stone")} / 藥材 {Inventory.Balance(w, o.Id, "herb")} / 丹藥 {Inventory.Balance(w, o.Id, "pill")}")) +
            "\n\n排程\n" + string.Join("\n", w.Scheduled.OrderBy(t => t.DueTick).Take(60).Select(t => $"{t.Intent.ActorId} {t.Intent.Kind}: 到期 {t.DueTick}（剩 {t.DueTick - w.Clock.Tick}）"));
        var decision = w.Events.LastOrDefault(e => e.Kind == "Decision" && e.ActionId.StartsWith("ai:" + actorId + ":", StringComparison.Ordinal));
        reasoning.Text = $"{selected.Name}｜煉丹成功率 {CharacterDevelopment.SuccessChance(selected, w.Rules):P1}\n" +
            $"性格：財富 {selected.WealthWeight:F2} / 成長 {selected.GrowthWeight:F2} / 謹慎 {selected.Caution:F2} / 社交 {selected.SocialWeight:F2}\n\n" +
            "最近決策\n" + (decision is null ? "尚無決策" : $"Tick {decision.Tick}\n" + decision.Detail.Replace("; ", "\n")) +
            "\n\n有效記憶\n" + string.Join("\n", w.Memories.Where(m => m.ActorId == actorId).TakeLast(30).Select(m => $"[{m.Tick}] {m.Kind} 成功={m.Success} 錢財Δ{m.StoneDelta} {m.ResourceId}Δ{m.ItemDelta} 來源={m.Source}"));
        economy.Text = "任務與保留款\n" + string.Join("\n", w.Missions.Select(m => $"{m.Id}: {m.Status} {m.Delivered}/{m.Required}, 已付 {m.Delivered * m.UnitPrice}/{m.ReservedBudget}, 保留 {Inventory.Balance(w, "$mission:" + m.Id, "spirit-stone")}, 剩 {Math.Max(0, m.DeadlineTick - w.Clock.Tick)} Tick")) +
            "\n\n最近交付\n" + string.Join("\n", w.Deliveries.TakeLast(20).Select(d => $"[{d.Tick}] {d.ActorId} → {d.MissionId}: {d.Quantity} 顆，收入 {d.Payment}")) +
            "\n\n最近市場成交\n" + string.Join("\n", w.Trades.TakeLast(30).Select(t => $"[{t.Tick}] {t.ActorId} {t.Kind}: {t.ResourceId} x{t.Quantity} @ {t.UnitPrice}"));
    }

    private void DrawHistory()
    {
        if (!history.TryGetValue(selectedId, out var values)) return;
        chart.SetSamples(values.Select(s => (s.Tick, (double)(metric.Selected switch
        { 1 => s.Herbs, 2 => s.Pills, 3 => s.Growth, 4 => s.Health, 5 => s.Alchemy, 6 => s.Gathering, 7 => s.Reputation, _ => s.Stones }))));
    }
}
