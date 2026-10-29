using Cultivation.Domain;
using Godot;

namespace Cultivation.Presentation;

public partial class SectEconomyPanel : RichTextLabel
{
    public void Refresh(WorldState w)
    {
        if (w.TestProcurement is null) { Text = "請建立「宗門經濟／副本測試」情境。"; return; }
        var r = w.TestProcurement;
        string primaryName = w.Items.SingleOrDefault(i => i.Id == r.PrimaryResourceId)?.Name ?? r.PrimaryResourceId;
        var hideMissionIds = w.Missions.Where(m => m.ResourceId == "beast-hide" && m.OrganizationId == r.SellerId).Select(m => m.Id).ToHashSet(StringComparer.Ordinal);
        long hideDelivered = w.Deliveries.Where(d => hideMissionIds.Contains(d.MissionId)).Sum(d => d.Quantity);
        var hideSales = w.ProcurementReceipts.Where(p => p.ResourceId == "beast-hide").ToArray();
        Text = $"測試收購節點：無資金上限（每筆外部注資有帳本）\n每{r.IntervalTicks} Tick重新檢查，每輪各需求{r.DemandPerItem}；不足漲{r.PriceChangePercent}%，超額降{r.PriceChangePercent}%\n" +
            string.Join("\n", w.ProcurementQuotes.Select(q => $"{q.ResourceId}: 單價{q.UnitPrice}，本輪收到{q.ReceivedThisPeriod}/{r.DemandPerItem}")) +
            $"\n\n宗門可用錢財：{Inventory.Balance(w, "sect", "spirit-stone")}；{primaryName}：{Inventory.Balance(w, "sect", r.PrimaryResourceId)}；獸皮：{Inventory.Balance(w, "sect", "beast-hide")}\n" +
            $"獸皮累計：任務收貨 {hideDelivered} 張／售出 {hideSales.Sum(s => s.Quantity)} 張／銷售收入 {hideSales.Sum(s => s.Payment)} 錢財。庫存為0不代表未售出，到貨後測試收購會自動買走。\n" +
            $"俸祿：每{w.SectPayroll?.IntervalTicks} Tick，普通成員／長老各{w.SectPayroll?.StonesPerMember}錢財，不含宗主\n" +
            $"自動任務：每{w.SectDuty?.MissionIntervalTicks} Tick；考核每{w.SectDuty?.ReviewIntervalTicks} Tick，連續{w.SectDuty?.MissLimit}次未交付／成功工作逐出\n" +
            string.Join("\n", w.MemberDuties.Select(d => $"{d.ActorId}：連續未完成{d.Misses}次，下次考核Tick {d.LastReviewTick + w.SectDuty!.ReviewIntervalTicks}")) +
            "\n\n近期宗門與副本事件\n" + string.Join("\n", w.Events.Where(e => e.Kind.StartsWith("Payroll") || e.Kind.StartsWith("Duty") || e.Kind.StartsWith("Dungeon") || e.Kind.StartsWith("Procurement") || e.Kind == "TestProcurementBought").TakeLast(30).Select(e => $"[{e.Tick}] {e.Kind}: {e.Detail}"));
    }
}
