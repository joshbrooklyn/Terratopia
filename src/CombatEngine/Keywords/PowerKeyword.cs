using CombatEngine.DataClasses;

namespace CombatEngine.Keywords;

public abstract class PowerKeyword
{
    public abstract string Name { get; }

    // Called once per resolved command that carries this keyword, before any bonus is computed.
    public virtual void OnUsed(CombatEntity actor, bool actorIsAlly, string actionId, IKeywordUsageStore store) { }

    // Raw, uncapped bonus fraction for one (effect, target) pair. Caller applies the shared cap.
    public abstract double GetBonus(CombatEntity actor, CombatEntity target, bool actorIsAlly, string actionId, IKeywordUsageStore store);
}
