using CombatEngine.DataClasses;

namespace CombatEngine.Keywords;

public class EngageKeyword : PowerKeyword
{
    public const string KeywordName = "Engage";
    private const double Threshold = 0.75;
    private const double Bonus = 0.5;

    public override string Name => KeywordName;

    // +50% when the target is healthy (at or above 75% of their max HP).
    public override double GetBonus(CombatEntity actor, CombatEntity target, bool actorIsAlly, string actionId, IKeywordUsageStore store) =>
        target.Hp >= Threshold * target.MaxHp ? Bonus : 0.0;
}
