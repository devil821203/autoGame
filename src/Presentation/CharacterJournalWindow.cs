using Cultivation.Domain;
using Godot;

namespace Cultivation.Presentation;

// Session-only observer. Never changes simulation state or grants control over an NPC.
public partial class CharacterJournalWindow : Window
{
    public override void _ExitTree() => text.Clear();
    private sealed class Journal
    {
        public List<string> Lines = [];
        public Dictionary<string, string> Actions = new(StringComparer.Ordinal);
        public Dictionary<string, string> Previous = [];
        public long EventSequence, LedgerSequence;
    }
    private readonly Dictionary<string, Journal> journals = new(StringComparer.Ordinal);
    private List<string> lines => journals[actorId!].Lines;
    private Dictionary<string, string> actions => journals[actorId!].Actions;
    private Dictionary<string, string> previous { get => journals[actorId!].Previous; set => journals[actorId!].Previous = value; }
    private object? session;
    private string? actorId;
    private long eventSequence { get => journals[actorId!].EventSequence; set => journals[actorId!].EventSequence = value; }
    private long ledgerSequence { get => journals[actorId!].LedgerSequence; set => journals[actorId!].LedgerSequence = value; }
    private OptionButton characters = null!;
    private string[] ids = [], names = [];
    private WorldState? snapshot;
    private RichTextLabel text = null!;
    public string DisplayText => text.Text;
    public string? SelectedActorId => actorId;
    public override void _Ready()
    {
        Title = "角色紀錄"; Size = new(560, 520); MinSize = new(380, 260);
        CloseRequested += Hide;
        var margin = new MarginContainer(); AddChild(margin); margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var column = new VBoxContainer(); margin.AddChild(column);
        column.AddChild(new Label { Text = "唯讀角色觀察｜行動、物品收支與狀態變化\n每人保留開始觀察後最近300筆；切角色保留紀錄，切世界／讀檔重新開始。" });
        characters = new OptionButton(); column.AddChild(characters);
        characters.ItemSelected += n => SelectActor(ids[(int)n]);
        text = new RichTextLabel { SizeFlagsVertical = Control.SizeFlags.ExpandFill, ScrollFollowing = true }; column.AddChild(text);
    }
    public void Track(ActionIntent intent, string controlledId)
    {
        if (journals.TryGetValue(intent.ActorId, out var journal)) journal.Actions[intent.Id] = intent.Kind.ToString();
    }
    public void SelectActor(string id)
    {
        if (snapshot is null || !ids.Contains(id)) return;
        actorId = id; Observe(snapshot, session!);
    }
    public void Observe(WorldState w, object identity)
    {
        if (!ReferenceEquals(session, identity))
        { session = identity; journals.Clear(); actorId = w.ControlledCharacterId; }
        snapshot = w;
        var nextIds = w.Characters.Select(c => c.Id).ToArray();
        var nextNames = w.Characters.Select(c => c.Name).ToArray();
        if (!ids.SequenceEqual(nextIds) || !names.SequenceEqual(nextNames))
        {
            ids = nextIds; names = nextNames; characters.Clear();
            foreach (var c in w.Characters) characters.AddItem($"{c.Name} ({c.Id})");
        }
        if (actorId is null || !ids.Contains(actorId)) actorId = w.ControlledCharacterId;
        characters.Select(Array.IndexOf(ids, actorId));
        var actor = w.Characters.Single(c => c.Id == actorId);
        if (!journals.ContainsKey(actor.Id))
        {
            journals.Add(actor.Id, new());
            eventSequence = w.Events.Select(e => e.Sequence).DefaultIfEmpty(-1).Max();
            ledgerSequence = w.Ledger.Select(e => e.Sequence).DefaultIfEmpty(-1).Max();
            Add(w.Clock.Tick, "開始觀察", $"{actor.Name} ({actor.Id})；位置 {actor.DemoLocationId}，生命 {actor.Health}");
        }
        Title = $"角色紀錄 — {actor.Name}";
        foreach (var intent in w.Pending.Select(p => p.Intent).Concat(w.Scheduled.Select(s => s.Intent))) Track(intent, actor.Id);
        foreach (var charge in w.Time?.Charges.Where(c => c.ActorId == actor.Id) ?? []) actions.TryAdd(charge.ActionId, "行動");
        var battles = w.Combats.Where(c => c.InitiatorId == actor.Id || c.DefenderId == actor.Id || c.Allies?.Contains(actor.Id) == true).Select(c => c.Id).ToHashSet();
        if (w.BoardBattle is { } board && board.Fighters.Any(f => f.ActorId == actor.Id)) battles.Add(board.Id);
        foreach (var entry in w.Ledger.Where(l => l.Sequence > ledgerSequence && (l.From == actor.Id || l.To == actor.Id)).OrderBy(l => l.Sequence))
        {
            actions.TryAdd(entry.ActionId, "行動");
            string item = w.Items.SingleOrDefault(i => i.Id == entry.ResourceId)?.Name ?? entry.ResourceId;
            long delta = (entry.To == actor.Id ? entry.Quantity : 0) - (entry.From == actor.Id ? entry.Quantity : 0);
            Add(entry.Tick, "物品", $"{item} {delta:+0;-0;0}｜{entry.Reason}｜{entry.From} → {entry.To}");
        }
        foreach (var e in w.Events.Where(e => e.Sequence > eventSequence).OrderBy(e => e.Sequence))
        {
            bool own = actions.ContainsKey(e.ActionId) || battles.Contains(e.ActionId) ||
                new[] { "ai:", "player-auto:", "rest:", "player-rest:", "board-player-auto:" }.Any(prefix => e.ActionId.StartsWith(prefix + actor.Id + ":", StringComparison.Ordinal)) ||
                e.Detail == actor.Id || e.Detail.StartsWith(actor.Id + ":", StringComparison.Ordinal);
            if (own) Add(e.Tick, "行動", $"{e.Kind}｜{e.Detail}");
        }
        var values = new Dictionary<string, string> {
            ["位置"] = actor.DemoLocationId + (actor.HexPosition is { } hex ? $" ({hex.Q},{hex.R})" : ""),
            ["重傷"] = actor.SeverelyInjured ? "是" : "否", ["生命"] = actor.Health.ToString(), ["體力"] = actor.Stamina?.Current.ToString() ?? "未啟用",
            ["休息"] = actor.Stamina?.Resting == true ? "是" : "否", ["昏厥"] = actor.Stamina?.Unconscious == true ? "是" : "否",
            ["疲勞"] = w.StaminaRules is { } stamina && StaminaCalculator.Fatigued(actor, stamina) ? "是" : "否",
            ["心境"] = actor.Mindset?.ToString() ?? "未啟用", ["修為"] = actor.Cultivation.ToString(), ["境界"] = actor.Realm.ToString(),
            ["煉丹熟練"] = actor.AlchemyExperience.ToString(), ["採集熟練"] = actor.GatheringExperience.ToString(),
            ["戰鬥熟練"] = actor.CombatExperience.ToString(), ["功法熟練"] = actor.MethodExperience.ToString(),
            ["功法"] = actor.MethodId ?? "無", ["武器"] = actor.CraftedWeaponId ?? actor.EquippedWeaponId ?? "無", ["防具"] = actor.EquippedArmorId ?? "無",
            ["醫術熟練"] = actor.LifeSkills?.MedicineExperience.ToString() ?? "未啟用", ["製藥熟練"] = actor.LifeSkills?.PharmacyExperience.ToString() ?? "未啟用", ["鍛造熟練"] = actor.LifeSkills?.SmithingExperience.ToString() ?? "未啟用", ["宗門"] = actor.OrganizationId ?? "無",
            ["職位"] = actor.Position.ToString(), ["聲望"] = actor.Reputation.ToString(),
            ["AP"] = w.Time?.Budgets.SingleOrDefault(b => b.ActorId == actor.Id)?.Remaining.ToString() ?? "未啟用"
        };
        foreach (var value in values)
            if (previous.TryGetValue(value.Key, out string? old) && old != value.Value) Add(w.Clock.Tick, "狀態", $"{value.Key} {old} → {value.Value}");
        previous = values;
        eventSequence = w.Events.Select(e => e.Sequence).DefaultIfEmpty(-1).Max();
        ledgerSequence = w.Ledger.Select(l => l.Sequence).DefaultIfEmpty(-1).Max();
        if (lines.Count > 300) lines.RemoveRange(0, lines.Count - 300);
        text.Text = string.Join("\n", lines);
    }
    private void Add(long tick, string category, string detail) => lines.Add($"[Tick {tick}] {category}｜{detail}");
}
