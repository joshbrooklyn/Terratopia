namespace CombatEngine.CombatFunctions;

public static class CombatFunctionRegistry
{
    private static readonly Dictionary<string, CombatFunction> _functions =
        new CombatFunction[]
        {
            new BasicDamageFunction(),
            new BasicHealFunction(),
            new NoOpFunction(),
        }.ToDictionary(f => f.Name);

    // Unlike PowerKeywordRegistry, an unrecognised name is fatal: the CombatFunction IS the
    // action, so silently dropping it would turn a Tech into a no-op instead of a loud failure.
    public static CombatFunction Resolve(string name) =>
        _functions.TryGetValue(name, out var function)
            ? function
            : throw new InvalidOperationException(
                $"Unknown CombatFunction '{name}'. Registered: {string.Join(", ", _functions.Keys.Order())}.");

    // Exposed so a test can assert the hand-synced schema enum hasn't drifted from the registry.
    public static IReadOnlyCollection<string> RegisteredNames => _functions.Keys;
}
