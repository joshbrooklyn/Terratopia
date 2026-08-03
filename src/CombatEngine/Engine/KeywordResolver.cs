using CombatEngine.DataClasses;
using CombatEngine.Keywords;

namespace CombatEngine.Engine;

// Everything keyword-related for a single encounter: the usage counters that stacking keywords
// read and write, the per-command "this action was used" notification, and the capped bonus
// summation. This type IS the IKeywordUsageStore passed to every keyword call - one instance per
// encounter, built by CombatEngineClass.InitCombat, so counts are scoped to a single combat and
// are not persisted across fights.
internal sealed class KeywordResolver : IKeywordUsageStore
{
    private readonly Dictionary<string, int> _usageCounts = new();

    int IKeywordUsageStore.Increment(string key) =>
        _usageCounts[key] = _usageCounts.GetValueOrDefault(key) + 1;

    int IKeywordUsageStore.GetCount(string key) =>
        _usageCounts.GetValueOrDefault(key);

    // Fires once per active keyword, per command - before any target is touched. This is the
    // hook stacking keywords (Growth, Teamwork) use to record that this action was used.
    internal void NotifyKeywordsUsed(List<PowerKeyword> activeKeywords, CombatEntity actor, bool actorIsAlly, string actionId)
    {
        foreach (var keyword in activeKeywords)
            keyword.OnUsed(actor, actorIsAlly, actionId, this);
    }

    // Each active keyword's bonus is capped independently against this action's own base power
    // factor, then the capped contributions are summed (not: sum first, then cap once) - see
    // docs/keywords.md "Capping rules". Also raises KeywordApplied for each nonzero contribution.
    internal double ApplyKeywordBonuses(List<PowerKeyword> activeKeywords, double basePowerFactor, CombatEntity actor, CombatEntity target, bool actorIsAlly, string actionId)
    {
        if (activeKeywords.Count == 0)
            return 0.0;

        // <= 0 when base PowerFactor is 0, which naturally yields no bonus ("no effect if the
        // base power modifier of the action is 0%").
        double cap = Math.Min(basePowerFactor * 2, basePowerFactor + 0.5);

        double totalBonus = 0.0;
        foreach (var keyword in activeKeywords)
        {
            double raw = keyword.GetBonus(actor, target, actorIsAlly, actionId, this);
            double applied = Math.Min(raw, cap);
            if (applied > 0)
                CombatEventBus.RaiseKeywordApplied(keyword.Name, actor.EntityId, actor.Name, target.EntityId, target.Name, applied);
            totalBonus += applied;
        }
        return totalBonus;
    }
}
