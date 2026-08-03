using CombatEngine.DataClasses;
using CombatEngine.Enums;

namespace CombatEngine.Engine;

// The damage and healing formulas. Pure functions of the actor, the target and the action's
// effective power factor - they read no engine state, raise no events and consume no randomness,
// which is what lets the numbers be reasoned about without a running combat. Reached by a
// CombatFunction through CombatFunctionContext.CalculateDamage/CalculateHealAmount, not directly.
internal static class CombatMath
{
    // The shared half of the formula: action power scaled by calc type, doubled, plus level bump.
    private static double CalculateBaseAmount(CombatEntity actor, double effectivePowerFactor, DamageCalcType calcType)
    {
        double actionPower = calcType == DamageCalcType.FixedPower
            ? effectivePowerFactor
            : actor.Power * effectivePowerFactor;
        double baseAmount = calcType == DamageCalcType.FixedDamage
            ? effectivePowerFactor
            : (actionPower * 2f) + (actor.Level * 5f);
        Logger.Debug($"[math] CalculateBaseAmount: {actor.Name} calcType={calcType} actionPower={actionPower:F2} -> baseAmount={baseAmount:F2}");
        return baseAmount;
    }

    internal static int CalculateDamage(
        CombatEntity   actor,
        CombatEntity   target,
        double         effectivePowerFactor,
        DamageCalcType calcType)
    {
        double baseDamage = CalculateBaseAmount(actor, effectivePowerFactor, calcType);

        double rawDamage;
        rawDamage = (baseDamage / ((target.Defense + 128f) / 128f)) - (target.Defense / 2f);

        int damage = (int)Math.Max(0f, rawDamage);

        Logger.Debug($"[math] CalculateDamage: {actor.Name} -> {target.Name} baseDamage={baseDamage:F2} defense={target.Defense:F2} rawDamage={rawDamage:F2} -> damage={damage}");

        return damage;
    }

    // Same formula as damage, minus the target's Defense divisor - healing ignores defense.
    internal static int CalculateHealAmount(CombatEntity actor, double effectivePowerFactor, DamageCalcType calcType)
    {
        int amount = (int)Math.Max(0f, CalculateBaseAmount(actor, effectivePowerFactor, calcType));
        Logger.Debug($"[math] CalculateHealAmount: {actor.Name} -> amount={amount}");
        return amount;
    }
}
