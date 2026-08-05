namespace CombatEngine.Enums;

// Whether a timed buff/debuff raises or lowers its stat - hand-mirrored by the buffsDebuffs[].type
// enum in tech/item/monsteraction.schema.json, the same way BuffDebuffStat is. Widening it is a
// schema bump.
public enum BuffDebuffType
{
    Positive,
    Negative
}
