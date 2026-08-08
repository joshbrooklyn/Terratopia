namespace CombatEngine.CombatFunctions;

// Restores Hp using the standard formula minus the Defense divisor. Deliberately narrower than
// BasicDamage: a heal on an ally is not dodgeable and does not crit, so it consumes NO randomness
// at all - which keeps the seeded draw order of every other test easy to reason about. Keyword
// power bonuses DO apply. Healing a dead target is a no-op; revival is a future dedicated
// function, not a side effect of healing.
public class BasicHealFunction : CombatFunction
{
    public const string FunctionName = "BasicHeal";

    public override string Name => FunctionName;

    public override void Execute(CombatFunctionContext ctx)
    {
        ctx.DeductTpCost();
        CalculateAndApplyHealing(ctx);
        ApplyBuffsDebuffs(ctx);
        ApplyRegensDrains(ctx);
        ApplyTriggeredEffects(ctx);
    }
}
