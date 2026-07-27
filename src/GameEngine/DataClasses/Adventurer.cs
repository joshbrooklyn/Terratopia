
namespace GameEngine.DataClasses;

public class Adventurer : IGameDataObject
{
    public string Id => AdventurerId;
    public string AdventurerId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;

    // Primary stats
    public int Level { get; init; }
    public int MaxHp { get; init; }
    public int Hp { get; init; }
    public int MaxTp { get; init; }
    public int Tp { get; init; }
    public int Power { get; init; }
    public int Defense { get; init; }
    public int Speed { get; init; }

    // Secondary stats
    public float Evasion { get; init; }
    public bool IsDead => false;
    public float CritChance { get; init; }
    public float CritModifier { get; init; } = 0.5f;

    // Inventory
    public List<string> TechsIds { get; init; } = [];
    public List<string> ItemIds { get; init; } = [];

    // Combat access
    public bool CanFight { get; init; }
}
