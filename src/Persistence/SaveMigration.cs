using System.Text.Json;
using System.Text.Json.Nodes;
using Cultivation.Domain;
using Cultivation.Simulation;

namespace Cultivation.Persistence;

internal static class SaveMigration
{
    internal static WorldState Read(string json)
    {
        try
        {
            var root = JsonNode.Parse(json) as JsonObject ?? throw new InvalidDataException("存檔根節點必須是物件。");
            if (root["SchemaVersion"] is not JsonValue versionNode || !versionNode.TryGetValue<int>(out int version))
                throw new InvalidDataException("存檔缺少有效的SchemaVersion。");
            if (version == 1)
            {
                // Retain fields present in later schema-1 saves; only fill absent legacy fields.
                if (!root.ContainsKey("DemoMapEnabled")) root["DemoMapEnabled"] = false;
                if (!root.ContainsKey("Notifications")) root["Notifications"] = new JsonArray();
                if (!root.ContainsKey("HerbRegeneration")) root["HerbRegeneration"] = null;
                if (root["Characters"] is JsonArray actors)
                    foreach (var actor in actors.OfType<JsonObject>())
                        if (!actor.ContainsKey("DemoLocationId")) actor["DemoLocationId"] = "town";
                root["SchemaVersion"] = WorldState.CurrentSchemaVersion;
            }
            else if (version == 2) root["SchemaVersion"] = WorldState.CurrentSchemaVersion;
            else if (version != WorldState.CurrentSchemaVersion)
                throw new InvalidDataException($"不支援存檔版本 {version}，目前支援1、2與{WorldState.CurrentSchemaVersion}。");
            bool RemovedCountry(JsonNode? kind) => kind is JsonValue value &&
                (value.TryGetValue<int>(out int number) && number == 0 || value.TryGetValue<string>(out string? name) && name == "State");
            if (root["Organizations"] is JsonArray organizations && organizations.OfType<JsonObject>().Any(o => RemovedCountry(o["Kind"])) ||
                root["HexMap"] is JsonObject map &&
                (map["Sites"] is JsonArray sites && sites.OfType<JsonObject>().Any(s => RemovedCountry(s["Kind"])) ||
                 map["Tiles"] is JsonArray tiles && tiles.OfType<JsonObject>().Any(t => t["StateId"] is not null)))
                throw new InvalidDataException("此存檔含已移除的國家組織、國家領地或治所，無法直接載入新版；請建立新情境。原存檔不會被修改。");
            // Display-only rename of the historical currency. Never rewrite resource IDs,
            // balances, prices, or load present-day configuration into an old world.
            if (root["Items"] is JsonArray items)
                foreach (var item in items.OfType<JsonObject>())
                {
                    if (item["Id"]?.GetValue<string>() == "spirit-stone" && item["Name"]?.GetValue<string>() == "靈石")
                        item["Name"] = "錢財";
                    if (item["Id"]?.GetValue<string>() == "beast-hide" && item["Name"]?.GetValue<string>() == "狼皮")
                        item["Name"] = "獸皮";
                }
            return WorldCodec.Deserialize(root.ToJsonString());
        }
        catch (JsonException error) { throw new InvalidDataException("存檔JSON損壞或欄位格式不符。", error); }
    }
}
