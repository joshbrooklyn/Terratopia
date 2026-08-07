using CombatEngine.DataClasses;
using CombatEngine.Engine;

namespace CombatEngine.Passives;

public class LivingDeadPassive : Passive
{
    public const string PassiveName = "LivingDead";

    public override string Name => PassiveName;

    public override (bool, int) OnBeforeDeath(CombatEntity target)
    {
        // One-shot: drop ownership now, so HandleDefeat's dispatch loop won't find this passive
        // on the entity again for any later lethal hit.
        RemoveFrom(target);
        return (true, CombatBalance.Current.LivingDeadReviveHp); // true = death prevented, revive will be applied by the engine after this returns
    }
}
