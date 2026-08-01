namespace CombatEngine.CombatFunctions;

// Pays the TP cost and does nothing else. A legitimate authored action - a "pass"/"defend" that
// only exists to burn a turn - and the explicit way to say "this command resolves to nothing".
public class NoOpFunction : CombatFunction
{
    public const string FunctionName = "NoOp";

    public override string Name => FunctionName;

    public override void Execute(CombatFunctionContext ctx) => ctx.DeductTpCost();
}
