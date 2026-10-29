namespace Cultivation.Domain;

public sealed record RegenerationRules(long IntervalTicks = 10, long Amount = 2, long Capacity = 10000);
