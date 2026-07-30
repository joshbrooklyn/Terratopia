using CombatEngine.DataClasses;

namespace CombatEngine.Keywords;

public class StoicKeyword : PowerKeyword
{
    public const string KeywordName = "Stoic";
    private const double Threshold = 0.25;
    private const double Bonus = 0.5;

    public override string Name => KeywordName;

    // +50% when the actor is wounded (at or below 25% of their own max HP).
    public override double GetBonus(CombatEntity actor, CombatEntity target, bool actorIsAlly, string actionId, IKeywordUsageStore store) =>
        actor.Hp <= Threshold * actor.MaxHp ? Bonus : 0.0;
}
