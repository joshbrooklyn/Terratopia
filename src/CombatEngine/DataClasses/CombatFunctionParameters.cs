using CombatEngine.Enums;

namespace CombatEngine.DataClasses;

// The single, closed, hand-maintained superset of every parameter any CombatFunction can read -
// mirrored one-for-one by the "parameters" block in tech/item/monsteraction.schema.json, the same
// way the keywords enum is hand-mirrored against PowerKeywordRegistry.
//
// EVERY field is nullable and optional. JSON Schema can't express "BasicDamage needs an element",
// so each function validates what it needs in its own Execute and defaults the rest. Nullable
// rather than defaulted matters: `double PowerFactor { get; init; } = 1.0` can't distinguish
// "omitted" from "authored as 1.0", which kills per-function validation.
//
// Adding a parameter = one nullable property here + the same field in all three schemas +
// a schemaVersion bump + a migration step.
public class CombatFunctionParameters
{
    public ElementType?    Element     { get; init; }
    public DamageCalcType? CalcType    { get; init; }
    public double?         PowerFactor { get; init; }
}
