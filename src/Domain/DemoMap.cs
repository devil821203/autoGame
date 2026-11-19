namespace Cultivation.Domain;

// Demo-only geography; not a commitment to the final map system.
public static class DemoMap
{
    public static bool Contains(string id) => id is "town" or "sect" or "grove" or "dungeon" or "alchemy";
    public static long TravelTicks(string from, string to, DemoWorldRules? rules = null) => from == to ? 0 :
        from == "town" || to == "town" ? rules?.TownTravelTicks ?? 3 : rules?.OtherTravelTicks ?? 5;
}
