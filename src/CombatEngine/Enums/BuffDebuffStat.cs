namespace CombatEngine.Enums;

// The stats a timed buff/debuff can move. Deliberately limited to the stats CombatEntity actually
// applies a modifier to - hand-mirrored by the buffDebuffStat enum in tech/item/monsteraction.schema.json,
// so data can never author a buff that silently does nothing. Widening it is a schema bump.
public enum BuffDebuffStat
{
    Power,
    Defense,
    Speed
}
