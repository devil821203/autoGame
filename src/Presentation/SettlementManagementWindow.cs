using Cultivation.Domain;
using Godot;

namespace Cultivation.Presentation;

public partial class SettlementManagementWindow : Window
{
    public Func<WorldState>? ReadWorld { get; set; }
    public event Action<ActionIntent>? Requested;
    public event Action? TickRequested;
    public SettlementPanel Panel { get; }=new(){SizeFlagsHorizontal=Control.SizeFlags.ExpandFill,CustomMinimumSize=new(720,0)};
    public override void _Ready()
    {
        Title="聚落管理與查詢";Size=new(1000,700);MinSize=new(500,360);CloseRequested+=Hide;
        var scroll=new ScrollContainer();AddChild(scroll);scroll.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var column=new VBoxContainer(){SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};scroll.AddChild(column);
        var tick=new Button(){Text="前進一Tick（結算已送出操作）"};column.AddChild(tick);tick.Pressed+=()=>TickRequested?.Invoke();
        column.AddChild(Panel);Panel.ReadWorld=()=>ReadWorld!.Invoke();Panel.Requested+=i=>Requested?.Invoke(i);
    }
    public void Refresh(WorldState w,object identity)=>Panel.Refresh(w,identity);
}
