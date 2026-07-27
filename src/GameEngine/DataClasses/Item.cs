using CombatEngine.Enums;

namespace GameEngine.DataClasses;

public class Item : IGameDataObject
{
    public string Id => ItemId;
    public string ItemId { get; init; } = "";
    public string Name { get; init; } = "";
    public int Rarity { get; init; }
    public string Description { get; init; } = "";
    public float PowerModifier { get; init; }
    public int NumAttacks { get; init; } = 1;
    public List<string> Traits { get; init; } = [];
    public TargetingType TargetingType { get; init; } = TargetingType.Choose;
    public required ValidTarget ValidTargets { get; init; }
    public required LivingOrDead LivingOrDead { get; init; }
    public ElementType? Element { get; init; }
    public CombatDirectEffectType? EffectType { get; init; }
    public List<string> TargetStatuses { get; init; } = [];
    public List<string> UserStatuses { get; init; } = [];

/*
    public EntityAction ToEntityAction(string actorId) => new()
    {
        ActionId           = ItemId,
        Actor              = actorId,
        TargetingType      = Enum.Parse<BattleEngine.TargetingType>(TargetingType),
        NumAttacks         = NumAttacks,
        PowerModifier      = PowerModifier,
        ResourceCost       = 0,
        Flags              = BuildFlags(Traits),
        DirectEffects      = BuildDirectEffects(),
        StatusApplications = BuildStatusApplications(),
        ActionTags         = ["Item"],
    };

    private List<CombatEffectEvent> BuildDirectEffects()
    {
        if (EffectType == null) return [];
        var type    = Enum.Parse<BattleEngine.EffectType>(EffectType);
        var element = Element != null ? Enum.Parse<ElementType>(Element) : (ElementType?)null;
        return [new CombatEffectEvent { Type = type, Element = element }];
    }

    private List<Status> BuildStatusApplications() =>
        TargetStatuses.Select(id => new Status { StatusId = id }).ToList();

    private static HashSet<ActionTrait> BuildFlags(List<string> traits)
    {
        var flags = new HashSet<ActionTrait>();
        foreach (var trait in traits)
        {
            if (Enum.TryParse<ActionTrait>(trait, out var flag))
                flags.Add(flag);
        }
        return flags;
    }
    */
}
