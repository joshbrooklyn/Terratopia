using CombatEngine.Enums;

namespace GameEngine.DataClasses;

public class Tech : IGameDataObject
{
    public string Id => TechId;
    public string TechId { get; init; } = "";
    public string Name { get; init; } = "";
    public string JobClass { get; init; } = "";
    public int TpCost { get; init; }
    public int Tier { get; init; }
    public int Rarity { get; init; }
    public string Description { get; init; } = "";
    public int NumAttacks { get; init; } = 1;
    public List<string> Keywords { get; init; } = [];
    public List<string> Traits { get; init; } = [];
    public TargetingType TargetingType { get; init; } = TargetingType.Choose;
    public required ValidTarget ValidTargets { get; init; }
    public required LivingOrDead LivingOrDead { get; init; }
    public bool? AllowMultipleAttackOnSameTarget { get; init; }
    public List<string> TargetStatuses { get; init; } = [];
    public List<string> UserStatuses { get; init; } = [];
    public List<TechDirectEffect> DirectEffects { get; init; } = [];
}

public record TechDirectEffect
{
    public CombatDirectEffectType EffectType  { get; init; }
    public ElementType?           Element     { get; init; }
    public DamageCalcType         CalcType    { get; init; } = DamageCalcType.StandardFormula;
    public double                 PowerFactor { get; init; } = 1.0f;
}
