namespace Cultivation.Persistence;

public static class WorldStores
{
    public static IWorldStore ForPath(string path)=>Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".json"=>new JsonWorldStore(),
        ".sqlite" or ".db"=>new SqliteWorldStore(),
        _=>throw new InvalidDataException("支援.json／.sqlite／.db世界存檔")
    };
}
