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
    public ElementType?          Element     { get; init; }
    public DamageOrHealCalcType? CalcType    { get; init; }
    public double?               PowerFactor { get; init; }

    // Timed buffs/debuffs, applied once each after the action fully resolves - after all hits, all
    // damage/healing, and regardless of what was evaded. Each entry is self-contained (schema
    // marks stat/type/target/rounds required) and carries its own target selector, so it need not
    // land on the action's own targets.
    public IReadOnlyList<BuffDebuffSpec>? BuffsDebuffs { get; init; }

    // Timed regen/drain (heal/damage a fixed % of MaxHp or MaxTp at the start of every round),
    // applied once each the same way as BuffsDebuffs.
    public IReadOnlyList<RegenDrainSpec>? RegensDrains { get; init; }

    // Passives granted once after the action fully resolves, the same way as BuffsDebuffs. Unlike
    // the two above, an entry has no duration - PassiveTracker.Add either grants it outright or,
    // if the target already owns it, no-ops.
    public IReadOnlyList<PassiveApplySpec>? PassivesApplied { get; init; }
}
