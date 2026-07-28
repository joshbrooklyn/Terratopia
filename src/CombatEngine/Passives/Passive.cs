using CombatEngine.Enums;

namespace CombatEngine.Passives;

public abstract class Passive
{
    public abstract string Name { get; }
    public abstract PassiveTrigger Trigger { get; }
}
