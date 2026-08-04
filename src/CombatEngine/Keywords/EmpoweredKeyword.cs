using CombatEngine.DataClasses;
using CombatEngine.Engine;

namespace CombatEngine.Keywords;

public class EmpoweredKeyword : PowerKeyword
{
    public const string KeywordName = "Empowered";

    public override string Name => KeywordName;

    // +50% when the actor is healthy (at or above 75% of their own max HP).
    public override double GetBonus(CombatEntity actor, CombatEntity target, bool actorIsAlly, string actionId, IKeywordUsageStore store)
    {
        var settings = CombatBalance.Current.Keywords.Empowered;
        bool triggered = actor.Hp >= settings.Threshold * actor.MaxHp;
        double bonus = triggered ? settings.Bonus : 0.0;
        Logger.Debug($"[keyword] Empowered: {actor.Name} hp={actor.Hp} threshold={settings.Threshold * actor.MaxHp:F1} -> bonus={bonus:F2}");
        return bonus;
    }
}
