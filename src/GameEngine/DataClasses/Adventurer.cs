
namespace GameEngine.DataClasses;

public class Adventurer : IGameDataObject
{
    public static string SchemaResourceName => "GameEngine.Schemas.adventurer.schema.json";

    public string Id => AdventurerId;
    public required int SchemaVersion { get; init; }
    public string AdventurerId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string JobId { get; init; } = string.Empty;

    // Secondary stats
    public float Evasion { get; init; }
    public bool IsDead => false;
    public float CritChance { get; init; }
    public float CritModifier { get; init; } = 0.5f;

    // Inventory
    public List<string> TechsIds { get; init; } = [];
    public List<string> ItemIds { get; init; } = [];

    // Combat access
    public bool CanUseFightAction { get; init; }
}
