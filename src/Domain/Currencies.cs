namespace Cultivation.Domain;

public enum CurrencyKind { Money, SpiritStone }

// ResourceId is absent for a proposed currency: it cannot enter the item/ledger economy.
public sealed record CurrencyDefinition(CurrencyKind Kind, string Name, string? ResourceId, bool Enabled);
