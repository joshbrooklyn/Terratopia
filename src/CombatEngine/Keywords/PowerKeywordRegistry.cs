namespace CombatEngine.Keywords;

public static class PowerKeywordRegistry
{
    private static readonly Dictionary<string, PowerKeyword> _keywords =
        new PowerKeyword[]
        {
            new TeamworkKeyword(),
            new EngageKeyword(),
            new CruelKeyword(),
            new EmpoweredKeyword(),
            new StoicKeyword(),
            new GrowthKeyword(),
        }.ToDictionary(k => k.Name);

    // Duplicate names collapse (Distinct); names with no registered keyword are silently dropped
    // (OfType filters the nulls from GetValueOrDefault) rather than erroring.
    public static IEnumerable<PowerKeyword> Resolve(IEnumerable<string> names) =>
        names.Distinct()
             .Select(n => _keywords.GetValueOrDefault(n))
             .OfType<PowerKeyword>();
}
