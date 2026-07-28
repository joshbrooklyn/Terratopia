using CombatEngine.DataClasses;
using CombatEngine.Enums;

namespace CombatEngine.Passives;

public abstract class DeathPassive : Passive
{
    public override PassiveTrigger Trigger => PassiveTrigger.OnDeath;

    // Returns true if death was prevented/reversed for this entity.
    public virtual bool TryPreventDeath(CombatEntity target)
    {
        return false;    
    }
}
