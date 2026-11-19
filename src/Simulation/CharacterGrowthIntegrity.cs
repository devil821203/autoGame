using Cultivation.Domain;

namespace Cultivation.Simulation;

internal static class CharacterGrowthIntegrity
{
    internal static void Validate(WorldState world)
    {
        if (world.MindsetRules is not { } r)
        {
            if (world.Characters.Any(c => c.Mindset is not null)) throw new InvalidDataException("Mindset requires rules");
            return;
        }
        if (r.Minimum != 0 || r.Maximum != 100 || r.Initial < r.Minimum || r.Initial > r.Maximum ||
            !double.IsFinite(r.BaseMultiplier) || r.BaseMultiplier is < 0 or > 10 ||
            !double.IsFinite(r.MultiplierPerPoint) || r.MultiplierPerPoint is < 0 or > 0.1 ||
            world.Characters.Any(c => c.Mindset is null || c.Mindset < r.Minimum || c.Mindset > r.Maximum))
            throw new InvalidDataException("Invalid mindset state or rules");
    }
}
