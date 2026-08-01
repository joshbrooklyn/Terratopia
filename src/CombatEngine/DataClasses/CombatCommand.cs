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

    // Resolved to a live CombatFunction instance via CombatFunctionRegistry at execution time,
    // the same way Keywords resolve through PowerKeywordRegistry. Unknown names throw.
    public required string CombatFunction { get; init; }

    // Flat bag the resolved CombatFunction reads its inputs from. Always non-null; the function
    // decides which fields it requires.
    public CombatFunctionParameters Parameters { get; init; } = new();

    // Resolved into live PowerKeyword instances via PowerKeywordRegistry when this command
    // is executed - see docs/keywords.md.
    public List<string> Keywords { get; init; } = [];

    // Identifies the specific Tech/Item/MonsterAction this command came from, so stacking
    // keywords (e.g. Growth) can tell "used this action again" from "used a different action".
    public string ActionId { get; init; } = string.Empty;
    public List<string> ChosenTargets { get; internal set; } = [];
}
