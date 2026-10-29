namespace Cultivation.Domain;

public enum ServiceMaterialSource { Client, Provider }
public sealed record PaidServiceRules(int RequestActionPoints, double MedicalUtility, Dictionary<string, long> LaborFees,
    Dictionary<string, long> MaterialPrices);
public sealed record PaidServiceContract(string ClientId, string ProviderId, ServiceMaterialSource MaterialSource,
    string ServiceId, long Fee, long StartedTick, string LocationId, HexCoord? Position);
public sealed record PaidServiceReceipt(string ActionId, PaidServiceContract Contract, bool Success, long Tick, string? Output);
