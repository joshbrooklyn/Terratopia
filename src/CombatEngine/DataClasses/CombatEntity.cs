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

    internal string EntityId { get; set; } = string.Empty;
    internal string Name { get; set; } = string.Empty;

    // Primary stats
    internal int Level { get; set; }
    internal int MaxHp { get; set; }
    internal int Hp { get; set; }
    internal int MaxTp { get; set; }     // adventurers only; 0 for enemies
    internal int Tp { get; set; }
    internal int Power { get; set; }
    internal int Defense { get; set; }
    internal int Speed { get; set; }

    // Secondary stats
    internal float Evasion { get; set; }

    internal bool IsDead { get; set; } = false ;
    internal float CritChance { get; set; }
    internal float CritModifier { get; set; } = 0.5f;   // default +50% on crit

    internal List<string> Passives { get; set; } = new();
    internal HashSet<string> ConsumedPassives { get; } = new();
}
