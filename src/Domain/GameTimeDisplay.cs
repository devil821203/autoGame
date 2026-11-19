namespace Cultivation.Domain;

public static class GameTimeDisplay
{
    public static string Duration(WorldState world, long steps) => world.Time?.ActionDriven?.MinutesPerActionPoint is { } minutes
        ? $"{checked(steps * minutes)}分鐘" : $"{steps}Tick";
}
