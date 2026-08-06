namespace CombatEngine.Passives;

public static class PassiveRegistry
{
    private static readonly Dictionary<string, Passive> _passives =
        new Passive[] { new LivingDeadPassive() }.ToDictionary(p => p.Name);

    // Yields, in order, the registered passives from passiveNames. Unrecognised names are
    // silently dropped, the same way PowerKeywordRegistry.Resolve treats unrecognised keywords.
    public static IEnumerable<Passive> Resolve(IEnumerable<string> passiveNames)
    {
        foreach (var name in passiveNames)
        {
            if (_passives.TryGetValue(name, out var passive))
                yield return passive;
        }
    }
}
