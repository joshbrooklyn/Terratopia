using CombatEngine.DataClasses;

namespace CombatEngine.Keywords;

public class CruelKeyword : PowerKeyword
{
    public const string KeywordName = "Cruel";
    private const double Threshold = 0.25;
    private const double Bonus = 0.5;

    public override string Name => KeywordName;

    // +50% when the target is wounded (at or below 25% of their max HP).
    public override double GetBonus(CombatEntity actor, CombatEntity target, bool actorIsAlly, string actionId, IKeywordUsageStore store) =>
        target.Hp <= Threshold * target.MaxHp ? Bonus : 0.0;
}
