using System.Text.Json;
using System.Text.Json.Nodes;
using Cultivation.Domain;
using Cultivation.Simulation;

namespace Cultivation.Persistence;

internal static class SaveMigration
{
    internal const int DiscreteSaveVersion = 4;
    internal const int LifecycleSaveVersion = 5;
    internal const int DecisionSaveVersion = 6;
    internal const int ContentSaveVersion = 7;
    internal const int OrderSaveVersion = 8;
    internal const int RegionSaveVersion = 9;
    internal const int ControlSaveVersion = 10;
    internal const int RepairSaveVersion = 11;
    internal const int LifeEventSaveVersion = 18;
    internal static string Write(WorldState world)
    {
        if (world.Time?.DiscreteEvents is null) return WorldCodec.Serialize(world);
        var root = JsonSerializer.SerializeToNode(world)!.AsObject();
        // Wire schema 4 prevents an old schema-3 engine from silently dropping the seconds queue.
        // The detached in-memory WorldState schema remains unchanged for legacy systems.
        root["SchemaVersion"] = world.Time.IndependentLifeEvents?LifeEventSaveVersion:world.Time.EquipmentRepair is not null?RepairSaveVersion:world.Time.PlayerControl is not null ? ControlSaveVersion : world.HexMap?.Regional is not null ? RegionSaveVersion : world.Time.MarketOrders is not null ? OrderSaveVersion : world.Time.Content is not null ? ContentSaveVersion : world.Time.EventDecisions is not null ? DecisionSaveVersion : world.Time.ActionRuntime is null ? DiscreteSaveVersion : LifecycleSaveVersion;
        return root.ToJsonString();
    }
    internal static WorldState Read(string json)
    {
        try
        {
            var root = JsonNode.Parse(json) as JsonObject ?? throw new InvalidDataException("存檔根節點必須是物件。");
            if (root["SchemaVersion"] is not JsonValue versionNode || !versionNode.TryGetValue<int>(out int version))
                throw new InvalidDataException("存檔缺少有效的SchemaVersion。");
            bool discrete = root["Time"] is JsonObject time && time["DiscreteEvents"] is JsonObject;
            bool lifecycle = root["Time"] is JsonObject lifeTime && lifeTime["ActionRuntime"] is JsonObject;
            bool decisions = root["Time"] is JsonObject decisionTime && decisionTime["EventDecisions"] is JsonObject;
            bool content=root["Time"] is JsonObject contentTime && contentTime["Content"] is JsonObject;
            bool orders=root["Time"] is JsonObject orderTime && orderTime["MarketOrders"] is JsonObject;
            bool regional=root["HexMap"] is JsonObject regionMap&&regionMap["Regional"] is JsonObject;
            bool control=root["Time"] is JsonObject controlTime&&controlTime["PlayerControl"] is JsonObject;
            bool repair=root["Time"] is JsonObject repairTime&&repairTime["EquipmentRepair"] is JsonObject;
            bool lifeEvents=root["Time"] is JsonObject preciseLife&&preciseLife["IndependentLifeEvents"]?.GetValue<bool>()==true;
            if(version==LifeEventSaveVersion){
                if(!lifeEvents||!discrete||!lifecycle||!control||root["Time"]!["IndependentScheduledEvents"]?.GetValue<bool>()!=true||root["Time"]!["IndependentRecoveryEvents"]?.GetValue<bool>()!=true||root["Time"]!["IndependentResourceEvents"]?.GetValue<bool>()!=true||root["Time"]!["IndependentPayrollEvents"]?.GetValue<bool>()!=true||root["Time"]!["IndependentMissionExpiryEvents"]?.GetValue<bool>()!=true||root["Time"]!["IndependentSocialExpiryEvents"]?.GetValue<bool>()!=true)throw new InvalidDataException($"格式{LifeEventSaveVersion}需要最新工作／恢復／資源／發薪秒事件模式及共用核心快照");
                root["SchemaVersion"]=WorldState.CurrentSchemaVersion;
            }
            else if(lifeEvents)throw new InvalidDataException($"工作秒事件僅使用最新格式{LifeEventSaveVersion}；不轉換舊檔");
            else if(version==RepairSaveVersion){
                if(!repair||!control||!content||!decisions||!lifecycle||!discrete)throw new InvalidDataException("格式11需要R8修理及R1～R6b快照");
                root["SchemaVersion"]=WorldState.CurrentSchemaVersion;
            }
            else if(repair)throw new InvalidDataException("R8修理必須使用格式11，不遷移舊檔");
            else if(version==ControlSaveVersion)
            {
                if(!control||!content||!decisions||!lifecycle||!discrete)throw new InvalidDataException("格式10需要R6b控制資格及R1～R4快照");
                root["SchemaVersion"]=WorldState.CurrentSchemaVersion;
            }
            else if(control)throw new InvalidDataException("R6b控制資格必須使用格式10，不自動轉換舊檔");
            else if(version==RegionSaveVersion)
            {
                if(!regional||!content||!decisions||!lifecycle||!discrete)throw new InvalidDataException("格式9需要R6a及R1～R4快照");
                root["SchemaVersion"]=WorldState.CurrentSchemaVersion;
            }
            else if(regional)throw new InvalidDataException("R6a區域資料必須使用格式9，不轉換舊檔");
            else if(version==OrderSaveVersion)
            {
                if(!orders||!content||!decisions||!lifecycle||!discrete)throw new InvalidDataException("格式8需要R5及R1～R4快照");
                root["SchemaVersion"]=WorldState.CurrentSchemaVersion;
            }
            else if(orders)throw new InvalidDataException("R5訂單必須使用格式8，不自動轉換舊檔");
            else if(version==ContentSaveVersion)
            {
                if(!discrete||!lifecycle||!decisions||!content)throw new InvalidDataException("格式7需要R1／R2／R3及內容版本快照");
                root["SchemaVersion"]=WorldState.CurrentSchemaVersion;
            }
            else if(content)throw new InvalidDataException("R4內容快照必須使用存檔格式7；不轉換舊檔");
            else if(version==DecisionSaveVersion)
            {
                if(!discrete||!lifecycle||!decisions)throw new InvalidDataException("格式6需要R1／R2／R3快照");
                root["SchemaVersion"]=WorldState.CurrentSchemaVersion;
            }
            else if(decisions)throw new InvalidDataException("R3事件決策必須使用存檔格式6；不轉換舊檔");
            else if (version == LifecycleSaveVersion)
            {
                if(!discrete||!lifecycle)throw new InvalidDataException("格式5需要秒制及行動生命週期快照");
                root["SchemaVersion"]=WorldState.CurrentSchemaVersion;
            }
            else if(lifecycle)throw new InvalidDataException("R2行動生命週期必須使用存檔格式5；不轉換舊檔");
            else if (version == DiscreteSaveVersion)
            {
                if (!discrete) throw new InvalidDataException("秒制存檔缺少DiscreteEvents，不可當作舊模式載入。");
                root["SchemaVersion"] = WorldState.CurrentSchemaVersion;
            }
            else if (discrete) throw new InvalidDataException("秒制事件模式必須使用存檔格式4；不自動轉換舊檔。");
            else if (version == 1)
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
                throw new InvalidDataException($"不支援存檔版本 {version}，目前支援1、2、{WorldState.CurrentSchemaVersion}與格式{DiscreteSaveVersion}／{LifecycleSaveVersion}／{DecisionSaveVersion}／{ContentSaveVersion}。");
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
