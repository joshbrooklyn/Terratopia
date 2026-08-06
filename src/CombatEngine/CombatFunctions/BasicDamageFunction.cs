namespace CombatEngine.CombatFunctions;

// The pre-CombatFunction engine behaviour, verbatim: deduct TP, then for each chosen target roll
// evasion, add keyword bonuses to this action's base power factor, run the standard damage
// formula, roll a crit, apply. Stateless - the registry hands out one shared instance.
public class BasicDamageFunction : CombatFunction
{
    public const string FunctionName = "BasicDamage";

    public override string Name => FunctionName;

    public override void Execute(CombatFunctionContext ctx)
    {
        ctx.DeductTpCost();
        CalculateAndApplyDamage(ctx);
        ApplyBuffsDebuffs(ctx);
        ApplyRegensDrains(ctx);
    }
}
