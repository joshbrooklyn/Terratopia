using CombatEngine.DataClasses;
using CombatEngine.Engine;

namespace CombatEngine.Passives;

public class LivingDeadPassive : Passive
{
    public const string PassiveName = "LivingDead";

    public override string Name => PassiveName;

    public override bool TryPreventDeath(CombatEntity target)
    {
        bool alreadyTriggered = target.HasConsumedPassive(Name);
        if (alreadyTriggered)
        {
            Logger.Debug($"[passive] LivingDead: {target.Name} alreadyTriggered -> not prevented");
            return false;
        }
        target.ConsumePassive(Name);
        target.Revive(CombatBalance.Current.LivingDeadReviveHp, PassiveName, PassiveName);
        return true;
    }
}
