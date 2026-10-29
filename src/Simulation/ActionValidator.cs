using Cultivation.Domain;

namespace Cultivation.Simulation;

public static class ActionValidator
{
    public static string? Validate(WorldState world, ActionIntent intent)
        => ValidateCore(world, intent, true);

    internal static string? ValidateForTravel(WorldState world, ActionIntent intent)
        => ValidateCore(world, intent, false);

    private static string? ValidateCore(WorldState world, ActionIntent intent, bool checkLocation)
    {
        if (string.IsNullOrWhiteSpace(intent.Id) || intent.Id.StartsWith("$", StringComparison.Ordinal)) return "Invalid intent ID";
        if (!Enum.IsDefined(intent.Source)) return "Invalid decision source";
        if(intent.Kind is not (ActionKind.SettlementBuy or ActionKind.SettlementSell)&&intent.SettlementShopId is not null)return "Unexpected settlement shop selection";
        if (intent.Kind != ActionKind.BuildSettlement && intent.SettlementBuildPosition is not null) return "Unexpected settlement build position";
        if (!world.Characters.Any(c => c.Id == intent.ActorId)) return "Unknown actor";
        if (intent.Source == DecisionSource.Player && world.ControlledCharacterId != intent.ActorId) return "Actor is not player controlled";
        if (intent.Source == DecisionSource.Npc && world.ControlledCharacterId == intent.ActorId) return "Actor is not AI controlled";
        if (world.Characters.Single(c => c.Id == intent.ActorId) is { Health: 0 } incapacitated && !(world.LifeSystem is not null && incapacitated.SeverelyInjured && (intent.Kind == ActionKind.TreatSevereInjury || intent.Kind == ActionKind.RequestLifeService && intent.ResourceId == "severe-injury"))) return "Actor incapacitated";
        if (intent.Plan is not null && (intent.Plan.ActorId != intent.ActorId || intent.Plan.Steps.Count == 0 || intent.Plan.Steps[0] != intent.Kind || intent.Plan.Steps.Any(s => !Enum.IsDefined(s)))) return "Invalid plan";
        if (intent.Kind == ActionKind.UseCombatMedicine) return LifeActions.Validate(world, intent);
        if (intent.Kind == ActionKind.SetBoardAutoCombat) return BoardBattleActions.ValidateAutoCombat(world, intent);
        if (intent.Kind == ActionKind.BoardTurn) return BoardBattleActions.Validate(world, intent);
        if (world.MedicineOnly && (intent.ResourceId == "pill" || intent.Kind is ActionKind.Cultivate or ActionKind.Breakthrough or ActionKind.Heal or ActionKind.CombatHeal or ActionKind.PowerStrike)) return "Pills removed; former pill-consuming actions paused";
        if (world.MortalTrainingWork.Any(t => t.Intent.ActorId == intent.ActorId)) return "Actor busy in mortal training";
        if (world.LifeSystem?.Work.Any(t=>t.Intent.ActorId==intent.ActorId)==true) return "Actor busy in life work";
        if (world.LifeSystem?.Work.Any(t => t.Service?.ClientId == intent.ActorId) == true) return "Actor awaiting commissioned service";
        if (intent.Kind != ActionKind.RequestLifeService && intent.ServiceMaterials is not null) return "Unexpected service payload";
        if (!world.AlchemyEnabled && (intent.Kind is ActionKind.Brew or ActionKind.SectBrew || intent.Kind == ActionKind.AcceptSectWork && intent.ResourceId == "pill")) return "Alchemy disabled in this test world";
        if (intent.BoardTurn is not null) return "Unexpected board order payload";
        if (BoardBattleActions.Participant(world, intent.ActorId)) return "Actor in board battle";
        var timeError = TimeActions.Validate(world, intent);
        if (timeError is not null) return timeError;
        var staminaError = StaminaActions.Validate(world, intent);
        if (staminaError is not null) return staminaError;
        if (MortalTrainingActions.Handles(intent.Kind)) return MortalTrainingActions.Validate(world, intent);
        if (intent.Kind == ActionKind.RequestLifeService) return CombatActions.Active(world, intent.ActorId) is not null ? "Client in combat" : PaidLifeServices.Validate(world, intent);
        if (LifeActions.Handles(intent.Kind)) return LifeActions.Validate(world, intent);
        if (CombatActions.Handles(intent.Kind)) return CombatActions.Validate(world, intent);
        if (CombatActions.Active(world, intent.ActorId) is not null) return "Actor in combat";
        if (world.Scheduled.Any(t => t.Intent.ActorId == intent.ActorId && t.Intent.Kind == ActionKind.Travel)) return "Actor traveling";
        if (intent.Kind == ActionKind.Rest) return null;
        if (intent.Kind == ActionKind.Wait) return world.Time?.ActionDriven is null || intent.TargetId != intent.ActorId || intent.ResourceId != "time" ||
            intent.Quantity != 1 || intent.DurationTicks != 0 || intent.MissionId is not null ? "Invalid wait action" : null;
        if(intent.Kind==ActionKind.InvestSettlementPopulation)return PopulationInvestmentActions.Validate(world,intent);
        if(OrganizationLaborActions.Handles(intent.Kind))return OrganizationLaborActions.Validate(world,intent);
        if (ExternalMarketActions.Handles(intent.Kind)) return ExternalMarketActions.Validate(world,intent);
        if (SettlementActions.Handles(intent.Kind)) return SettlementActions.Validate(world, intent);
        if (intent.Kind == ActionKind.Travel) return TravelActions.Validate(world, intent);
        if (HexBuildingActions.Handles(intent.Kind)) return HexBuildingActions.Validate(world, intent);
        if (intent.Kind == ActionKind.Transfer && intent.ResourceId == "contribution") return "Contribution is non-transferable";
        if (world.SectFacilities is not null && intent.Kind is ActionKind.Gather or ActionKind.Brew) return "Sect facilities require accepted sect work";
        if (checkLocation && world.DemoMapEnabled && TravelActions.RequiredLocation(intent.Kind) is { } location &&
            world.Characters.Single(c => c.Id == intent.ActorId).DemoLocationId != location &&
            !HexBuildingActions.HasService(world, intent.ActorId, location)) return "Action requires demo location: " + location;
        if (intent.Kind == ActionKind.ExploreDungeon) return DungeonActions.Validate(world, intent);
        if (PartyActions.Handles(intent.Kind)) return PartyActions.Validate(world, intent);
        if (intent.Kind == ActionKind.AcceptHunt) return HuntActions.Validate(world, intent);
        if (SectFacilityActions.Handles(intent.Kind)) return SectFacilityActions.Validate(world, intent);
        if (GovernanceActions.Handles(intent.Kind)) return GovernanceActions.Validate(world, intent);
        if (SocialActions.Handles(intent.Kind)) return SocialActions.Validate(world, intent);
        if (MissionActions.IsMission(intent.Kind)) return MissionActions.Validate(world, intent);
        if (AdvancementActions.Handles(intent.Kind)) return AdvancementActions.Validate(world, intent);
        if (intent.MissionId is not null)
        {
            var mission = world.Missions.SingleOrDefault(m => m.Id == intent.MissionId);
            if (mission is null || mission.Status != MissionStatus.Active || mission.DeadlineTick <= world.Clock.Tick || !mission.Participants.Contains(intent.ActorId)) return "Mission plan unavailable";
            long duration = ProductionActions.Duration(world, intent.Kind);
            if (duration >= mission.DeadlineTick - world.Clock.Tick - 1) return "Mission plan cannot finish before deadline";
        }
        if (intent.Kind != ActionKind.Transfer) return ProductionActions.Validate(world, intent);
        if (intent.TargetId == intent.ActorId) return "Self transfer is not allowed";
        if (!world.Characters.Any(c => c.Id == intent.TargetId) && !world.Organizations.Any(o => o.Id == intent.TargetId)) return "Unknown recipient";
        if (string.IsNullOrWhiteSpace(intent.ResourceId) || intent.Quantity <= 0) return "Invalid resource or quantity";
        if (intent.DurationTicks < 0 || intent.DurationTicks > long.MaxValue - world.Clock.Tick) return "Invalid duration";
        if (Inventory.Balance(world, intent.ActorId, intent.ResourceId) < intent.Quantity) return "Insufficient resources";
        if (world.Characters.Single(c => c.Id == intent.ActorId) is var owner && (owner.EquippedWeaponId == intent.ResourceId || owner.CraftedWeaponId == intent.ResourceId || owner.EquippedArmorId == intent.ResourceId)) return "Unequip weapon before transferring it";
        if (!InventoryCapacity.CanReceive(world, intent.TargetId, intent.ResourceId, intent.Quantity)) return "Recipient capacity exceeded";
        return null;
    }
}




