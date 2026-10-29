namespace Cultivation.Domain;

public enum TestProfession { None, Physician, Pharmacist, Smith, SpiritTraining, BodyTraining, QiTraining }
public sealed record SpecialistDefinition(string ActorId, string Name, TestProfession Profession, string LocationId,
    Dictionary<string, long> InitialStock);
public sealed record SpecialistRules(double WorkUtility, double SupplyUtility, int MedicineStockTarget,
    List<SpecialistDefinition> Characters);
