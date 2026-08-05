using CombatEngine.Enums;

namespace CombatEngine.DataClasses;

// One authored entry in CombatFunctionParameters.RegensDrains. Every field is mandatory - the
// schema marks stat/type/target/rounds/untilRemoved all required inside each array entry, so there
// is no cross-field pairing left to validate at combat time (only the no-duplicate-stat-per-target
// rule, enforced in CombatFunction.ApplyRegensDrains). Mirrors BuffDebuffSpec field for field.
public class RegenDrainSpec
{
    public required RegenDrainStat   Stat         { get; init; }
    public required RegenDrainType   Type         { get; init; }
    public required BuffDebuffTarget Target       { get; init; }
    public required int              Rounds       { get; init; }
    // When true, Rounds is ignored - the entry never ticks down and only goes away via an
    // opposite-polarity application on the same stat (see CombatEntity.AddRegenDrain).
    public required bool             UntilRemoved { get; init; }
}
