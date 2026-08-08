namespace GameEngine.DataClasses;

public class Monster : IGameDataObject
{
    public static string SchemaResourceName => "GameEngine.Schemas.monster.schema.json";

    public string Id => MonsterId;
    public required int SchemaVersion { get; init; }
    public string MonsterId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    // Primary stats
    public int Level { get; init; } = 1;
    public int HpBase { get; init; }
    public int HpPerLevel { get; init; }
    public int PowerBase { get; init; }
    public int PowerPerLevel { get; init; }
    public int DefenseBase { get; init; }
    public int DefensePerLevel { get; init; }
    public int SpeedBase { get; init; }
    public int SpeedBasePerLevel { get; init; }

    public List<string> MonsterActionIds { get; init; } = [];
    public List<string>? TriggeredEffects { get; init; }

    // Combat access
    public bool CanUseFightAction { get; init; }
}