using Cultivation.Domain;

namespace Cultivation.Simulation;

// Called only on the simulation's private working copy.
internal static class ActionExecutor
{
    internal static string Escrow(string intentId) => "$escrow:" + intentId;

    internal static void Transfer(WorldState world, ActionIntent intent, string from, string to, string reason)
    {
        world.Ledger.Add(new(world.NextSequence++, world.Clock.Tick, intent.Id,
            from, to, intent.ResourceId, intent.Quantity, reason));
    }

    internal static void Execute(WorldState world, ActionIntent intent)
    {
        if (intent.Kind == ActionKind.Wait) { Emit(world, intent.Id, "WaitCompleted", intent.ActorId); return; }
        if (intent.Kind == ActionKind.SetBoardAutoCombat) { world.BoardBattle!.PlayerAutoCombat = intent.Quantity == 1; Emit(world, intent.Id, "PlayerAutoCombatChanged", intent.Quantity == 1 ? "On" : "Off"); return; }
        if (intent.Kind == ActionKind.BoardTurn) { world.BoardBattle!.Orders.Add(new(intent.ActorId, intent.BoardTurn!)); return; }
        StaminaActions.Start(world, intent);
        if (intent.Kind == ActionKind.Rest) return;
        if(intent.Kind==ActionKind.InvestSettlementPopulation){PopulationInvestmentActions.Execute(world,intent);return;}
        if(OrganizationLaborActions.Handles(intent.Kind)){OrganizationLaborActions.Execute(world,intent);return;}
        if (ExternalMarketActions.Handles(intent.Kind)) { ExternalMarketActions.Execute(world,intent); return; }
        if (SettlementActions.Handles(intent.Kind)) { SettlementActions.Execute(world, intent); return; }
        if (intent.Kind == ActionKind.RequestLifeService) { PaidLifeServices.Start(world, intent); return; }
        if (MortalTrainingActions.Handles(intent.Kind)) { MortalTrainingActions.Start(world, intent); return; }
        if (LifeActions.Handles(intent.Kind)) { LifeActions.Execute(world, intent); return; }
        if (HexBuildingActions.Handles(intent.Kind)) { HexBuildingActions.Execute(world, intent); return; }
        if (PartyActions.Handles(intent.Kind)) { PartyActions.Execute(world, intent); return; }
        if (intent.Kind == ActionKind.AcceptHunt) { HuntActions.Accept(world, intent); return; }
        if (intent.Kind == ActionKind.Travel) { TravelActions.Start(world, intent); return; }
        if (intent.Kind == ActionKind.ExploreDungeon) { DungeonActions.Execute(world, intent); return; }
        if (SectFacilityActions.Handles(intent.Kind)) { SectFacilityActions.Execute(world, intent); return; }
        if (CombatActions.Handles(intent.Kind)) { CombatActions.Execute(world, intent); return; }
        if (GovernanceActions.Handles(intent.Kind)) { GovernanceActions.Execute(world, intent); return; }
        if (SocialActions.Handles(intent.Kind)) { SocialActions.Execute(world, intent); return; }
        if (MissionActions.IsMission(intent.Kind)) { MissionActions.Execute(world, intent); return; }
        if (AdvancementActions.Handles(intent.Kind)) { AdvancementActions.Start(world, intent); return; }
        if (intent.Kind != ActionKind.Transfer) { ProductionActions.Start(world, intent); return; }
        if (intent.DurationTicks == 0)
        {
            Transfer(world, intent, intent.ActorId, intent.TargetId, "Transfer");
            Emit(world, intent.Id, "Completed", "Resource transfer completed");
            return;
        }
        Transfer(world, intent, intent.ActorId, Escrow(intent.Id), "Reserved");
        world.Scheduled.Add(new(world.NextSequence++, checked(world.Clock.Tick + intent.DurationTicks), intent));
        Emit(world, intent.Id, "Scheduled", "Resources reserved until completion");
    }

    internal static void Emit(WorldState world, string id, string kind, string detail) =>
        world.Events.Add(new(world.NextSequence++, world.Clock.Tick, id, kind, detail));
}



