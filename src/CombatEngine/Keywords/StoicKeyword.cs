using CombatEngine.DataClasses;
using CombatEngine.Engine;

namespace CombatEngine.Keywords;

public class StoicKeyword : PowerKeyword
{
    public const string KeywordName = "Stoic";

    public override string Name => KeywordName;

    // +50% when the actor is wounded (at or below 25% of their own max HP).
    public override double GetBonus(CombatEntity actor, CombatEntity target, bool actorIsAlly, string actionId, IKeywordUsageStore store)
    {
        var settings = CombatBalance.Current.Keywords.Stoic;
        bool triggered = actor.Hp <= settings.Threshold * actor.MaxHp;
        double bonus = triggered ? settings.Bonus : 0.0;
        Logger.Debug($"[keyword] Stoic: {actor.Name} hp={actor.Hp} threshold={settings.Threshold * actor.MaxHp:F1} -> bonus={bonus:F2}");
        return bonus;
    }
}
