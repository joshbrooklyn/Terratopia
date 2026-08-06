using CombatEngine.DataClasses;
using CombatEngine.Engine;
using CombatEngine.Keywords;

namespace CombatEngine.CombatFunctions;

// Everything a CombatFunction may touch, built fresh by CombatEngineClass once per resolved
// command. The methods below are the engine's standard implementations of each step - a bespoke
// function overrides behaviour by declining to call one of them, not by subclassing the engine.
public sealed class CombatFunctionContext
{
    // The engine collaborators the standard steps run through. Internal, so the context stays
    // engine-constructible only; an internal ctor rather than internal `required` members because
    // a required member may not be less visible than its containing type.
    internal readonly CombatRoster       Roster;
    internal readonly KeywordResolver    Keywords;
    internal readonly List<PowerKeyword> ActiveKeywords;

    internal CombatFunctionContext(CombatRoster roster, KeywordResolver keywords, List<PowerKeyword> activeKeywords)
    {
        Roster         = roster;
        Keywords       = keywords;
        ActiveKeywords = activeKeywords;
    }

    // ---- What is being resolved -------------------------------------------------------
    public required CombatCommand Command     { get; init; }
    public required CombatEntity  Actor       { get; init; }
    public required bool          ActorIsAlly { get; init; }

    // Command.ChosenTargets resolved to entities, in order and INCLUDING duplicates
    // (NumAttacks + AllowMultipleAttackOnSameTarget can repeat a target).
    public required IReadOnlyList<CombatEntity> Targets { get; init; }

    // The engine's RNG, for bespoke rolls. Standard rolls go through TryEvade/RollCrit so the
    // draw order stays identical to the pre-refactor engine.
    public required Random Rng { get; init; }

    public CombatFunctionParameters Parameters => Command.Parameters;

    // ---- Standard steps -----------------------------------------------------------------
    // Monsters don't spend TP on actions today; MonsterAction.TPCost is reserved for a future
    // feature. No-ops on a 0-TP command, so there is no reason to guard a call to this.
    public void DeductTpCost()
    {
        if (ActorIsAlly)
            Actor.SpendTp(Command.TPCost, Command.SourceId, Command.SourceName);
    }

    // True when the attack is evaded. Evasion decays 25% on each successful dodge.
    public bool TryEvade(CombatEntity target)
    {
        float roll = Rng.NextSingle();
        if (roll >= target.Evasion)
        {
            Logger.Debug($"[combat] TryEvade: {target.Name} roll={roll:F3} vs evasion={target.Evasion:F3} -> not evaded");
            return false;
        }

        target.RegisterEvasion(Actor, roll, Command.SourceId, Command.SourceName);
        return true;
    }

    // Crit is split the same way as TP: RollCrit owns the chance, ApplyCritModifier owns the
    // multiplier, so a function can override either half.
    public bool RollCrit()
    {
        float roll = Rng.NextSingle();
        bool isCrit = roll < Actor.CritChance;
        Logger.Debug($"[combat] RollCrit: {Actor.Name} roll={roll:F3} vs critChance={Actor.CritChance:F3} -> {(isCrit ? "crit" : "no crit")}");
        return isCrit;
    }

    public int ApplyCritModifier(int damage)
    {
        int result = (int)(damage * (CombatBalance.Current.CritBaseMultiplier + Actor.CritModifier));
        Logger.Debug($"[combat] ApplyCritModifier: {Actor.Name} damage={damage} critModifier={Actor.CritModifier:F3} -> {result}");
        return result;
    }
}
