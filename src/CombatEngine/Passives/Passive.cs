using CombatEngine.DataClasses;

namespace CombatEngine.Passives;

public abstract class Passive
{
    public abstract string Name { get; }

    // Returns (deathPrevented, reviveHp). When deathPrevented is true, the engine sets the
    // entity's Hp to reviveHp instead of letting it die.
    public virtual (bool, int) OnBeforeDeath(string EntityId, string EntityName)
    {
        return (false, 0); // false = death not prevented, 0 = no revive HP to apply
    }

    protected virtual int TotalApplications(CombatEntity entity)
    {
        return PassiveTracker.Get(Name, entity.EntityId).TotalApplications;
    }

    protected virtual int ApplicationsThisRound(CombatEntity entity)
    {
        return PassiveTracker.Get(Name, entity.EntityId).ApplicationsThisRound;
    }

    public virtual void RemoveFrom(string EntityId, string EntityName)
    {
        if (PassiveTracker.Remove(Name, EntityId))
            CombatEventBus.RaisePassiveRemoved(EntityId, EntityName, Name);
    }
}