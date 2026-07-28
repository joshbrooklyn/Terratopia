using CombatEngine.DataClasses;

namespace CombatEngine.Passives;

public class LivingDeadPassive : DeathPassive
{
    public const string PassiveName = "LivingDead";

    public override string Name => PassiveName;

    public override bool TryPreventDeath(CombatEntity target)
    {
        if (!target.ConsumedPassives.Add(Name))
            return false;

        int oldHp = target.Hp;
        target.Hp = 1;
        CombatEventBus.RaiseEntityRevived(target.EntityId, target.Name);
        CombatEventBus.RaiseEntityHpChanged(target.EntityId, target.Name, oldHp, target.Hp);
        return true;
    }
}
