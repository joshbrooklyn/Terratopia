namespace GameEngine.DataClasses;

public class Dungeon : IGameDataObject
{
    public string Id => DungeonId;
    public string DungeonId { get; init; } = "";
    public string Name { get; init; } = "";
    public List<string> MonsterIds { get; init; } = [];
}