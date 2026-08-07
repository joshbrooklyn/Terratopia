namespace CombatEngine.Passives;

public static class PassiveRegistry
{
    private static readonly Dictionary<string, Passive> _passives =
        new Passive[] { new LivingDeadPassive() }.ToDictionary(p => p.Name);

    // Returns null for an unrecognised name, the same way PowerKeywordRegistry ignores
    // unrecognised keywords. Called once per grant, by PassiveTracker.Add - never on the
    // dispatch path, since PassiveTracker caches the resolved instance.
    public static Passive? Resolve(string passiveName) =>
        _passives.GetValueOrDefault(passiveName);

    // Every registered passive name, e.g. for schema-drift tests to check the "passive" enum in
    // the action schemas against - the same role CombatFunctionRegistry.RegisteredNames plays for
    // combatFunction.
    public static IEnumerable<string> RegisteredNames => _passives.Keys;
}
