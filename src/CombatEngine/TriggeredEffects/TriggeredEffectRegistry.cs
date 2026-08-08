namespace CombatEngine.TriggeredEffects;

public static class TriggeredEffectRegistry
{
    private static readonly Dictionary<string, TriggeredEffect> _triggeredEffects =
        new TriggeredEffect[] { new LivingDeadTriggeredEffect() }.ToDictionary(p => p.Name);

    // Returns null for an unrecognised name, the same way PowerKeywordRegistry ignores
    // unrecognised keywords. Called once per grant, by TriggeredEffectTracker.Add - never on the
    // dispatch path, since TriggeredEffectTracker caches the resolved instance.
    public static TriggeredEffect? Resolve(string triggeredEffectName) =>
        _triggeredEffects.GetValueOrDefault(triggeredEffectName);

    // Every registered triggered effect name, e.g. for schema-drift tests to check the
    // "triggeredEffect" enum in the action schemas against - the same role
    // CombatFunctionRegistry.RegisteredNames plays for combatFunction.
    public static IEnumerable<string> RegisteredNames => _triggeredEffects.Keys;
}
