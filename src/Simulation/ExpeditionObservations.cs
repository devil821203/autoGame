using Cultivation.AI;
using Cultivation.Domain;

namespace Cultivation.Simulation;

internal static class ExpeditionObservations
{
    internal static ExpeditionObservation? Observe(WorldState w, CharacterState a)
    {
        if (w.PartyRules is not { } rules || a.OrganizationId is null || a.Hostile) return null;
        var p = PartyActions.For(w, a.Id);
        var contacts = w.Characters.Where(b => b.Id != a.Id && !b.Hostile && b.OrganizationId == a.OrganizationId && b.DemoLocationId == a.DemoLocationId && b.RegionId == a.RegionId && b.Health >= rules.MinimumHealth && (b.Aspiration == Aspiration.Combat || b.CombatDrive >= 1) &&
            PartyActions.For(w, b.Id) is null && PartyActions.Available(w, b.Id) && !w.PartyInvitations.Any(v => v.FromId == a.Id && v.ToId == b.Id && v.ExpiresTick > w.Clock.Tick)).OrderByDescending(b => b.Aspiration == Aspiration.Combat).ThenBy(b => b.Id, StringComparer.Ordinal).Take(3).Select(b => b.Id).ToArray();
        var invitation = w.PartyInvitations.FirstOrDefault(v => v.ToId == a.Id && v.Status == PartyInviteStatus.Pending && v.ExpiresTick > w.Clock.Tick &&
            w.Characters.Any(b => b.Id == v.FromId && b.OrganizationId == a.OrganizationId && b.DemoLocationId == a.DemoLocationId && PartyActions.Available(w, b.Id)));
        var notices = w.Hunts.Where(h => h.OrganizationId == a.OrganizationId && h.DeadlineTick > w.Clock.Tick + 5 && h.Kills < h.Required)
            .Select(h => new HuntNotice(h.Id, h.Target, h.RewardPerKill, h.Participants.Contains(a.Id))).ToArray();
        return new(a.OrganizationId, "expedition:" + a.OrganizationId, w.Clock.Tick, p?.Id, p?.LeaderId, p?.Members.Count ?? 0, rules.MaximumMembers,
            p is not null && p.Members.All(id => w.Characters.Any(c => c.Id == id && c.DemoLocationId == "dungeon" && c.Health >= rules.MinimumHealth) && PartyActions.Available(w, id)), contacts, invitation?.Id, notices);
    }
}
