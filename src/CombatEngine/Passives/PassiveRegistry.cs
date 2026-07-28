using CombatEngine.Enums;

namespace CombatEngine.Passives;

public static class PassiveRegistry
{
    private static readonly Dictionary<string, Passive> _passives =
        new Passive[] { new LivingDeadPassive() }.ToDictionary(p => p.Name);

    // Yields, in order, the registered passives from passiveNames that fire on `trigger`
    // and are of type T.
    public static IEnumerable<T> GetForTrigger<T>(IEnumerable<string> passiveNames, PassiveTrigger trigger)
        where T : Passive
    {
        foreach (var name in passiveNames)
        {
            if (_passives.TryGetValue(name, out var passive)
                && passive.Trigger == trigger
                && passive is T typed)
            {
                yield return typed;
            }
        }
    }
}
