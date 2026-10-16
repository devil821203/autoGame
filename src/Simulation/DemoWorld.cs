using Cultivation.Domain;

namespace Cultivation.Simulation;

public static class DemoWorld
{
    public static WorldState Create(ulong seed = 12345)
    {
        var random = new SeededRandom(seed);
        var world = new WorldState
        {
            Characters = [new("player", "行者"), new("npc", "林青玄")],
            Organizations = [new("sect", "青雲宗")]
        };
        foreach (var owner in new[] { "player", "npc", "sect" })
            world.Ledger.Add(new(world.NextSequence++, 0, "$initial:" + owner, "$genesis", owner,
                "spirit-stone", 100 + random.NextUInt32() % 50, "Genesis"));
        world.RandomState = random.State;
        return world;
    }
}
