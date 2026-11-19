using Cultivation.Domain;

namespace Cultivation.Simulation;

internal static class SectPayroll
{
    internal static void Advance(WorldState w)
    {
        if (w.SectPayroll is not { } r || w.Clock.Tick % r.IntervalTicks != 0) return;
        var organization = w.Organizations.Single(o => o.Id == r.OrganizationId);
        var members = w.Characters.Where(c => c.OrganizationId == r.OrganizationId && c.Id != organization.LeaderId &&
            c.Position is OrganizationPosition.Member or OrganizationPosition.Elder).OrderBy(c => c.Id, StringComparer.Ordinal).ToArray();
        if (members.Length == 0) return;
        string id = "payroll:" + r.OrganizationId + ":" + w.Clock.Tick;
        long available = Inventory.Balance(w, r.OrganizationId, "spirit-stone");
        if (members.Length > available / r.StonesPerMember || members.Any(c => !InventoryCapacity.CanReceive(w, c.Id, "spirit-stone", r.StonesPerMember)))
        {
            ActionExecutor.Emit(w, id, "PayrollSkipped", "Insufficient available funds or recipient capacity; no partial payment");
            return;
        }
        foreach (var member in members)
            ActionExecutor.Transfer(w, new(id + ":" + member.Id, member.Id, r.OrganizationId, "spirit-stone", r.StonesPerMember, 0, DecisionSource.Npc),
                r.OrganizationId, member.Id, "SectSalary");
        ActionExecutor.Emit(w, id, "PayrollPaid", $"{members.Length}x{r.StonesPerMember}");
    }
}
