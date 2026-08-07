namespace CombatEngine.CombatFunctions;

// Applies no damage or healing of its own - just the TP cost and any authored
// buffsDebuffs/regensDrains/passivesApplied riders. For actions that are pure status effects
// (e.g. a buff-only tech) with no direct HP impact.
public class NoDirectEffectsFunction : CombatFunction
{
    public const string FunctionName = "NoDirectEffects";

    public override string Name => FunctionName;

    public override void Execute(CombatFunctionContext ctx)
    {
        ctx.DeductTpCost();
        ApplyBuffsDebuffs(ctx);
        ApplyRegensDrains(ctx);
        ApplyPassives(ctx);
    }
}
