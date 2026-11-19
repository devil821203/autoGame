using Cultivation.Domain;

namespace Cultivation.Simulation;

public static partial class CultivationMethodConfiguration
{
    // Explicit opt-in only; never activates methods in an existing world.
    public static CultivationMethodCatalog Read(GameConfiguration configuration)
    {
        var catalog = configuration.Read<CultivationMethodCatalog>(Path);
        Validate(catalog);
        return catalog;
    }
}
