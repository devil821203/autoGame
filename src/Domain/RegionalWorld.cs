namespace Cultivation.Domain;

public sealed record RegionName(string Id,string Name);
public sealed record RegionalGenerationRules(ulong Seed,int Width,int Height,RegionName[] Regions,Dictionary<string,HexCoord> HomeSiteOverrides);
public sealed record WorldRegion(string Id,string Name,HexCoord Origin,int Width,int Height,HexCoord Town,HexCoord Resource,string MarketOwner,string ResourceOwner);
public sealed record RegionRoute(string Id,string FromRegion,string ToRegion,HexCoord From,HexCoord To,bool Open=true);
public sealed class RegionalWorldState
{
    public int GeneratorVersion { get; set; }=1;
    public ulong Seed { get; set; }
    public RegionalGenerationRules Rules { get; set; }=null!;
    public List<WorldRegion> Regions { get; set; }=[];
    public List<RegionRoute> Routes { get; set; }=[];
    public long InitialResourceHerbs { get; set; }
    public Dictionary<string,long> InitialMarketStock { get; set; }=[];
}
public static class RegionGeometry
{
    public static WorldRegion? At(HexMapState map,HexCoord coord)=>map.Regional?.Regions.SingleOrDefault(r=>coord.Q>=r.Origin.Q&&coord.Q<r.Origin.Q+r.Width&&coord.R>=r.Origin.R&&coord.R<r.Origin.R+r.Height);
    public static bool Allows(HexMapState map,HexCoord from,HexCoord to)
    {
        if(map.Regional is not {} topology)return true;
        var a=At(map,from);var b=At(map,to);
        return a is not null&&b is not null&&(a.Id==b.Id||topology.Routes.Any(r=>r.Open&&(r.From==from&&r.To==to||r.To==from&&r.From==to)));
    }
    public static string PlaceId(HexMapState map,string region,string local)=>map.Regional is null?local:"region:"+region+":"+local;
    public static HexCoord? Resolve(HexMapState map,CharacterState actor,string target)
    {
        if(map.Regional is {} topology)
        {
            if(target is "town" or "grove" or "sect" or "alchemy" or "dungeon")
                target=actor.RegionId==topology.Regions[0].Id?target:PlaceId(map,actor.RegionId,target);
            var parts=target.Split(':');
            if(parts.Length==3&&parts[0]=="region"&&topology.Regions.SingleOrDefault(r=>r.Id==parts[1]) is {} region)
            {
                if(parts[2]=="town")return region.Town;
                if(parts[2]=="grove")return region.Resource;
                if(region.Id==topology.Regions[0].Id)return HexNavigation.Resolve(map,parts[2]);
            }
        }
        return HexNavigation.Resolve(map,target);
    }
    public static string LocalPlace(HexMapState map,HexCoord at)
    {
        var tile=map.Tiles.Single(t=>t.Coord==at);var region=At(map,at);
        if(region is not null){if(at==region.Town)return "town";if(at==region.Resource)return "grove";}
        return tile.PlaceId??at.ToString();
    }
}
