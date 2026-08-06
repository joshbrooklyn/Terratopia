using CombatEngine.DataClasses;

namespace CombatEngine.Passives;

public abstract class Passive
{
    public abstract string Name { get; }

    // Returns true if death was prevented/reversed for this entity.
    public abstract bool TryPreventDeath(CombatEntity target);
}
