using System.Text.Json.Nodes;
using Cultivation.Domain;
using Cultivation.Persistence;
using Cultivation.Simulation;

internal static class CountryRemovalTests
{
    private static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    public static IEnumerable<(string Name,Action Body)> Cases=>new (string,Action)[]{
        ("Fresh worlds contain only three organization kinds and six scene kinds without country territory",()=>{
            var w=SettlementScenario.Create();
            Check(Enum.GetValues<OrganizationKind>().Length==3&&Enum.GetValues<SiteKind>().Length==6,"Country enum remains");
            Check(w.Organizations.All(o=>(int)o.Kind!=0)&&w.HexMap!.Sites.All(s=>(int)s.Kind!=0),"Country instance remains");
            Check(w.HexMap!.Tiles.Any(t=>t.LocalOrganizationId=="sect")&&w.Settlements.Count==1,"Local organization or settlement lost");
            var json=JsonNode.Parse(WorldCodec.Serialize(w))!;
            Check(json["HexMap"]!["Tiles"]!.AsArray().All(t=>!t!.AsObject().ContainsKey("StateId")),"Country territory field remains");
            Check((int)OrganizationKind.Sect==1&&(int)SiteKind.Village==4,"Existing non-country save IDs shifted");
        }),
        ("Old country saves are rejected without modifying their source while country-free saves still load",()=>{
            string directory=Path.Combine(Path.GetTempPath(),"country-removal-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
            try{
                var w=HexWorldScenario.Create();var json=JsonNode.Parse(WorldCodec.Serialize(w))!;
                foreach(var tile in json["HexMap"]!["Tiles"]!.AsArray())tile!["StateId"]=null;
                string path=Path.Combine(directory,"old.json");File.WriteAllText(path,json.ToJsonString());var store=new JsonWorldStore();
                Check(WorldCodec.Digest(store.Load(path))==WorldCodec.Digest(w),"Country-free old metadata changed assets");
                json["Organizations"]!.AsArray().Add(new JsonObject{["Id"]="legacy-country",["Name"]="歷史國家",["Kind"]=0});
                string original=json.ToJsonString();File.WriteAllText(path,original);
                try{_ = store.Load(path);}catch(InvalidDataException ex){Check(ex.Message.Contains("國家")&&File.ReadAllText(path)==original,"Country save rejection changed source or lacks explanation");return;}
                throw new Exception("Removed country world still loads");
            }finally{Directory.Delete(directory,true);}
        }),
        ("Removed country types cannot be reintroduced through JSON configuration",()=>{
            string directory=Path.Combine(Path.GetTempPath(),"country-config-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
            try{
                File.WriteAllText(Path.Combine(directory,"organizations.json"),"[{\"Id\":\"country\",\"Name\":\"Removed\",\"Kind\":\"State\"}]");
                try{_ = new GameConfiguration(directory).Read<List<OrganizationState>>("organizations.json");}
                catch(InvalidDataException){return;}
                throw new Exception("Removed country configuration accepted");
            }finally{Directory.Delete(directory,true);}
        })
    };
}
