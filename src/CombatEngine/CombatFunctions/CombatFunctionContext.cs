using CombatEngine.DataClasses;
using CombatEngine.Enums;

namespace CombatEngine.CombatFunctions;

// Everything a CombatFunction may touch, built fresh by CombatEngineClass once per resolved
// command. The Func/Action members are the engine's standard implementations, injected the same
// way CombatFlowMachine's constructor takes its eleven callbacks - a bespoke function overrides
// behaviour by declining to call one of them, not by subclassing the engine.
public sealed class CombatFunctionContext
{
    // ---- What is being resolved -------------------------------------------------------
    public required CombatCommand            Command     { get; init; }
    public required CombatEntity             Actor       { get; init; }
    public required bool                     ActorIsAlly { get; init; }
    public required CombatFunctionParameters Parameters  { get; init; }

    // Command.ChosenTargets resolved to entities, in order and INCLUDING duplicates
    // (NumAttacks + AllowMultipleAttackOnSameTarget can repeat a target).
    public required IReadOnlyList<CombatEntity> Targets { get; init; }

    // Full roster, for functions that reach beyond ChosenTargets (e.g. a party-wide buff).
    public required IReadOnlyDictionary<string, CombatEntity> AllEntities { get; init; }
    public required Func<string, CombatEntity>                GetEntity   { get; init; }

    // The engine's RNG, for bespoke rolls. Standard rolls go through TryEvade/RollCrit so the
    // draw order stays identical to the pre-refactor engine.
    public required Random Rng { get; init; }

    // ---- Standard steps, injected -----------------------------------------------------
    // TP is split so a function can keep the standard deduction but compute a bespoke cost,
    // or vice versa.
    public required Func<int>                 ResolveTpCost { get; init; }
    public required Action<CombatEntity, int> DeductTp      { get; init; }

    // True when the attack was evaded; also decays the target's Evasion and raises AttackEvaded.
    public required Func<CombatEntity, CombatEntity, bool> TryEvade { get; init; }

    // Crit is split the same way as TP: RollCrit owns the chance, ApplyCritModifier owns the
    // multiplier, so a function can override either half.
    public required Func<CombatEntity, bool>     RollCrit          { get; init; }
    public required Func<CombatEntity, int, int> ApplyCritModifier { get; init; }

    // (basePowerFactor, actor, target) => summed, individually-capped keyword bonus. Raises
    // KeywordApplied per contributing keyword. Consumes no randomness.
    public required Func<double, CombatEntity, CombatEntity, double> ApplyKeywordBonuses { get; init; }

    public required Func<CombatEntity, CombatEntity, double, DamageOrHealCalcType, int> CalculateDamageAmount { get; init; }
    public required Func<CombatEntity, CombatEntity, double, DamageOrHealCalcType, int> CalculateHealAmount   { get; init; }

    // Writes Hp, raises EntityDamaged, and runs death passives if Hp hit 0.
    public required Action<CombatEntity, CombatEntity, int, bool> ApplyDamage { get; init; }

    // Clamps to MaxHp, raises EntityHealed. No-ops on a dead target.
    public required Action<CombatEntity, CombatEntity, int> ApplyHeal { get; init; }

    // (target, stat, isPositive, rounds, untilRemoved) => applies a buff/debuff, raising BuffDebuffApplied,
    // or BuffDebuffExpired when an opposite-polarity entry cancels the existing one out. When
    // untilRemoved is true, rounds is ignored and the entry never expires from the round clock.
    public required Action<CombatEntity, BuffDebuffStat, bool, int, bool> ApplyBuffDebuff { get; init; }

    // (selector) => the living entities a buffsDebuffs[] entry lands on, resolved relative to
    // Actor and independent of Targets except for BuffDebuffTarget.SelectedTargets. RandomAlly/
    // RandomEnemy draw from the engine's shared Rng.
    public required Func<BuffDebuffTarget, IReadOnlyList<CombatEntity>> ResolveBuffDebuffTargets { get; init; }

    // Convenience: cost resolution + deduction, the first line of any function without bespoke
    // TP rules.
    public void DeductTpCost() => DeductTp(Actor, ResolveTpCost());
}
