using CombatEngine.DataClasses;
using CombatEngine.Engine;

namespace CombatEngine.Keywords;

public class EngageKeyword : PowerKeyword
{
    public const string KeywordName = "Engage";
    private const double Threshold = 0.75;
    private const double Bonus = 0.5;

    public override string Name => KeywordName;

    // +50% when the target is healthy (at or above 75% of their max HP).
    public override double GetBonus(CombatEntity actor, CombatEntity target, bool actorIsAlly, string actionId, IKeywordUsageStore store)
    {
        bool triggered = target.Hp >= Threshold * target.MaxHp;
        double bonus = triggered ? Bonus : 0.0;
        Logger.Debug($"[keyword] Engage: {target.Name} hp={target.Hp} threshold={Threshold * target.MaxHp:F1} -> bonus={bonus:F2}");
        return bonus;
    }
}
