using CombatEngine.Enums;

namespace CombatEngine.DataClasses;

public class CombatDirectEffect
{
    public CombatDirectEffectType EffectType { get; init; }
    public ElementType? Element { get; init; }
    public DamageCalcType CalcType { get; init; }
    public double PowerFactor { get; init; }
}