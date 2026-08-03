using CombatEngine.DataClasses;
using CombatEngine.Enums;

namespace GameEngine.DataClasses;

public class Item : IGameDataObject
{
    public static string SchemaResourceName => "GameEngine.Schemas.item.schema.json";

    public string Id => ItemId;
    public required int SchemaVersion { get; init; }
    public string ItemId { get; init; } = "";
    public string Name { get; init; } = "";
    public int Rarity { get; init; }
    public string Description { get; init; } = "";
    public required int MaxUses { get; init; }
    public int NumAttacks { get; init; } = 1;
    public List<string> Keywords { get; init; } = [];
    public List<string> Traits { get; init; } = [];
    public TargetingType TargetingType { get; init; } = TargetingType.Choose;
    public required ValidTarget ValidTargets { get; init; }
    public required LivingOrDead LivingOrDead { get; init; }
    public bool? AllowMultipleAttackOnSameTarget { get; init; }
    public required string CombatFunction { get; init; }
    public CombatFunctionParameters Parameters { get; init; } = new();
}
