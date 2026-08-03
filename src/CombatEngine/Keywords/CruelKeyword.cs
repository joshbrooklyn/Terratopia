using CombatEngine.DataClasses;
using CombatEngine.Engine;

namespace CombatEngine.Keywords;

public class CruelKeyword : PowerKeyword
{
    public const string KeywordName = "Cruel";
    private const double Threshold = 0.25;
    private const double Bonus = 0.5;

    public override string Name => KeywordName;

    // +50% when the target is wounded (at or below 25% of their max HP).
    public override double GetBonus(CombatEntity actor, CombatEntity target, bool actorIsAlly, string actionId, IKeywordUsageStore store)
    {
        bool triggered = target.Hp <= Threshold * target.MaxHp;
        double bonus = triggered ? Bonus : 0.0;
        Logger.Debug($"[keyword] Cruel: {target.Name} hp={target.Hp} threshold={Threshold * target.MaxHp:F1} -> bonus={bonus:F2}");
        return bonus;
    }
}
