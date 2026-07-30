using CombatEngine.DataClasses;

namespace CombatEngine.Keywords;

public class TeamworkKeyword : PowerKeyword
{
    public const string KeywordName = "Teamwork";

    public override string Name => KeywordName;

    public override void OnUsed(CombatEntity actor, bool actorIsAlly, string actionId, IKeywordUsageStore store) =>
        store.Increment(UsageKey(actorIsAlly));

    public override double GetBonus(CombatEntity actor, CombatEntity target, bool actorIsAlly, string actionId, IKeywordUsageStore store) =>
        0.05 * store.GetCount(UsageKey(actorIsAlly));

    // Scoped to the whole side, not the individual actor - any teammate's Teamwork use
    // counts toward (and benefits from) the same counter.
    private static string UsageKey(bool actorIsAlly) => actorIsAlly ? "ally:Teamwork" : "enemy:Teamwork";
}
