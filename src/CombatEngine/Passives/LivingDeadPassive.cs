using CombatEngine.DataClasses;
using CombatEngine.Engine;

namespace CombatEngine.Passives;

public class LivingDeadPassive : Passive
{
    public const string PassiveName = "LivingDead";

    public override string Name => PassiveName;

    public override (bool, int) OnBeforeDeath(CombatEntity target)
    {
        bool alreadyTriggered = TotalApplications(target) > 0;
        if (alreadyTriggered)
        {
            Logger.Debug($"[passive] LivingDead: {target.Name} alreadyTriggered -> not prevented");
            return (false, 0);
        }
        PassiveTracker.RecordActivation(this, target.EntityId);
        return (true, CombatBalance.Current.LivingDeadReviveHp); // true = death prevented, revive will be applied by the engine after this returns
    }
}
