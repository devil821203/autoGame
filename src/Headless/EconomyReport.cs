using System.Text.Json;
using Cultivation.Domain;
using Cultivation.Simulation;

namespace Cultivation.Headless;

public static class EconomyReport
{
    public static void Run(int ticks, ulong seed, bool hex = false)
    {
        var e = new SimulationEngine(MedicineOnlyProfile.ApplyFresh(hex ? HexWorldScenario.Create(seed: seed) : SectEconomyScenario.Create(seed: seed)));
        for (int n = 0; n < ticks; n++) e.Step(true);
        var w = e.Snapshot();
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            seed, ticks, hex, w.MedicineOnly, RecoveryItem=w.RecoveryResourceId, MedicinesProduced=w.LifeSystem?.Receipts.Count ?? 0, Members = w.Characters.Count(c => c.OrganizationId == "sect"),
            WolfHideChain = new
            {
                Spawned = w.Ledger.Where(l => l.From == "$dungeon" && l.ResourceId == "beast-hide").Sum(l => l.Quantity),
                Looted = w.Ledger.Where(l => l.ResourceId == "beast-hide" && w.Characters.Any(c => c.Id == l.From && c.MonsterKind == MonsterKind.Wolf) && w.Characters.Any(c => c.Id == l.To && c.MonsterKind == MonsterKind.None)).Sum(l => l.Quantity),
                Delivered = w.Deliveries.Where(d => w.Missions.Any(m => m.Id == d.MissionId && m.ResourceId == "beast-hide")).Sum(d => d.Quantity),
                SectStock = Inventory.Balance(w, "sect", "beast-hide"),
                Sold = w.ProcurementReceipts.Where(r => r.ResourceId == "beast-hide").Sum(r => r.Quantity),
                Income = w.ProcurementReceipts.Where(r => r.ResourceId == "beast-hide").Sum(r => r.Payment),
                Holders = w.Characters.Where(c => c.MonsterKind == MonsterKind.None && Inventory.Balance(w, c.Id, "beast-hide") > 0).Select(c => new { c.Id, c.OrganizationId, c.DemoLocationId, Hides = Inventory.Balance(w, c.Id, "beast-hide") }),
                Missions = w.Missions.Where(m => m.ResourceId == "beast-hide").Select(m => new { m.Id, m.Status, m.Delivered, m.Participants.Count })
            },
            Herbs = Inventory.Balance(w, "sect", "herb"), Pills = Inventory.Balance(w, "sect", "pill"), Stones = Inventory.Balance(w, "sect", "spirit-stone"),
            Gathered = w.SectWorkReceipts.Count(r => r.Kind == ActionKind.SectGather && r.Success), Brewed = w.SectWorkReceipts.Count(r => r.Kind == ActionKind.SectBrew && r.Success),
            Redeemed = w.Ledger.Where(l => l.Reason == "ContributionRedemption").Sum(l => l.Quantity),
            Procured = w.ProcurementReceipts.Sum(r => r.Quantity), Income = w.ProcurementReceipts.Sum(r => r.Payment),
            Payroll = w.Events.Count(v => v.Kind == "PayrollPaid"), MissedPayroll = w.Events.Count(v => v.Kind == "PayrollSkipped"),
            Parties = w.Parties.Count, Invitations = w.PartyInvitations.Count, TeamBattles = w.Combats.Count(b => b.Allies is { Count: > 0 }),
            Hunts = w.Hunts.Sum(h => h.Kills), HuntRewards = w.HuntReceipts.Sum(r => r.Contribution),
            HuntAcceptances = w.Events.Count(v => v.Kind == "HuntAccepted"), PartyHistory = w.Events.Where(v => v.Kind.StartsWith("Party")).TakeLast(12).Select(v => $"{v.Tick}:{v.Kind}:{v.Detail}"),
            Injuries = w.Characters.Count(c => !c.Hostile && c.Health < 30), Expelled = w.Events.Count(v => v.Kind == "DutyExpelled"),
            RecentGathered = w.SectWorkReceipts.Count(r => r.Tick > ticks / 2 && r.Kind == ActionKind.SectGather && r.Success),
            RecentBrewed = w.SectWorkReceipts.Count(r => r.Tick > ticks / 2 && r.Kind == ActionKind.SectBrew && r.Success),
            RecentProcurement = w.ProcurementReceipts.Where(r => r.Tick > ticks / 2).Sum(r => r.Quantity),
            PartyDetails = w.Parties,
            Roles = w.Characters.Where(c => c.OrganizationId == "sect" || c.Aspiration == Aspiration.Combat && !c.Hostile).Select(c => new { c.Id, c.DemoLocationId, c.Health, c.Cultivation, c.DesiredCultivation,
                Plan = w.Plans.FirstOrDefault(p => p.ActorId == c.Id),
                Points = Inventory.Balance(w, c.Id, "contribution"), Pills = Inventory.Balance(w, c.Id, "pill"),
                Last = w.Events.LastOrDefault(v => v.Kind == "Decision" && v.ActionId.StartsWith("ai:" + c.Id + ":", StringComparison.Ordinal))?.Detail })
        }));
    }
}
