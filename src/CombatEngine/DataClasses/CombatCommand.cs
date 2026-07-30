using CombatEngine.Enums;

namespace CombatEngine.DataClasses;

public class CombatCommand
{
    public string ActorId { get; init; } = string.Empty;
    public TargetingType TargetingType { get; init; }
    public required ValidTarget ValidTargets { get; init; }
    public required LivingOrDead LivingOrDead { get; init; }
    public int TPCost { get; init; }
    public int NumAttacks { get; init; } = 1;
    public bool AllowMultipleAttackOnSameTarget { get; init; } = false;
    public List<CombatDirectEffect> DirectEffects { get; init; } = [];

    // Resolved into live PowerKeyword instances via PowerKeywordRegistry when this command
    // is executed - see docs/keywords.md.
    public List<string> Keywords { get; init; } = [];

    // Identifies the specific Tech/Item/MonsterAction this command came from, so stacking
    // keywords (e.g. Growth) can tell "used this action again" from "used a different action".
    public string ActionId { get; init; } = string.Empty;
    public List<string> ChosenTargets { get; internal set; } = [];
}
