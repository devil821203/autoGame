using Cultivation.Domain;
using Cultivation.Simulation;

namespace Cultivation.Persistence;

public interface IWorldStore
{
    void Save(string path, WorldState snapshot);
    WorldState Load(string path);
}

public sealed class JsonWorldStore : IWorldStore
{
    // Create-only: never silently replace a user's existing save.
    public void Save(string path, WorldState snapshot)
    {
        WorldIntegrity.Validate(snapshot);
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temporary = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(WorldCodec.Serialize(snapshot));
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, fullPath, overwrite: false);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public WorldState Load(string path)
    {
        var world = WorldCodec.Deserialize(File.ReadAllText(path));
        WorldIntegrity.Validate(world);
        return world;
    }
}
