using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Cultivation.Domain;

namespace Cultivation.Simulation;

// File I/O belongs to world creation. Running worlds and save loading never consult these files.
public sealed class GameConfiguration
{
    private static readonly JsonSerializerOptions Options = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };
    public string DirectoryPath { get; }
    public GameConfiguration(string directoryPath) => DirectoryPath = Path.GetFullPath(directoryPath);
    public static GameConfiguration Find()
    {
        foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            for (var d = new DirectoryInfo(start); d is not null; d = d.Parent)
                if (File.Exists(Path.Combine(d.FullName, "AGENTS.md")) && Directory.Exists(Path.Combine(d.FullName, "config")))
                    return new(Path.Combine(d.FullName, "config"));
        return new(Path.Combine(AppContext.BaseDirectory, "config"));
    }
    public T Read<T>(string relativePath)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(Path.Combine(DirectoryPath, relativePath)), Options)
                ?? throw new InvalidDataException("設定不可為null");
        }
        catch (Exception e) when (e is IOException or JsonException or ArgumentException)
        { throw new InvalidDataException($"設定讀取失敗：{relativePath}：{e.Message}", e); }
    }
    public T Patch<T>(string path, T original)
    {
        var node = JsonSerializer.SerializeToNode(original, Options)!.AsObject();
        foreach (var pair in Read<JsonObject>(path)) node[pair.Key] = pair.Value?.DeepClone();
        try { return node.Deserialize<T>(Options) ?? throw new InvalidDataException($"無效設定：{path}"); }
        catch (JsonException e) { throw new InvalidDataException($"無效設定：{path}：{e.Message}", e); }
    }
    public void ApplyCharacters(WorldState w, string profile)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var patch in Read<List<JsonObject>>($"characters/{profile}.json"))
        {
            string id = patch["Id"]?.GetValue<string>() ?? throw new InvalidDataException($"角色缺少Id：{profile}");
            if (!ids.Add(id)) throw new InvalidDataException($"角色Id重複：{profile}:{id}");
            int index = w.Characters.FindIndex(c => c.Id == id);
            var merged = JsonSerializer.SerializeToNode(index < 0 ? new CharacterState(id, id) : w.Characters[index], Options)!.AsObject();
            foreach (var pair in patch) merged[pair.Key] = pair.Value?.DeepClone();
            CharacterState character;
            try { character = merged.Deserialize<CharacterState>(Options)!; }
            catch (JsonException e) { throw new InvalidDataException($"無效角色設定：{profile}:{id}", e); }
            if (index < 0) w.Characters.Add(character); else w.Characters[index] = character;
        }
    }
    public void AddStock(WorldState w, string profile)
    {
        var owners = w.Characters.Select(c => c.Id).Concat(w.Organizations.Select(o => o.Id)).ToHashSet(StringComparer.Ordinal);
        foreach (var s in Read<List<InitialStock>>($"economy/{profile}-stock.json"))
        {
            // Autonomous templates include all 50 NPCs; smaller worlds deliberately use the prefix.
            if (profile == "autonomous" && s.Owner.StartsWith("npc:", StringComparison.Ordinal) && !owners.Contains(s.Owner)) continue;
            if (!owners.Contains(s.Owner) || s.Quantity <= 0 || !w.Items.Any(i => i.Id == s.Item)) throw new InvalidDataException($"無效初始庫存：{profile}:{s.Owner}:{s.Item}");
            w.Ledger.Add(new(w.NextSequence++, 0, s.ActionId, "$genesis", s.Owner, s.Item, s.Quantity, "Genesis"));
        }
    }
    public void Initialize(WorldState w)
    {
        w.Rules = Read<EconomyRules>("economy/production.json");
        w.Items = Read<List<ItemDefinition>>("items/catalog.json");
        w.Currencies = Read<List<CurrencyDefinition>>("items/currencies.json");
        CurrencyIntegrity.Validate(w);
        w.Aspirations = Read<List<AspirationDefinition>>("characters/aspirations.json");
        if (w.Aspirations.Count != Enum.GetValues<Aspiration>().Length) throw new InvalidDataException("志向設定須列出所有已支援的志向");
        w.MapRules = Read<DemoWorldRules>("world/demo.json");
        w.MissionDefaults = Read<MissionTemplate>("organizations/representative-mission.json");
        w.Clock = new(0, w.MapRules.SecondsPerTick);
        w.EquipmentDurability=new(){Rules=Read<DurabilityRules>("items/durability.json")};
    }
    private sealed record InitialStock(string Owner, string Item, long Quantity, string ActionId);
}
