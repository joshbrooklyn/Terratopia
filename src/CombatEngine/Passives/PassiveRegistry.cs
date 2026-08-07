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
}
