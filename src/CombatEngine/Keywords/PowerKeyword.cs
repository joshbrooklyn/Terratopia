using CombatEngine.DataClasses;

namespace CombatEngine.Keywords;

public abstract class PowerKeyword
{
    public abstract string Name { get; }

    // Called once per resolved command that carries this keyword, before any bonus is computed.
    public virtual void OnUsed(CombatEntity actor, bool actorIsAlly, string actionId, IKeywordUsageStore store) { }

    // Raw, uncapped bonus fraction for one (effect, target) pair. Caller applies the shared cap.
    public abstract double GetBonus(CombatEntity actor, CombatEntity target, bool actorIsAlly, string actionId, IKeywordUsageStore store);

    // The IKeywordUsageStore key this keyword's counter lives under, or null for the stateless
    // keywords that keep no counter. Lets KeywordResolver report a running use-count alongside
    // KeywordApplied without knowing each stacking keyword's own key scheme.
    public virtual string? UsageKey(CombatEntity actor, bool actorIsAlly, string actionId) => null;
}
