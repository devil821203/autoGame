using Cultivation.Domain;

namespace Cultivation.Simulation;

public static class NewGameFactory
{
    // Rebuild a known scenario instead of partially clearing a live world's interdependent state.
    public static WorldState CreateLike(WorldState current, string playerName, GameConfiguration? configuration = null)
    {
        var config = configuration ?? GameConfiguration.Find();
        if(current.Time?.IndependentLifeEvents==true)return LifeEventScenario.Create(playerName,config);
        if(current.Time?.EquipmentRepair is not null)return EquipmentRepairScenario.Create(playerName,config);
        if(current.Time?.PlayerControl is not null)return PlayerControlScenario.Create(playerName,current.HexMap?.Regional?.Seed,config);
        if(current.HexMap?.Regional is {} regions)return RegionalWorldScenario.Create(playerName,regions.Seed,config);
        WorldState fresh;
        if (current.SettlementOrganizationDemo is not null) fresh = SettlementOrganizationScenario.Create(playerName, config);
        else if (current.SettlementRules is not null) fresh = SettlementScenario.Create(playerName, config);
        else if (current.Methods is not null) fresh = MethodTrainingScenario.Create(playerName, config);
        else if (current.MortalActionRules is not null) fresh = MortalCultivationScenario.Create(playerName, config);
        else if (current.LifeSkillDemo || current.LifeSkillRules is not null && !current.MedicineOnly) fresh = LifeSkillScenario.Create(playerName, config);
        else if (current.Time is not null) fresh = TimeScenario.Create(playerName, config);
        else if (current.BoardBattle is not null) fresh = BoardBattleScenario.Create(playerName, config);
        else if (current.PlayerAutoSchedule) fresh = PlayerAutoScheduleScenario.Create(playerName, config);
        else if (current.StaminaRules is not null) fresh = GrowthScenario.Create(playerName, configuration: config);
        else if (current.HexMap is not null) fresh = HexWorldScenario.Create(playerName, configuration: config);
        else if (current.ScaleNpcCount is { } count) fresh = ScaleScenario.Create(count, configuration: config);
        else if (current.TestProcurement is not null || current.SectFacilities is not null)
            fresh = SectEconomyScenario.Create(playerName, configuration: config);
        else if (current.DemoMapEnabled) fresh = PlayerDemoScenario.Create(playerName, configuration: config);
        else if (current.SocialRules is not null) fresh = SocialScenario.Create(configuration: config);
        else if (current.CombatRules is not null) fresh = CombatScenario.Create(configuration: config);
        else if (current.Advancement is not null) fresh = AdvancementScenario.Create(configuration: config);
        else if (current.MarketListings.Count > 0) fresh = MissionScenario.Create(configuration: config);
        else if (current.Characters.Any(c => c.Id.StartsWith("npc:", StringComparison.Ordinal)))
            fresh = AutonomousWorld.Create(count: Math.Clamp(current.Characters.Count(c => c.Id.StartsWith("npc:", StringComparison.Ordinal)), 30, 50), configuration: config);
        else fresh = DemoWorld.Create(configuration: config);
        string name = playerName?.Trim() ?? "";
        if (name.Length is < 1 or > 24 || name.Any(char.IsControl)) throw new ArgumentException("角色名稱須為1–24字，且不可包含控制字元。", nameof(playerName));
        int index = fresh.Characters.FindIndex(c => c.Id == fresh.ControlledCharacterId);
        fresh.Characters[index] = fresh.Characters[index] with { Name = name };
        if (current.MedicineOnly) MedicineOnlyProfile.ApplyFresh(fresh, config);
        if (current.Time?.ActionDriven is not null && fresh.Time is { } time) {
            MinuteTimeProfile.EnableFresh(fresh, config);
        }
        WorldIntegrity.Validate(fresh);
        if (current.Time?.DiscreteEvents is not null) DiscreteTime.EnableFresh(fresh, config);
        if (current.Time?.ActionRuntime is not null) ActionLifecycle.EnableFresh(fresh);
        if (current.Time?.EventDecisions is not null) EventDecisions.EnableFresh(fresh);
        if (current.Time?.Content is not null) ContentProfile.EnableFresh(fresh,config);
        if(current.Time?.MarketOrders is not null)MarketOrders.EnableFresh(fresh,config);
        return fresh;
    }
}




