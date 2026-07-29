using CombatEngine.Enums;

namespace GameEngine.DataClasses;

public class MonsterAction : IGameDataObject
{
    public static string SchemaResourceName => "GameEngine.Schemas.monsteraction.schema.json";

    public string Id => MonsterActionId;
    public required int SchemaVersion { get; init; }
    public string MonsterActionId { get; init; } = "";
    public string Name { get; init; } = "";
    public string JobClass { get; init; } = "";
    public int TpCost { get; init; }
    public int Tier { get; init; }
    public string Description { get; init; } = "";
    public List<string> Keywords { get; init; } = [];
    public List<string> Traits { get; init; } = [];
    public int NumTargets { get; init; } = 1;
    public TargetingType TargetingType { get; init; } = TargetingType.Choose;
    public required ValidTarget ValidTargets { get; init; }
    public required LivingOrDead LivingOrDead { get; init; }
    public List<string> TargetStatuses { get; init; } = [];
    public List<string> UserStatuses { get; init; } = [];
    public List<MonsterActionDirectEffect> DirectEffects { get; init; } = [];
}

public record MonsterActionDirectEffect
{
    public CombatDirectEffectType EffectType  { get; init; }
    public ElementType?           Element     { get; init; }
    public DamageCalcType         CalcType    { get; init; } = DamageCalcType.StandardFormula;
    public double                 PowerFactor { get; init; } = 1.0f;
}
