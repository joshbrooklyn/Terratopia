using CombatEngine.DataClasses;
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
    public int NumAttacks { get; init; } = 1;
    public TargetingType TargetingType { get; init; } = TargetingType.Choose;
    public required ValidTarget ValidTargets { get; init; }
    public required LivingOrDead LivingOrDead { get; init; }
    public bool? AllowMultipleAttackOnSameTarget { get; init; }
    public required string CombatFunction { get; init; }
    public CombatFunctionParameters Parameters { get; init; } = new();
}
