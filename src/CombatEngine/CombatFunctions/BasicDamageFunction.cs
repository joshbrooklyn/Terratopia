using CombatEngine.Enums;

namespace CombatEngine.CombatFunctions;

// The pre-CombatFunction engine behaviour, verbatim: deduct TP, then for each chosen target roll
// evasion, add keyword bonuses to this action's base power factor, run the standard damage
// formula, roll a crit, apply. Stateless - the registry hands out one shared instance.
public class BasicDamageFunction : CombatFunction
{
    public const string FunctionName = "BasicDamage";

    private const double DefaultPowerFactor = 1.0;

    public override string Name => FunctionName;

    public override void Execute(CombatFunctionContext ctx)
    {
        // Element doesn't feed the formula yet - it's what the UI reports and what elemental
        // resistances will key off. A null element means non-elemental (physical).
        double         basePowerFactor = ctx.Parameters.PowerFactor ?? DefaultPowerFactor;
        DamageCalcType calcType        = ctx.Parameters.CalcType    ?? DamageCalcType.StandardFormula;

        ctx.DeductTpCost();

        foreach (var target in ctx.Targets)
        {
            if (ctx.TryEvade(ctx.Actor, target))
                continue;

            double keywordBonus         = ctx.ApplyKeywordBonuses(basePowerFactor, ctx.Actor, target);
            double effectivePowerFactor = basePowerFactor + keywordBonus;

            int  damage = ctx.CalculateDamage(ctx.Actor, target, effectivePowerFactor, calcType);
            bool isCrit = ctx.RollCrit(ctx.Actor);
            if (isCrit)
                damage = ctx.ApplyCritModifier(ctx.Actor, damage);

            ctx.ApplyDamage(ctx.Actor, target, damage, isCrit);
        }
    }
}
