using Cultivation.Domain;
using Godot;

namespace Cultivation.Presentation;

public partial class EventJournalPanel : VBoxContainer
{
    public Func<WorldState> ReadWorld { get; set; } = null!;
    private LineEdit kind = null!, search = null!;
    private RichTextLabel entries = null!;
    private CheckBox mine = null!;
    public void OnlyPlayerNotifications(bool value) { mine.ButtonPressed = value; Refresh(); }
    public string DisplayText => entries.Text;
    public void Filter(string eventKind, string query) { kind.Text = eventKind; search.Text = query; Refresh(); }
    public override void _Ready()
    {
        mine = new() { Text = "只看玩家實際收到的世界通知" }; AddChild(mine); mine.Toggled += _ => Refresh();
        kind = new() { PlaceholderText = "事件類型，例如 ResourceRegenerated；空白為全部" }; AddChild(kind);
        search = new() { PlaceholderText = "搜尋角色ID、內容或行動ID" }; AddChild(search);
        kind.TextChanged += _ => Refresh(); search.TextChanged += _ => Refresh();
        entries = new() { CustomMinimumSize = new(0, 330), SizeFlagsVertical = SizeFlags.ExpandFill }; AddChild(entries);
    }
    public void Refresh()
    {
        if (entries is null) return;
        var world = ReadWorld();
        var received = world.Notifications.Where(n => n.RecipientId == world.ControlledCharacterId).Select(n => n.EventSequence).ToHashSet();
        var matches = world.Events.Where(e => (!mine.ButtonPressed || received.Contains(e.Sequence)) && (kind.Text.Length == 0 || e.Kind.Equals(kind.Text, StringComparison.OrdinalIgnoreCase)) &&
            (search.Text.Length == 0 || (e.ActionId + " " + e.Detail).Contains(search.Text, StringComparison.OrdinalIgnoreCase))).ToArray();
        entries.Text = $"符合 {matches.Length} 筆，顯示最近100筆\n" + string.Join("\n", matches.TakeLast(100).Select(e => $"[{e.Tick}] {e.Kind} {e.ActionId}: {e.Detail}"));
    }
}
