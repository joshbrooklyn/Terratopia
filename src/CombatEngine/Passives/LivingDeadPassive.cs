using CombatEngine.DataClasses;
using CombatEngine.Engine;

namespace CombatEngine.Passives;

public class LivingDeadPassive : DeathPassive
{
    public const string PassiveName = "LivingDead";

    public override string Name => PassiveName;

    public override bool TryPreventDeath(CombatEntity target)
    {
        bool alreadyTriggered = target.ConsumedPassives.Contains(Name);
        if (alreadyTriggered)
        {
            Logger.Debug($"[passive] LivingDead: {target.Name} alreadyTriggered -> not prevented");
            return false;
        }
        target.ConsumedPassives.Add(Name);

        int oldHp = target.Hp;
        target.Hp = 1;
        Logger.Debug($"[passive] LivingDead: {target.Name} oldHp={oldHp} -> revived at hp=1");
        CombatEventBus.RaiseEntityRevived(target.EntityId, target.Name);
        CombatEventBus.RaiseEntityHpChanged(target.EntityId, target.Name, oldHp, target.Hp);
        return true;
    }
}
