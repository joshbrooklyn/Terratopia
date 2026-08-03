using CombatEngine.DataClasses;
using CombatEngine.Engine;

namespace CombatEngine.Keywords;

public class GrowthKeyword : PowerKeyword
{
    public const string KeywordName = "Growth";

    public override string Name => KeywordName;

    public override void OnUsed(CombatEntity actor, bool actorIsAlly, string actionId, IKeywordUsageStore store) =>
        store.Increment(UsageKey(actor.EntityId, actionId));

    // "After each use, increase the power modifier by 10%" means the triggering use itself gets
    // no bonus - only uses before it do.
    public override double GetBonus(CombatEntity actor, CombatEntity target, bool actorIsAlly, string actionId, IKeywordUsageStore store)
    {
        int usesIncludingThisOne = store.GetCount(UsageKey(actor.EntityId, actionId)); // OnUsed already counted this use
        int priorUses = Math.Max(0, usesIncludingThisOne - 1);
        double bonus = 0.10 * priorUses;
        Logger.Debug($"[keyword] Growth: {actor.Name} priorUses={priorUses} -> bonus={bonus:F2}");
        return bonus;
    }

    private static string UsageKey(string actorId, string actionId) => $"growth:{actorId}:{actionId}";
}
