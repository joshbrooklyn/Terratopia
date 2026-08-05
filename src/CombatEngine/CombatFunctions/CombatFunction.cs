using CombatEngine.DataClasses;
using CombatEngine.Enums;

namespace CombatEngine.CombatFunctions;

// One CombatFunction owns the ENTIRE resolution of one action: the target loop, evasion, crit,
// damage/healing, bespoke impacts, and every event raised along the way. The engine hands it a
// context whose delegates are the *standard* implementations of each step - a function may call
// them, replace them, or skip them entirely.
//
// The registry hands out one shared instance per name, so implementations MUST be stateless.
//
// Contract:
//   1. Call ctx.DeductTpCost() (or bespoke TP logic) before touching a target. It no-ops on a
//      0-TP command, so there is no reason to guard it.
//   2. Validate your own parameters and throw InvalidOperationException naming
//      ctx.Command.SourceId. The "parameters" schema block marks every field optional, so the
//      function is the ONLY place requirements are enforced.
public abstract class CombatFunction
{
    public abstract string Name { get; }

    public abstract void Execute(CombatFunctionContext ctx);

    // Resolves and applies every buffsDebuffs[] entry, shared by every function that can rider one
    // onto its action. Called once, after the function has fully resolved its own damage/healing -
    // entries are a property of the action, not of any individual hit, so they land regardless of
    // evasion and never influence the action's own numbers. No-ops when none were authored. Throws
    // when two entries land on the same (entity, stat) pair, since the schema can only enforce
    // uniqueness per authored (stat, target) and two different targets can still resolve to the
    // same entity (e.g. Self and AllAllies both including the actor).
    protected static void ApplyBuffsDebuffs(CombatFunctionContext ctx)
    {
        var specs = ctx.Parameters.BuffsDebuffs;
        if (specs is not { Count: > 0 })
            return;

        var applied = new HashSet<(string EntityId, BuffDebuffStat Stat)>();

        foreach (var spec in specs)
        {
            foreach (var entity in ctx.ResolveBuffDebuffTargets(spec.Target))
            {
                if (!applied.Add((entity.EntityId, spec.Stat)))
                    throw new InvalidOperationException(
                        $"{ctx.Command.CombatFunction} ('{ctx.Command.SourceId}'): two buffsDebuffs entries both target {entity.Name}'s {spec.Stat}.");

                ctx.ApplyBuffDebuff(entity, spec.Stat, spec.Type == BuffDebuffType.Positive, spec.Rounds, spec.UntilRemoved);
            }
        }
    }

    // Resolves and applies every regensDrains[] entry, the same way as ApplyBuffsDebuffs - shared
    // by every function that can rider one onto its action, called once after the function has
    // fully resolved its own damage/healing, no-ops when none were authored, and throws when two
    // entries land on the same (entity, stat) pair.
    protected static void ApplyRegensDrains(CombatFunctionContext ctx)
    {
        var specs = ctx.Parameters.RegensDrains;
        if (specs is not { Count: > 0 })
            return;

        var applied = new HashSet<(string EntityId, RegenDrainStat Stat)>();

        foreach (var spec in specs)
        {
            foreach (var entity in ctx.ResolveBuffDebuffTargets(spec.Target))
            {
                if (!applied.Add((entity.EntityId, spec.Stat)))
                    throw new InvalidOperationException(
                        $"{ctx.Command.CombatFunction} ('{ctx.Command.SourceId}'): two regensDrains entries both target {entity.Name}'s {spec.Stat}.");

                ctx.ApplyRegenDrain(entity, spec.Stat, spec.Type == RegenDrainType.Positive, spec.Rounds, spec.UntilRemoved);
            }
        }
    }
}
