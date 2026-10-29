namespace Cultivation.Domain;

public static class CurrentAttributeCalculator
{
    public static long Value(CharacterState actor, MortalAttribute attribute)
    {
        var state = actor.MortalCultivation ?? throw new InvalidOperationException("Missing mortal attributes");
        long value = checked(state.BaseAttributes.GetValueOrDefault(attribute) + state.AttributeGrowth.GetValueOrDefault(attribute));
        foreach (var source in actor.CurrentAttributeModifiers.Values)
            value = checked(value + source.GetValueOrDefault(attribute));
        return Math.Max(0, value);
    }
}
