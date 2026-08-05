namespace CombatEngine.Enums;

// Who a buffsDebuffs[] entry lands on, resolved relative to the acting entity and independent of
// the action's own targets - hand-mirrored by the buffsDebuffs[].target enum in
// tech/item/monsteraction.schema.json. See CombatRoster.ResolveBuffDebuffTargets for how each
// value resolves. Widening it is a schema bump.
public enum BuffDebuffTarget
{
    // The action's own chosen targets, de-duplicated (a multi-hit action can repeat a target).
    SelectedTargets,

    // The acting entity itself.
    Self,

    // One living ally other than the actor, drawn from the engine's shared Rng. Empty (no-op) if
    // the actor has no other living ally.
    RandomAlly,

    // One living enemy of the actor, drawn from the engine's shared Rng. Empty (no-op) if the
    // actor has no living enemies.
    RandomEnemy,

    // Every living entity on the actor's side, actor included.
    AllAllies,

    // Every living entity on the opposing side.
    AllEnemies
}
