using Cultivation.Domain;
using Cultivation.AI;

namespace Cultivation.Simulation;

public static class PaidLifeServices
{
    internal static string Account(string id) => ActionExecutor.Escrow("service-fee:" + id);
    internal static ActionIntent ProviderIntent(ActionIntent i) => new(i.Id, i.TargetId,
        i.ResourceId is "medical" or "severe-injury" ? i.ActorId : i.TargetId,
        i.ResourceId == "medical" ? "herb" : i.ResourceId, 1, 0, DecisionSource.Npc,
        i.ResourceId == "medical" ? ActionKind.MedicalTreatment : i.ResourceId == "severe-injury" ? ActionKind.TreatSevereInjury : ActionKind.CraftLifeItem);
    public static long Quote(WorldState w, string service, ServiceMaterialSource source)
    {
        var r = w.PaidServiceRules ?? throw new InvalidOperationException("Paid services unavailable");
        if (!Enum.IsDefined(source) || !r.LaborFees.TryGetValue(service, out long fee)) throw new ArgumentException("Unknown service or material source");
        var fake = new ActionIntent("quote", "client", "provider", service, 1, 0, DecisionSource.Player);
        if (source == ServiceMaterialSource.Provider)
            foreach (var input in LifeActions.Inputs(w, ProviderIntent(fake))) fee = checked(fee + checked(input.Value * r.MaterialPrices[input.Key]));
        return fee;
    }
    public static bool Matches(TestProfession profession, string service) => profession switch {
        TestProfession.Physician => service is "medical" or "severe-injury",
        TestProfession.Pharmacist => service is "ointment" or "bleeding-powder",
        TestProfession.Smith => service is "iron-sword" or "leather-armor", _ => false };
    internal static bool Present(CharacterState a, CharacterState b) => a.RegionId == b.RegionId && a.DemoLocationId == b.DemoLocationId && a.HexPosition == b.HexPosition;
    internal static bool Need(WorldState w, CharacterState a, string service) => service switch {
        "medical" => !a.SeverelyInjured && a.Health is > 0 and < 100,
        "severe-injury" => a.SeverelyInjured,
        "ointment" or "bleeding-powder" => Inventory.Balance(w, a.Id, service) < w.SpecialistRules!.MedicineStockTarget,
        "iron-sword" => a.EquippedWeaponId is null && a.CraftedWeaponId is null && !w.LifeSystem!.Equipment.Any(g => g.Slot == "weapon" && Inventory.Balance(w, a.Id, g.Id) > 0),
        "leather-armor" => a.EquippedArmorId is null && !w.LifeSystem!.Equipment.Any(g => g.Slot == "armor" && Inventory.Balance(w, a.Id, g.Id) > 0), _ => false };
    internal static bool Busy(WorldState w, string id) => w.Scheduled.Any(t => t.Intent.ActorId == id) ||
        w.MortalTrainingWork.Any(t => t.Intent.ActorId == id) || w.LifeSystem?.Work.Any(t => t.Intent.ActorId == id || t.Service?.ClientId == id) == true ||
        w.Time is not null && (TimeActions.Budget(w, id).BusyThroughTick > w.Clock.Tick || w.Time.Deferred.Any(t => t.Intent.ActorId == id));
    internal static string? Validate(WorldState w, ActionIntent i)
    {
        if (w.PaidServiceRules is null || w.SpecialistRules is null || w.LifeSystem is null || w.LifeSkillRules is null) return "Paid services not enabled";
        var client = w.Characters.Single(c => c.Id == i.ActorId); var provider = w.Characters.SingleOrDefault(c => c.Id == i.TargetId);
        if (provider is null || provider.Id == client.Id || provider.Hostile || client.Hostile || !Present(client, provider) || provider.Health <= 0 || provider.SeverelyInjured ||
            !Matches(provider.TestProfession, i.ResourceId) || !Need(w, client, i.ResourceId) || i.ServiceMaterials is not { } source || !Enum.IsDefined(source) ||
            i.Quantity != 1 || i.DurationTicks != 0 || i.MissionId is not null || i.BoardTurn is not null || i.Plan is not null && i.Plan.Steps.Count != 1)
            return "Invalid service, provider, need or location";
        if (Busy(w, provider.Id) || w.LifeSystem.Work.Any(t => t.Service?.ClientId == client.Id) || CombatActions.Active(w, provider.Id) is not null ||
            BoardBattleActions.Participant(w, provider.Id)) return "Service provider/client busy";
        var job = ProviderIntent(i);
        if (StaminaActions.Validate(w, job) is not null) return "Service provider must rest";
        int cost = LifeActions.Cost(w, job);
        if (w.Time is not null && TimeActions.Budget(w, provider.Id).Remaining < cost) return "Service provider lacks AP";
        long price;
        try { price = Quote(w, i.ResourceId, source); } catch (Exception e) when (e is KeyNotFoundException or OverflowException or ArgumentException) { return "Invalid service price"; }
        if (i.LimitPrice is null || price > i.LimitPrice || Inventory.Balance(w, client.Id, "spirit-stone") < price) return "Insufficient service funds or quote exceeds limit";
        string owner = source == ServiceMaterialSource.Client ? client.Id : provider.Id;
        if (LifeActions.Inputs(w, job).Any(p => Inventory.Balance(w, owner, p.Key) < p.Value)) return "Missing service materials";
        if (job.Kind == ActionKind.CraftLifeItem && !InventoryCapacity.CanReceive(w, client.Id, w.LifeSystem.Recipes.Single(r => r.Id == i.ResourceId).Output, 1)) return "Client inventory capacity exceeded";
        return null;
    }
    internal static void Start(WorldState w, ActionIntent i)
    {
        var source = i.ServiceMaterials!.Value; var job = ProviderIntent(i); long price = Quote(w, i.ResourceId, source);
        ActionExecutor.Transfer(w, i with { ResourceId = "spirit-stone", Quantity = price }, i.ActorId, Account(i.Id), "ServiceFeeReserved");
        if (source == ServiceMaterialSource.Client)
            foreach (var p in LifeActions.Inputs(w, job)) ActionExecutor.Transfer(w, i with { ResourceId = p.Key, Quantity = p.Value }, i.ActorId, i.TargetId, "ServiceMaterialsSupplied");
        if (w.Time is not null) TimeActions.Spend(w, "service-work:" + i.Id, i.TargetId, LifeActions.Cost(w, job));
        LifeActions.Execute(w, job);
        int n = w.LifeSystem!.Work.FindIndex(t => t.Intent.Id == i.Id); var client = w.Characters.Single(c => c.Id == i.ActorId);
        w.LifeSystem.Work[n] = w.LifeSystem.Work[n] with { Service = new(i.ActorId, i.TargetId, source, i.ResourceId, price, w.Clock.Tick, client.DemoLocationId, client.HexPosition) };
        ActionExecutor.Emit(w, i.Id, "PaidServiceStarted", $"{i.ActorId}:{i.TargetId}:{i.ResourceId}:{source}:保留{price}錢財");
    }
    internal static bool ValidCompletion(WorldState w, LifeWork task)
    {
        if (task.Service is not { } c) return true;
        var client = w.Characters.Single(a => a.Id == c.ClientId); var provider = w.Characters.Single(a => a.Id == c.ProviderId);
        return !client.Hostile && !provider.Hostile && Present(client, provider) && client.DemoLocationId == c.LocationId && client.HexPosition == c.Position &&
            provider.Health > 0 && !provider.SeverelyInjured && (client.Health > 0 || c.ServiceId == "severe-injury") &&
            CombatActions.Active(w, client.Id) is null && CombatActions.Active(w, provider.Id) is null && !BoardBattleActions.Participant(w, client.Id) && !BoardBattleActions.Participant(w, provider.Id) && Need(w, client, c.ServiceId);
    }
    internal static void Settle(WorldState w, LifeWork task, bool success)
    {
        if (task.Service is not { } c) return;
        string? output = null;
        if (success && task.Intent.Kind == ActionKind.CraftLifeItem)
        {
            output = w.LifeSystem!.Receipts.Single(r => r.ActionId == task.Intent.Id).Output;
            ActionExecutor.Transfer(w, task.Intent with { ResourceId = output, Quantity = 1 }, c.ProviderId, c.ClientId, "PaidServiceDelivery");
        }
        if (!success && c.MaterialSource == ServiceMaterialSource.Client)
            foreach (var p in task.Inputs) ActionExecutor.Transfer(w, task.Intent with { ResourceId = p.Key, Quantity = p.Value }, c.ProviderId, c.ClientId, "ServiceMaterialsRefunded");
        ActionExecutor.Transfer(w, task.Intent with { ResourceId = "spirit-stone", Quantity = c.Fee }, Account(task.Intent.Id), success ? c.ProviderId : c.ClientId, success ? "PaidServicePayment" : "ServiceFeeRefunded");
        w.PaidServiceReceipts.Add(new(task.Intent.Id, c, success, w.Clock.Tick, output));
        ActionExecutor.Emit(w, task.Intent.Id, success ? "PaidServiceCompleted" : "PaidServiceRefunded", $"{c.ClientId}:{c.ProviderId}:{c.ServiceId}:{c.Fee}錢財");
    }
    internal static void Interrupt(WorldState w, string actorId)
    {
        foreach (var task in w.LifeSystem?.Work.Where(t => t.Service is { } c && (c.ClientId == actorId || c.ProviderId == actorId)).ToArray() ?? [])
            LifeActions.CancelWork(w, task);
    }
    internal static IEnumerable<ScoredAction> Evaluate(WorldState w, CharacterState a, DecisionSource source)
    {
        if (w.PaidServiceRules is not { } rules || w.SpecialistRules is not { } specialist || a.Hostile || a.MonsterKind != MonsterKind.None) yield break;
        foreach (var provider in w.Characters.Where(p => p.Id != a.Id && !p.Hostile && p.TestProfession is TestProfession.Physician or TestProfession.Pharmacist or TestProfession.Smith && Present(a, p)).OrderBy(p => p.Id, StringComparer.Ordinal))
        foreach (string service in rules.LaborFees.Keys.Where(s => Matches(provider.TestProfession, s) && Need(w, a, s)))
        foreach (var materials in Enum.GetValues<ServiceMaterialSource>())
        {
            if (a.TestProfession is TestProfession.Physician or TestProfession.Pharmacist or TestProfession.Smith && service is not ("medical" or "severe-injury")) continue;
            long quote = Quote(w, service, materials);
            var i = new ActionIntent($"service:{a.Id}:{w.Clock.Tick}:{provider.Id}:{service}:{materials}", a.Id, provider.Id, service, 1, 0, source,
                ActionKind.RequestLifeService, LimitPrice: quote, ServiceMaterials: materials);
            double score = service is "medical" or "severe-injury" ? rules.MedicalUtility + (100 - a.Health) : specialist.SupplyUtility;
            yield return new(i, score / (1 + quote / (double)Math.Max(1, Inventory.Balance(w, a.Id, "spirit-stone"))), "同地專職服務，需求／資金／材料經共用驗證；報價" + quote);
        }
    }
}
