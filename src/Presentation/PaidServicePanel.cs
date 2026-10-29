using Cultivation.Domain;
using Cultivation.Simulation;
using Godot;

namespace Cultivation.Presentation;

public partial class PaidServicePanel : VBoxContainer
{
    public Func<WorldState> ReadWorld { get; set; } = null!;
    public event Action<ActionIntent>? Requested;
    private readonly OptionButton providers = new(), services = new(), materials = new();
    private readonly Label details = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly Button submit = new() { Text = "確認報價並委託（主角）" };
    private string[] providerIds = [], serviceIds = [];
    public string DisplayText => details.Text;
    public override void _Ready()
    {
        AddChild(new Label { Text = "同地專職服務｜開始保留材料與費用，成功後付款交付；失敗／受襲全額退款" });
        var row = new HFlowContainer(); AddChild(row); row.AddChild(providers); row.AddChild(services); row.AddChild(materials); row.AddChild(submit);
        materials.AddItem("我提供材料（只付手工費）"); materials.AddItem("師傅提供材料（材料費＋手工費）");
        AddChild(details); providers.ItemSelected += _ => Refresh(ReadWorld()); services.ItemSelected += _ => Refresh(ReadWorld()); materials.ItemSelected += _ => Refresh(ReadWorld());
        submit.Pressed += Request;
    }
    private ActionIntent? Intent(WorldState w)
    {
        if (providers.Selected < 0 || services.Selected < 0 || providers.Selected >= providerIds.Length || services.Selected >= serviceIds.Length || w.PaidServiceRules is null) return null;
        var source = materials.Selected == 0 ? ServiceMaterialSource.Client : ServiceMaterialSource.Provider;
        string service = serviceIds[services.Selected];
        return new($"ui-service:{w.NextSequence}", w.ControlledCharacterId, providerIds[providers.Selected], service, 1, 0, DecisionSource.Player,
            ActionKind.RequestLifeService, LimitPrice: PaidLifeServices.Quote(w, service, source), ServiceMaterials: source);
    }
    public void Request() { if (Intent(ReadWorld()) is { } i) Requested?.Invoke(i); }
    public void Select(string providerId, string service, ServiceMaterialSource source)
    {
        int n = Array.IndexOf(providerIds, providerId); if (n >= 0) providers.Select(n);
        Refresh(ReadWorld()); n = Array.IndexOf(serviceIds, service); if (n >= 0) services.Select(n);
        materials.Select(source == ServiceMaterialSource.Client ? 0 : 1); Refresh(ReadWorld());
    }
    public void Refresh(WorldState w)
    {
        if (!IsNodeReady()) return;
        var a = w.Characters.Single(c => c.Id == w.ControlledCharacterId);
        var present = w.Characters.Where(p => p.Id != a.Id && !p.Hostile && p.RegionId == a.RegionId && p.DemoLocationId == a.DemoLocationId && p.HexPosition == a.HexPosition &&
            p.TestProfession is TestProfession.Physician or TestProfession.Pharmacist or TestProfession.Smith).ToArray();
        var next = present.Select(p => p.Id).ToArray();
        if (!providerIds.SequenceEqual(next))
        { string? selected = providers.Selected >= 0 && providers.Selected < providerIds.Length ? providerIds[providers.Selected] : null; providerIds = next; providers.Clear(); foreach (var p in present) providers.AddItem(p.Name); int n = Array.IndexOf(providerIds, selected); if (n >= 0) providers.Select(n); }
        string? id = providers.Selected >= 0 && providers.Selected < providerIds.Length ? providerIds[providers.Selected] : null;
        var profession = present.SingleOrDefault(p => p.Id == id)?.TestProfession ?? TestProfession.None;
        next = w.PaidServiceRules?.LaborFees.Keys.Where(s => PaidLifeServices.Matches(profession, s)).ToArray() ?? [];
        if (!serviceIds.SequenceEqual(next))
        { serviceIds = next; services.Clear(); foreach (string s in serviceIds) services.AddItem(s switch { "medical" => "普通治療", "severe-injury" => "重傷療程", _ => w.Items.SingleOrDefault(i => i.Id == s)?.Name ?? s }); }
        var intent = Intent(w); string? error = intent is null ? "請到有專職師傅的地點並選擇服務" : ActionValidator.Validate(w, intent);
        submit.Disabled = error is not null;
        details.Text = intent is null ? "此處沒有可委託的專職師傅。六位專職在新建測試的青雲宗；舊存檔不自動新增。" :
            $"總價 {intent.LimitPrice} 錢財｜手工費 {w.PaidServiceRules!.LaborFees[intent.ResourceId]}｜{(intent.ServiceMaterials == ServiceMaterialSource.Client ? "材料由你提供" : "報價已含材料費")}\n{error ?? "可以委託"}";
        foreach (var work in w.LifeSystem?.Work.Where(t => t.Service?.ClientId == a.Id) ?? []) details.Text += $"\n委託中：{work.Service!.ServiceId}，剩{GameTimeDisplay.Duration(w, work.DueTick - w.Clock.Tick)}";
    }
}
