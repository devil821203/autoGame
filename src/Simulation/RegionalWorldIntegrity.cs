using Cultivation.Domain;

namespace Cultivation.Simulation;

public static class RegionalWorldIntegrity
{
    public static void ValidateRules(RegionalGenerationRules r)
    {
        if(r is null||r.Width<2||r.Height<2||r.Height>128||r.Regions is not {Length:>0}||(long)r.Regions.Length*r.Width>128||(long)r.Regions.Length*r.Width*r.Height>16384||
            r.Regions.Any(n=>n is null||string.IsNullOrWhiteSpace(n.Id)||n.Id.Contains(':')||string.IsNullOrWhiteSpace(n.Name))||r.Regions[0].Id!="valley"||r.Regions.Select(n=>n.Id).Distinct().Count()!=r.Regions.Length||r.HomeSiteOverrides is null||
            r.HomeSiteOverrides.Any(p=>p.Value.Q<0||p.Value.Q>=r.Width||p.Value.R<0||p.Value.R>=r.Height))throw new InvalidDataException("world/regions-demo.json: invalid dimensions/regions/site overrides");
    }
    public static void Validate(WorldState w)
    {
        if(w.HexMap?.Regional is not {} t)return;
        ValidateRules(t.Rules);var map=w.HexMap;
        if(w.Time?.Content is null||t.GeneratorVersion!=1||t.Regions is null||t.Routes is null||t.Regions.Count!=t.Rules.Regions.Length||t.InitialResourceHerbs<=0||t.InitialMarketStock is null||t.InitialMarketStock.Any(p=>p.Value<=0||!w.Items.Any(i=>i.Id==p.Key))||
            t.Regions.Any(r=>r is null)||t.Routes.Any(r=>r is null)||map.Tiles.Count!=t.Rules.Width*t.Rules.Height*t.Regions.Count||map.Tiles.Any(tile=>RegionGeometry.At(map,tile.Coord) is null)||t.Routes.Count!=t.Regions.Count-1||t.Routes.Select(r=>r.Id).Distinct().Count()!=t.Routes.Count)
            throw new InvalidDataException("Invalid generated region snapshot");
        for(int index=0;index<t.Regions.Count;index++)
        {
            var r=t.Regions[index];var configured=t.Rules.Regions[index];
            if(r.Id!=configured.Id||r.Name!=configured.Name||r.Origin!=new HexCoord(index*t.Rules.Width,0)||r.Width!=t.Rules.Width||r.Height!=t.Rules.Height||RegionGeometry.At(map,r.Town)?.Id!=r.Id||RegionGeometry.At(map,r.Resource)?.Id!=r.Id||r.Town==r.Resource||
                !map.Tiles.Any(tile=>tile.Coord==r.Town&&tile.Settlement is not null&&HexNavigation.Passable(tile))||!map.Tiles.Any(tile=>tile.Coord==r.Resource&&tile.Forest&&HexNavigation.Passable(tile))||
                !w.Organizations.Any(o=>o.Id==r.MarketOwner&&o.RegionId==r.Id)||!w.Organizations.Any(o=>o.Id==r.ResourceOwner&&o.RegionId==r.Id)||
                !map.Sites.Any(s=>s.Anchor==r.Town&&s.Kind is SiteKind.Village or SiteKind.Town or SiteKind.City)||HexNavigation.Path(map,r.Town,r.Resource) is null)
                throw new InvalidDataException("Region lacks valid identity, settlement, resource or local access");
            if(index>0)
            {
                if(w.Ledger.Where(e=>e.To==r.ResourceOwner&&e.From=="$genesis"&&e.ResourceId=="herb").Sum(e=>e.Quantity)!=t.InitialResourceHerbs||
                    t.InitialMarketStock.Any(p=>w.Ledger.Where(e=>e.To==r.MarketOwner&&e.From=="$genesis"&&e.ResourceId==p.Key).Sum(e=>e.Quantity)!=p.Value))throw new InvalidDataException("Regional initial assets lack config-template provenance");
            }
        }
        foreach(var route in t.Routes)
            if(route is null||!route.From.Neighbors().Contains(route.To)||RegionGeometry.At(map,route.From)?.Id!=route.FromRegion||RegionGeometry.At(map,route.To)?.Id!=route.ToRegion||
                !map.Tiles.Any(tile=>tile.Coord==route.From&&HexNavigation.Passable(tile))||!map.Tiles.Any(tile=>tile.Coord==route.To&&HexNavigation.Passable(tile)))throw new InvalidDataException("Invalid regional route");
        foreach(var actor in w.Characters)
            if(RegionGeometry.At(map,HexNavigation.Position(map,actor))?.Id!=actor.RegionId)throw new InvalidDataException("Character region/hex disagree");
        if(w.Clock.Tick==0&&t.Routes.All(r=>r.Open)&&t.Regions.Any(r=>HexNavigation.Path(map,t.Regions[0].Town,r.Town) is null))throw new InvalidDataException("Initial regions not connected");
    }
}
