namespace GameEngine.DataClasses;

public record CombatantInventoryEntry(string EntityId, string Name, string Description, IReadOnlyList<string> Keywords);

public record CombatantSeed(
    string EntityId, string Name,
    int Hp, int MaxHp, int Tp, int MaxTp,
    int Level, int Power, int Defense, int Speed,
    float Evasion, float CritChance,
    IReadOnlyList<CombatantInventoryEntry> Techs,
    IReadOnlyList<CombatantInventoryEntry> Items,
    IReadOnlyList<CombatantInventoryEntry> Perks,
    IReadOnlyList<CombatantInventoryEntry> Gear,
    IReadOnlyList<string> TriggeredEffects);

public record CombatStartData(
    IReadOnlyList<CombatantSeed> Allies,
    IReadOnlyList<CombatantSeed> Enemies);
