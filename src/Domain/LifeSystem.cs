namespace Cultivation.Domain;

public enum EquipmentQuality { Ordinary, Fine, Superior }
public sealed record LifeRecipe(string Id, LifeSkillKind Skill, Dictionary<string,long> Inputs, string Output,
    int Ticks, string? Slot = null, int Attack = 0, int Defense = 0);
public sealed record LifeOptions(int TreatmentTicks = 2, int SevereTreatmentTicks = 5, int SevereHerbs = 3,
    int SevereOintment = 1, int SevereHealing = 20, int ExperiencePerCompletion = 10,
    double FineChance = .25, double SuperiorChance = .05, double QualityChancePerLevel = .03,
    double FineMultiplier = 1.25, double SuperiorMultiplier = 1.5);
public sealed record LifeWork(ActionIntent Intent, long DueTick, Dictionary<string,long> Inputs, PaidServiceContract? Service = null);
public sealed record LifeProductionReceipt(string ActionId, string ActorId, long Tick, string RecipeId, string Output);
public sealed record CraftedEquipment(string Id, string DefinitionId, EquipmentQuality Quality, string Slot, int Attack, int Defense, string ActionId);
public sealed record MedicineEffect(string Id, string ActorId, int Remaining, int Healing, string? BattleId, int NextRound, long NextTick);
public sealed record CombatMedicineUse(string BattleId, string ActorId, int Round);
public sealed class LifeSystemState
{
    public List<string> FinishedBattles { get; set; } = [];
    public LifeOptions Options { get; set; } = new();
    public List<LifeRecipe> Recipes { get; set; } = [];
    public List<LifeWork> Work { get; set; } = [];
    public List<LifeProductionReceipt> Receipts { get; set; } = [];
    public List<CraftedEquipment> Equipment { get; set; } = [];
    public List<MedicineEffect> Effects { get; set; } = [];
    public List<CombatMedicineUse> CombatUses { get; set; } = [];
}
