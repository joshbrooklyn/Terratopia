using CombatEngine.DataClasses;
using CombatEngine.Engine;

namespace CombatEngine.Keywords;

public class EngageKeyword : PowerKeyword
{
    public const string KeywordName = "Engage";

    public override string Name => KeywordName;

    // +50% when the target is healthy (at or above 75% of their max HP).
    public override double GetBonus(CombatEntity actor, CombatEntity target, bool actorIsAlly, string actionId, IKeywordUsageStore store)
    {
        var settings = CombatBalance.Current.Keywords.Engage;
        bool triggered = target.Hp >= settings.Threshold * target.MaxHp;
        double bonus = triggered ? settings.Bonus : 0.0;
        Logger.Debug($"[keyword] Engage: {target.Name} hp={target.Hp} threshold={settings.Threshold * target.MaxHp:F1} -> bonus={bonus:F2}");
        return bonus;
    }
}
