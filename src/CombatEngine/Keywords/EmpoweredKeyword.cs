using CombatEngine.DataClasses;

namespace CombatEngine.Keywords;

public class EmpoweredKeyword : PowerKeyword
{
    public const string KeywordName = "Empowered";
    private const double Threshold = 0.75;
    private const double Bonus = 0.5;

    public override string Name => KeywordName;

    // +50% when the actor is healthy (at or above 75% of their own max HP).
    public override double GetBonus(CombatEntity actor, CombatEntity target, bool actorIsAlly, string actionId, IKeywordUsageStore store) =>
        actor.Hp >= Threshold * actor.MaxHp ? Bonus : 0.0;
}
