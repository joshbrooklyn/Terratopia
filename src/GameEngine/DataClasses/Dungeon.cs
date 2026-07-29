namespace GameEngine.DataClasses;

public class Dungeon : IGameDataObject
{
    public static string SchemaResourceName => "GameEngine.Schemas.dungeon.schema.json";

    public string Id => DungeonId;
    public required int SchemaVersion { get; init; }
    public string DungeonId { get; init; } = "";
    public string Name { get; init; } = "";
    public List<string> MonsterIds { get; init; } = [];
}