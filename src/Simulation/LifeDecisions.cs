using Cultivation.Domain;
using Cultivation.AI;

namespace Cultivation.Simulation;

internal static class LifeDecisions
{
    internal static IEnumerable<ScoredAction> Evaluate(WorldState w,CharacterState a,DecisionSource source)
    {
        if(w.LifeSystem is not {} life||a.LifeSkills is null)return [];
        var stock=w.Items.ToDictionary(i=>i.Id,i=>Inventory.Balance(w,a.Id,i.Id));
        var present=w.Characters.Where(c=>c.RegionId==a.RegionId&&c.DemoLocationId==a.DemoLocationId&&c.HexPosition==a.HexPosition).ToArray();
        return LifeUtility.Evaluate(new(a,w.Clock.Tick,source,stock,present,life.Recipes,life.Equipment.Where(g=>Inventory.Balance(w,a.Id,g.Id)==1).ToArray()));
    }
}
