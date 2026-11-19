using Cultivation.Domain;

namespace Cultivation.Simulation;

public sealed record DemoStartingFunds(long MinimumStones, long StoneRange);

public static class DemoWorld
{
    public static WorldState Create(ulong seed = 12345, GameConfiguration? configuration = null)
    {
        var config = configuration ?? GameConfiguration.Find();
        var funds = config.Read<DemoStartingFunds>("economy/demo-start.json");
        if (funds.MinimumStones <= 0 || funds.StoneRange <= 0 || funds.MinimumStones > long.MaxValue - funds.StoneRange) throw new InvalidDataException("Invalid demo starting funds");
        var random = new SeededRandom(seed);
        var world = new WorldState
        {
            Characters = config.Read<List<CharacterState>>("characters/demo.json"),
            Organizations = [new("sect", "青雲宗")]
        };
        config.Initialize(world);
        foreach (var owner in new[] { "player", "npc", "sect" })
            world.Ledger.Add(new(world.NextSequence++, 0, "$initial:" + owner, "$genesis", owner,
                "spirit-stone", funds.MinimumStones + random.NextUInt32() % funds.StoneRange, "Genesis"));
        world.RandomState = random.State;
        WorldIntegrity.Validate(world);
        return world;
    }
}
