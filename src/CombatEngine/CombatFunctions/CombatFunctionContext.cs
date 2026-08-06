using CombatEngine.DataClasses;
using CombatEngine.Engine;
using CombatEngine.Enums;
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
    private readonly CombatRoster       _roster;
    private readonly KeywordResolver    _keywords;
    private readonly List<PowerKeyword> _activeKeywords;

    internal CombatFunctionContext(CombatRoster roster, KeywordResolver keywords, List<PowerKeyword> activeKeywords)
    {
        _roster         = roster;
        _keywords       = keywords;
        _activeKeywords = activeKeywords;
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

    // Full roster, for functions that reach beyond ChosenTargets (e.g. a party-wide buff).
    public IReadOnlyDictionary<string, CombatEntity> AllEntities => _roster.AllEntities;

    public CombatEntity GetEntity(string entityId) => _roster.GetEntity(entityId);

    // ---- Standard steps -----------------------------------------------------------------
    // TP is split so a function can keep the standard deduction but compute a bespoke cost,
    // or vice versa.
    public int ResolveTpCost() => Command.TPCost;

    // Monsters don't spend TP on actions today; MonsterAction.TPCost is reserved for a future feature.
    public void DeductTp(CombatEntity entity, int amount)
    {
        if (ActorIsAlly)
            entity.SpendTp(amount, Command.SourceId, Command.SourceName);
    }

    // Convenience: cost resolution + deduction, the first line of any function without bespoke
    // TP rules.
    public void DeductTpCost() => DeductTp(Actor, ResolveTpCost());

    // True when the attack is evaded. Evasion decays 25% on each successful dodge.
    public bool TryEvade(CombatEntity actor, CombatEntity target)
    {
        float roll = Rng.NextSingle();
        if (roll >= target.Evasion)
        {
            Logger.Debug($"[combat] TryEvade: {target.Name} roll={roll:F3} vs evasion={target.Evasion:F3} -> not evaded");
            return false;
        }

        target.RegisterEvasion(actor, roll, Command.SourceId, Command.SourceName);
        return true;
    }

    // Crit is split the same way as TP: RollCrit owns the chance, ApplyCritModifier owns the
    // multiplier, so a function can override either half.
    public bool RollCrit(CombatEntity actor)
    {
        float roll = Rng.NextSingle();
        bool isCrit = roll < actor.CritChance;
        Logger.Debug($"[combat] RollCrit: {actor.Name} roll={roll:F3} vs critChance={actor.CritChance:F3} -> {(isCrit ? "crit" : "no crit")}");
        return isCrit;
    }

    public int ApplyCritModifier(CombatEntity actor, int damage)
    {
        int result = (int)(damage * (CombatBalance.Current.CritBaseMultiplier + actor.CritModifier));
        Logger.Debug($"[combat] ApplyCritModifier: {actor.Name} damage={damage} critModifier={actor.CritModifier:F3} -> {result}");
        return result;
    }

    // (basePowerFactor, actor, target) => summed, individually-capped keyword bonus. Raises
    // KeywordApplied per contributing keyword. Consumes no randomness.
    public double ApplyKeywordBonuses(double basePowerFactor, CombatEntity actor, CombatEntity target) =>
        _keywords.ApplyKeywordBonuses(_activeKeywords, basePowerFactor, actor, target, ActorIsAlly, Command.SourceId, Command.SourceName);

    public int CalculateDamageAmount(CombatEntity actor, CombatEntity target, double powerFactor, DamageOrHealCalcType calcType) =>
        CombatMath.CalculateDamageAmount(actor, target, powerFactor, calcType);

    public int CalculateHealAmount(CombatEntity actor, CombatEntity target, double powerFactor, DamageOrHealCalcType calcType) =>
        CombatMath.CalculateHealAmount(actor, target, powerFactor, calcType);

    // Writes Hp, raises EntityDamaged, and runs death passives if Hp hit 0.
    public void ApplyDamage(CombatEntity actor, CombatEntity target, int damage, bool isCrit) =>
        target.TakeDamage(actor, damage, Command.SourceId, Command.SourceName, isCrit);

    // Clamps to MaxHp, raises EntityHealed. No-ops on a dead target.
    public void ApplyHeal(CombatEntity actor, CombatEntity target, int amount) =>
        target.Heal(actor, amount, Command.SourceId, Command.SourceName);

    // (target, stat, isPositive, rounds, untilRemoved, cancelOnEntityDeath, cancelOnApplierDeath) =>
    // applies a buff/debuff, raising BuffDebuffApplied, or BuffDebuffExpired when an
    // opposite-polarity entry cancels the existing one out. When untilRemoved is true, rounds is
    // ignored and the entry never expires from the round clock. cancelOnEntityDeath/
    // cancelOnApplierDeath control whether the entry is cancelled on the holder's own death or on
    // the applying actor's death (Actor.EntityId is invariant for the whole action, so it isn't
    // threaded as a parameter here).
    public void ApplyBuffDebuff(CombatEntity target, BuffDebuffStat stat, bool isPositive, int rounds, bool untilRemoved, bool cancelOnEntityDeath, bool cancelOnApplierDeath) =>
        target.AddBuffDebuff(stat, isPositive, rounds, untilRemoved, Command.SourceId, Command.SourceName, Actor.EntityId, cancelOnEntityDeath, cancelOnApplierDeath);

    // (selector) => the living entities a buffsDebuffs[]/regensDrains[] entry lands on, resolved
    // relative to Actor and independent of Targets except for BuffDebuffTarget.SelectedTargets.
    // RandomAlly/RandomEnemy draw from the engine's shared Rng.
    public IReadOnlyList<CombatEntity> ResolveBuffDebuffTargets(BuffDebuffTarget selector) =>
        _roster.ResolveBuffDebuffTargets(Actor, selector, Targets);

    // (target, stat, isPositive, rounds, untilRemoved, cancelOnEntityDeath, cancelOnApplierDeath) =>
    // applies a regen/drain, raising RegenDrainApplied, or RegenDrainExpired when an
    // opposite-polarity entry cancels the existing one out. When untilRemoved is true, rounds is
    // ignored and the entry never expires from the round clock. Targets are resolved via
    // ResolveBuffDebuffTargets, same as buffsDebuffs. cancelOnEntityDeath/cancelOnApplierDeath follow
    // the same rule as ApplyBuffDebuff above.
    public void ApplyRegenDrain(CombatEntity target, RegenDrainStat stat, bool isPositive, int rounds, bool untilRemoved, bool cancelOnEntityDeath, bool cancelOnApplierDeath) =>
        target.AddRegenDrain(stat, isPositive, rounds, untilRemoved, Command.SourceId, Command.SourceName, Actor.EntityId, cancelOnEntityDeath, cancelOnApplierDeath);
}
