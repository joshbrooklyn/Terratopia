using CombatEngine.Enums;

namespace CombatEngine.DataClasses;

// One authored entry in CombatFunctionParameters.TriggeredEffectsApplied. Both fields are
// mandatory - the schema marks triggeredEffect/target both required inside each array entry.
// Unlike BuffDebuffSpec and RegenDrainSpec, there is no rounds/untilRemoved/cancelOn* pairing:
// triggered effects don't expire and aren't tied to their applier, so granting one is a one-shot
// event, not a timed effect.
public class TriggeredEffectApplySpec
{
    // Resolved against TriggeredEffectRegistry the same way CombatFunction (the string) is
    // resolved against CombatFunctionRegistry. An unrecognised name is silently dropped by
    // TriggeredEffectTracker.Add, not an authoring error.
    public required string           TriggeredEffect { get; init; }
    public required BuffDebuffTarget Target          { get; init; }
}
