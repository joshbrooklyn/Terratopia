using CombatEngine.Enums;

namespace CombatEngine.DataClasses;

public class CombatCommand
{
    public string ActorId { get; init; } = string.Empty;
    public TargetingType TargetingType { get; init; }
    public required ValidTarget ValidTargets { get; init; }
    public required LivingOrDead LivingOrDead { get; init; }
    public int TPCost { get; init; }
    public List<CombatDirectEffect> DirectEffects { get; init; } = [];
    public List<string> ChosenTargets { get; internal set; } = [];
}
