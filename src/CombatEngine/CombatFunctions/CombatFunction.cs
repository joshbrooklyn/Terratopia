namespace CombatEngine.CombatFunctions;

// One CombatFunction owns the ENTIRE resolution of one action: the target loop, evasion, crit,
// damage/healing, bespoke impacts, and every event raised along the way. The engine hands it a
// context whose delegates are the *standard* implementations of each step - a function may call
// them, replace them, or skip them entirely.
//
// The registry hands out one shared instance per name, so implementations MUST be stateless.
//
// Contract:
//   1. Call ctx.DeductTpCost() (or bespoke TP logic) before touching a target. It no-ops on a
//      0-TP command, so there is no reason to guard it.
//   2. Validate your own parameters and throw InvalidOperationException naming
//      ctx.Command.ActionId. The "parameters" schema block marks every field optional, so the
//      function is the ONLY place requirements are enforced.
public abstract class CombatFunction
{
    public abstract string Name { get; }

    public abstract void Execute(CombatFunctionContext ctx);
}
