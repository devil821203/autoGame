using Cultivation.Domain;

namespace Cultivation.Simulation;

public static class AutonomousWorld
{
    public static WorldState Create(ulong seed = 12345, int count = 40, GameConfiguration? configuration = null)
    {
        if (count is < 30 or > 50) throw new ArgumentOutOfRangeException(nameof(count));
        var config = configuration ?? GameConfiguration.Find();
        var w = new WorldState { RandomState = seed == 0 ? 1 : seed };
        config.Initialize(w);
        w.Characters = config.Read<List<CharacterState>>("characters/autonomous.json");
        if (w.Characters.Count != 51 || w.Characters[0].Id != "player" ||
            !w.Characters.Skip(1).Select(c => c.Id).SequenceEqual(Enumerable.Range(0, 50).Select(n => $"npc:{n:D2}")))
            throw new InvalidDataException("自主情境須包含player與依序排列的50個NPC範本");
        w.Characters = w.Characters.Take(count + 1).ToList();
        w.Organizations = config.Read<List<OrganizationState>>("world/organizations.json");
        config.AddStock(w, "autonomous");
        WorldIntegrity.Validate(w);
        return w;
    }
}