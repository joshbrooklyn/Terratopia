using CombatEngine.DataClasses;
using CombatEngine.Engine;

namespace CombatEngine.Keywords;

public class TeamworkKeyword : PowerKeyword
{
    public const string KeywordName = "Teamwork";

    public override string Name => KeywordName;

    public override void OnUsed(CombatEntity actor, bool actorIsAlly, string actionId, IKeywordUsageStore store) =>
        store.Increment(UsageKey(actorIsAlly));

    public override double GetBonus(CombatEntity actor, CombatEntity target, bool actorIsAlly, string actionId, IKeywordUsageStore store)
    {
        int count = store.GetCount(UsageKey(actorIsAlly));
        double bonus = CombatBalance.Current.Keywords.TeamworkBonusPerUse * count;
        Logger.Debug($"[keyword] Teamwork: {actor.Name} side={(actorIsAlly ? "ally" : "enemy")} count={count} -> bonus={bonus:F2}");
        return bonus;
    }

    // Scoped to the whole side, not the individual actor - any teammate's Teamwork use
    // counts toward (and benefits from) the same counter.
    private static string UsageKey(bool actorIsAlly) => actorIsAlly ? "ally:Teamwork" : "enemy:Teamwork";
}
