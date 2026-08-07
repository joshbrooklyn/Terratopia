using CombatEngine.DataClasses;
using CombatEngine.Engine;

namespace CombatEngine.Passives;

public class LivingDeadPassive : Passive
{
    public const string PassiveName = "LivingDead";

    public override string Name => PassiveName;

    public override (bool, int) OnBeforeDeath(string EntityId, string EntityName)
    {
        // One-shot: drop ownership now, so HandleDefeat's dispatch loop won't find this passive
        // on the entity again for any later lethal hit.
        RemoveFrom(EntityId, EntityName);
        return (true, 1); // true = death prevented, revive will be applied by the engine after this returns
    }
}
