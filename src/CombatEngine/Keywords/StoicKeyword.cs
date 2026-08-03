using CombatEngine.DataClasses;
using CombatEngine.Engine;

namespace CombatEngine.Keywords;

public class StoicKeyword : PowerKeyword
{
    public const string KeywordName = "Stoic";
    private const double Threshold = 0.25;
    private const double Bonus = 0.5;

    public override string Name => KeywordName;

    // +50% when the actor is wounded (at or below 25% of their own max HP).
    public override double GetBonus(CombatEntity actor, CombatEntity target, bool actorIsAlly, string actionId, IKeywordUsageStore store)
    {
        bool triggered = actor.Hp <= Threshold * actor.MaxHp;
        double bonus = triggered ? Bonus : 0.0;
        Logger.Debug($"[keyword] Stoic: {actor.Name} hp={actor.Hp} threshold={Threshold * actor.MaxHp:F1} -> bonus={bonus:F2}");
        return bonus;
    }
}
