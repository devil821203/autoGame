using Cultivation.Domain;

namespace Cultivation.Simulation;

internal static class CurrencyIntegrity
{
    internal static void Validate(WorldState w)
    {
        // Legacy saves retain their catalog and economy; no new config is loaded.
        if (w.Currencies is not { } definitions) return;
        if (definitions.Count != Enum.GetValues<CurrencyKind>().Length ||
            definitions.Select(c => c.Kind).Distinct().Count() != definitions.Count ||
            definitions.Any(c => !Enum.IsDefined(c.Kind) || string.IsNullOrWhiteSpace(c.Name)))
            throw new InvalidDataException("items/currencies.json: invalid Kind/Name or duplicate classification");
        foreach (var c in definitions)
        {
            if (c.Kind == CurrencyKind.SpiritStone && (c.Enabled || c.ResourceId is not null))
                throw new InvalidDataException("items/currencies.json: SpiritStone.Enabled/ResourceId must remain disabled/null until rules are confirmed");
            if (c.Kind == CurrencyKind.Money && (!c.Enabled || c.ResourceId != "spirit-stone" ||
                !w.Items.Any(i => i.Id == c.ResourceId && i.Name == c.Name && i.Category == "貨幣")))
                throw new InvalidDataException("items/currencies.json: Money.Enabled/ResourceId/Name must match the existing money catalog");
        }
    }
}
