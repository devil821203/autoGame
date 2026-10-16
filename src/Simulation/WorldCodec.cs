using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Cultivation.Domain;

namespace Cultivation.Simulation;

public static class WorldCodec
{
    public static string Serialize(WorldState world) => JsonSerializer.Serialize(world);
    public static WorldState Deserialize(string json) =>
        JsonSerializer.Deserialize<WorldState>(json) ?? throw new InvalidDataException("Empty world.");
    public static WorldState Clone(WorldState world) => Deserialize(Serialize(world));
    public static string Digest(WorldState world) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Serialize(world))));
}

public interface IRandomSource
{
    ulong State { get; }
    uint NextUInt32();
}

// Explicit state and algorithm; independent of System.Random/runtime implementation.
public sealed class SeededRandom(ulong seed) : IRandomSource
{
    public ulong State { get; private set; } = seed == 0 ? 1 : seed;
    public uint NextUInt32()
    {
        var value = State;
        value ^= value << 13;
        value ^= value >> 7;
        value ^= value << 17;
        State = value;
        return (uint)(value >> 32);
    }
}
