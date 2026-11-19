using Cultivation.Domain;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Cultivation.Simulation;

public static class RegionalWorldScenario
{
    private static ulong Stream(ulong seed,string id)
    {
        ulong value=BinaryPrimitives.ReadUInt64LittleEndian(SHA256.HashData(Encoding.UTF8.GetBytes($"r6a:1:{seed}:{id}")));
        return value==0?1:value;
    }
    public static WorldState Create(string name="行者",ulong? seed=null,GameConfiguration? configuration=null)
    {
        name=name.Trim();if(name.Length is <1 or >24||name.Any(char.IsControl))throw new ArgumentException("角色名稱須為1–24字且無控制字元");
        var config=configuration??GameConfiguration.Find();var rules=config.Read<RegionalGenerationRules>("world/regions-demo.json");
        RegionalWorldIntegrity.ValidateRules(rules);
        var w=TestScenarioCatalog.Create("methods",name,config);var map=w.HexMap!;
        var t=new RegionalWorldState{Seed=seed??rules.Seed,Rules=rules,InitialResourceHerbs=Inventory.Balance(w,"grove","herb"),InitialMarketStock=w.Items.Select(i=>(i.Id,Amount:Inventory.Balance(w,"market",i.Id))).Where(p=>p.Amount>0).ToDictionary(p=>p.Id,p=>p.Amount)};
        var homeSites=map.Sites.Select(s=>rules.HomeSiteOverrides.TryGetValue(s.Id,out var coord)?s with {Anchor=coord}:s).ToList();
        var original=map.Tiles.ToArray();var features=original.Where(tile=>tile.Mountain||tile.Forest||tile.Terrain!=HexTerrain.Plains).ToArray();
        var homePlaces=original.Where(tile=>tile.PlaceId is not null).ToArray();
        map.Tiles=[];map.Sites=homeSites;map.Regional=t;
        for(int index=0;index<rules.Regions.Length;index++)
        {
            var label=rules.Regions[index];var origin=new HexCoord(index*rules.Width,0);var random=new SeededRandom(Stream(t.Seed,label.Id));
            var slots=(from r in Enumerable.Range(0,rules.Height) from q in Enumerable.Range(0,rules.Width) select new HexCoord(origin.Q+q,r)).ToList();
            for(int n=slots.Count-1;n>0;n--){int other=(int)(random.NextUInt32()%(uint)(n+1));(slots[n],slots[other])=(slots[other],slots[n]);}
            HexCoord town=index==0?homePlaces.Single(p=>p.PlaceId=="town").Coord:slots[0];
            HexCoord resource=index==0?homePlaces.Single(p=>p.PlaceId=="grove").Coord:slots[1];
            var region=new WorldRegion(label.Id,label.Name,origin,rules.Width,rules.Height,town,resource,index==0?"market":"region:"+label.Id+":market",index==0?"grove":"region:"+label.Id+":grove");t.Regions.Add(region);
            var protectedCoords=index==0?homePlaces.Select(p=>p.Coord).Concat(homeSites.Select(s=>s.Anchor)).Concat(map.Buildings.Select(b=>b.Coord)).ToHashSet():new HashSet<HexCoord>{town,resource};
            if(protectedCoords.Any(c=>c.Q<origin.Q||c.Q>=origin.Q+rules.Width||c.R<0||c.R>=rules.Height))throw new InvalidDataException("world/regions-demo.json: existing site does not fit region; provide HomeSiteOverrides");
            var terrainSlots=slots.Where(c=>!protectedCoords.Contains(c)).ToArray();
            if(terrainSlots.Length<features.Length)throw new InvalidDataException("world/regions-demo.json: insufficient terrain slots");
            var terrain=features.Select((f,i)=>(f,coord:terrainSlots[i])).ToDictionary(p=>p.coord,p=>p.f);
            foreach(var at in slots.OrderBy(c=>c.R).ThenBy(c=>c.Q))
            {
                terrain.TryGetValue(at,out var f);
                var home=index==0?original.FirstOrDefault(p=>p.Coord==at):null;
                string? place=index==0?home?.PlaceId:at==town?RegionGeometry.PlaceId(map,label.Id,"town"):at==resource?RegionGeometry.PlaceId(map,label.Id,"grove"):null;
                string? settlement=index==0?homeSites.FirstOrDefault(s=>s.Anchor==at&&s.Kind is SiteKind.Village or SiteKind.Town or SiteKind.City)?.Name:at==town?label.Name+"村":null;
                map.Tiles.Add(new(at,f?.Terrain??HexTerrain.Plains,f?.Mountain??false,at==resource||f?.Forest==true,settlement,place,home?.LocalOrganizationId));
            }
            if(index>0)
            {
                var template=homeSites.First(s=>s.Kind==SiteKind.Village);
                map.Sites.Add(template with {Id="region:"+label.Id+":settlement",Name=label.Name+"村",Anchor=town,Slots=template.Slots.ToList()});
                w.Organizations.Add(new(region.MarketOwner,label.Name+"市場帳戶",RegionId:label.Id));w.Organizations.Add(new(region.ResourceOwner,label.Name+"資源帳戶",RegionId:label.Id));
                foreach(var (item,amount) in t.InitialMarketStock)w.Ledger.Add(new(w.NextSequence++,0,"region-genesis:"+region.MarketOwner+":"+item,"$genesis",region.MarketOwner,item,amount,"Genesis"));
                w.Ledger.Add(new(w.NextSequence++,0,"region-genesis:"+region.ResourceOwner,"$genesis",region.ResourceOwner,"herb",t.InitialResourceHerbs,"Genesis"));
            }
            foreach(var at in protectedCoords)Carve(map,town,at);
        }
        for(int i=1;i<t.Regions.Count;i++)
        {
            var from=t.Regions[i-1];var to=t.Regions[i];var rng=new SeededRandom(Stream(t.Seed,"route:"+from.Id+":"+to.Id));int row=(int)(rng.NextUInt32()%(uint)rules.Height);
            var a=new HexCoord(from.Origin.Q+rules.Width-1,row);var b=new HexCoord(to.Origin.Q,row);
            t.Routes.Add(new("route:"+from.Id+":"+to.Id,from.Id,to.Id,a,b));Carve(map,from.Town,a);Carve(map,to.Town,b);
        }
        w.Characters=w.Characters.Select(a=>a with {RegionId=t.Regions[0].Id,HexPosition=HexNavigation.Resolve(map,a.DemoLocationId)}).ToList();
        w.RandomState=Stream(t.Seed,"simulation");
        DiscreteTime.EnableFresh(w,config);ActionLifecycle.EnableFresh(w);EventDecisions.EnableFresh(w);ContentProfile.EnableFresh(w,config);MarketOrders.EnableFresh(w,config);
        w.Time!.MarketOrders!.Settings=w.Time.MarketOrders.Settings with {Rules=w.Time.MarketOrders.Settings.Rules with {Venues=t.Regions.Select(r=>new MarketVenue(r.MarketOwner,r.Id,"town")).ToArray()}};
        WorldIntegrity.Validate(w);return w;
    }
    private static void Carve(HexMapState map,HexCoord from,HexCoord to)
    {
        void Clear(HexCoord at){int index=map.Tiles.FindIndex(p=>p.Coord==at);var tile=map.Tiles[index];map.Tiles[index]=tile with {Mountain=false,Terrain=HexTerrain.Plains,Forest=map.Regional!.Regions.Any(r=>r.Resource==at)};}
        var current=from;Clear(current);
        while(current.Q!=to.Q){current=current with {Q=current.Q+Math.Sign(to.Q-current.Q)};Clear(current);}
        while(current.R!=to.R){current=current with {R=current.R+Math.Sign(to.R-current.R)};Clear(current);}
    }
}
