using CombatEngine.Enums;

namespace CombatEngine.DataClasses;

// One authored entry in CombatFunctionParameters.BuffsDebuffs. Unlike CombatFunctionParameters
// itself, every field here is mandatory - the schema marks stat/type/target/rounds/untilRemoved/
// cancelOnEntityDeath/cancelOnApplierDeath all required inside each array entry, so there is no
// cross-field pairing left to validate at combat time (only the no-duplicate-stat-per-target rule,
// enforced in CombatFunction.ApplyBuffsDebuffs).
public class BuffDebuffSpec
{
    public required BuffDebuffStat   Stat         { get; init; }
    public required BuffDebuffType   Type         { get; init; }
    public required BuffDebuffTarget Target       { get; init; }
    public required int              Rounds       { get; init; }
    // When true, Rounds is ignored - the entry never ticks down and only goes away via an
    // opposite-polarity application on the same stat (see CombatEntity.AddBuffDebuff).
    public required bool             UntilRemoved { get; init; }
    // When true, this entry is removed the moment the entity holding it dies, before EntityDeath is
    // raised - see CombatEntity.HandleDefeat. When false it survives death and would still be
    // active on a later Revive.
    public required bool             CancelOnEntityDeath  { get; init; }
    // When true, this entry is removed the moment the entity that applied it dies, even though the
    // entity holding it stays alive - see CombatEntity.CancelEffectsAppliedBy, invoked by
    // CombatRoster on CombatEventBus.EntityDeath for every other still-living entity.
    public required bool             CancelOnApplierDeath { get; init; }
}
