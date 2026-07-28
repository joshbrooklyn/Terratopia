namespace CombatEngine.DataClasses;

public class CombatEntity
{
    public CombatEntity(string entityId, string name, int level,
        int maxHp, int hp, int maxTp, int tp,
        int power, int defense, int speed,
        float evasion, float critChance, float critModifier,
        IReadOnlyList<string>? passives = null)
    {
        EntityId = entityId; Name = name; Level = level;
        MaxHp = maxHp; Hp = hp; MaxTp = maxTp; Tp = tp;
        Power = power; Defense = defense; Speed = speed;
        Evasion = evasion; CritChance = critChance; CritModifier = critModifier;
        Passives = passives?.ToList() ?? new();
    }

    public string EntityId { get; internal set; } = string.Empty;
    public string Name { get; internal set; } = string.Empty;

    // Primary stats
    public int Level { get; internal set; }
    public int MaxHp { get; internal set; }
    public int Hp { get; internal set; }
    public int MaxTp { get; internal set; }     // adventurers only; 0 for enemies
    public int Tp { get; internal set; }
    public int Power { get; internal set; }
    public int Defense { get; internal set; }
    public int Speed { get; internal set; }

    // Secondary stats
    public float Evasion { get; internal set; }

    public bool IsDead { get; internal set; } = false ;
    public float CritChance { get; internal set; }
    public float CritModifier { get; internal set; } = 0.5f;   // default +50% on crit

    public List<string> Passives { get; internal set; } = new();
    public HashSet<string> ConsumedPassives { get; } = new();
}
