namespace CombatEngine.Enums;

// The resources a timed regen/drain can move. Hand-mirrored by the regenDrainStat enum in
// tech/item/monsteraction.schema.json, so data can never author a regen/drain that silently does
// nothing. Widening it is a schema bump.
public enum RegenDrainStat
{
    Hp,
    Tp
}
