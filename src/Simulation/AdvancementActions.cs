using Cultivation.Domain;

namespace Cultivation.Simulation;

internal static class AdvancementActions
{
    internal static bool Handles(ActionKind kind) => kind is ActionKind.LearnMethod or ActionKind.Breakthrough or ActionKind.EquipWeapon or ActionKind.UnequipWeapon;
    internal static string? Validate(WorldState w, ActionIntent i)
    {
        var rules = w.Advancement;
        if (rules is null) return "Advancement not enabled in this world";
        if (i.Quantity != 1 || i.DurationTicks != 0 || i.TargetId != i.ActorId || i.MissionId is not null) return "Invalid advancement action";
        var actor = w.Characters.Single(c => c.Id == i.ActorId);
        if (w.Scheduled.Any(t => t.Intent.ActorId == actor.Id)) return "Actor busy";
        if (i.Kind == ActionKind.EquipWeapon)
            return i.ResourceId == rules.WeaponId && actor.EquippedWeaponId is null && actor.CraftedWeaponId is null && EquipmentDurabilityActions.LegacyStock(w,actor.Id,rules.WeaponId) >= 1 ? null : "Weapon unavailable or already equipped";
        if (i.Kind == ActionKind.UnequipWeapon)
            return i.ResourceId == rules.WeaponId && actor.EquippedWeaponId is not null ? null : "No matching equipped weapon";
        if (actor.Health < 50) return "Insufficient health for advancement";
        long duration = i.Kind == ActionKind.LearnMethod ? rules.LearningTicks : rules.BreakthroughTicks;
        if (duration > long.MaxValue - w.Clock.Tick) return "Advancement duration overflow";
        if (i.Kind == ActionKind.LearnMethod)
            return i.ResourceId == "spirit-stone" && actor.MethodId is null && Inventory.Balance(w, actor.Id, "spirit-stone") >= rules.LearningCost ? null : "Method already known or insufficient learning funds";
        return i.ResourceId == "pill" && actor.MethodId == rules.MethodId && actor.Realm < rules.MaximumRealm &&
            actor.Cultivation >= CharacterProgression.Threshold(actor, rules) && Inventory.Balance(w, actor.Id, "pill") >= rules.BreakthroughPills ? null : "Breakthrough prerequisites not met";
    }

    internal static void Start(WorldState w, ActionIntent i)
    {
        var rules = w.Advancement!;
        if (i.Kind is ActionKind.EquipWeapon or ActionKind.UnequipWeapon)
        {
            int index = w.Characters.FindIndex(c => c.Id == i.ActorId);
            string? weapon=i.Kind==ActionKind.EquipWeapon?EquipmentDurabilityActions.EquipLegacy(w,i.ActorId,i.Id):null;
            w.Characters[index] = w.Characters[index] with { EquippedWeaponId=weapon,LegacyWeaponBroken=weapon is not null&&!EquipmentCondition.Intact(w,weapon) };
            ActionExecutor.Emit(w, i.Id, "Completed", $"{i.ActorId}:{i.Kind}");
            return;
        }
        long quantity = i.Kind == ActionKind.LearnMethod ? rules.LearningCost : rules.BreakthroughPills;
        long duration = i.Kind == ActionKind.LearnMethod ? rules.LearningTicks : rules.BreakthroughTicks;
        var reserved = i with { Quantity = quantity };
        ActionExecutor.Transfer(w, reserved, i.ActorId, ActionExecutor.Escrow(i.Id), "Reserved");
        w.Scheduled.Add(new(w.NextSequence++, checked(w.Clock.Tick + duration), reserved));
        ActionExecutor.Emit(w, i.Id, "Scheduled", $"{i.ActorId}:{i.Kind}");
    }

    internal static void Complete(WorldState w, ActionIntent i)
    {
        var rules = w.Advancement!;
        int index = w.Characters.FindIndex(c => c.Id == i.ActorId);
        var actor = w.Characters[index];
        ActionExecutor.Transfer(w, i, ActionExecutor.Escrow(i.Id), "consumed", "Consumed");
        w.Characters[index] = i.Kind == ActionKind.LearnMethod ? actor with { MethodId = rules.MethodId } :
            actor with { Realm = actor.Realm + 1, Cultivation = actor.Cultivation - CharacterProgression.Threshold(actor, rules) };
        w.Memories.Add(new(i.ActorId, w.Clock.Tick, i.Kind, true, i.ResourceId, i.Kind == ActionKind.LearnMethod ? -i.Quantity : 0, -i.Quantity, "self:advancement"));
        ActionExecutor.Emit(w, i.Id, "Completed", $"{i.ActorId}:{i.Kind}");
    }
}
