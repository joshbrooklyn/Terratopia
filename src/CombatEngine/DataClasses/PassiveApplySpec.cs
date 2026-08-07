using CombatEngine.Enums;

namespace CombatEngine.DataClasses;

// One authored entry in CombatFunctionParameters.PassivesApplied. Both fields are mandatory - the
// schema marks passive/target both required inside each array entry. Unlike BuffDebuffSpec and
// RegenDrainSpec, there is no rounds/untilRemoved/cancelOn* pairing: passives don't expire and
// aren't tied to their applier, so granting one is a one-shot event, not a timed effect.
public class PassiveApplySpec
{
    // Resolved against PassiveRegistry the same way CombatFunction (the string) is resolved
    // against CombatFunctionRegistry. An unrecognised name is silently dropped by
    // PassiveTracker.Add, not an authoring error.
    public required string           Passive { get; init; }
    public required BuffDebuffTarget Target  { get; init; }
}
