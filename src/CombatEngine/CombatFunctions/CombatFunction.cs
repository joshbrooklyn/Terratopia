using CombatEngine.DataClasses;
using CombatEngine.Enums;
using CombatEngine.Engine;
using CombatEngine.Passives;


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
            foreach (var entity in ctx.Roster.ResolveBuffDebuffTargets(ctx.Actor, spec.Target, ctx.Targets))
            {
                if (!applied.Add((entity.EntityId, spec.Stat)))
                    throw new InvalidOperationException(
                        $"{ctx.Command.CombatFunction} ('{ctx.Command.SourceId}'): two buffsDebuffs entries both target {entity.Name}'s {spec.Stat}.");

                entity.AddBuffDebuff(spec.Stat, spec.Type == BuffDebuffType.Positive, spec.Rounds, spec.UntilRemoved,
                    ctx.Command.SourceId, ctx.Command.SourceName, ctx.Actor.EntityId, spec.CancelOnEntityDeath, spec.CancelOnApplierDeath);
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
            foreach (var entity in ctx.Roster.ResolveBuffDebuffTargets(ctx.Actor, spec.Target, ctx.Targets))
            {
                if (!applied.Add((entity.EntityId, spec.Stat)))
                    throw new InvalidOperationException(
                        $"{ctx.Command.CombatFunction} ('{ctx.Command.SourceId}'): two regensDrains entries both target {entity.Name}'s {spec.Stat}.");

                entity.AddRegenDrain(spec.Stat, spec.Type == RegenDrainType.Positive, spec.Rounds, spec.UntilRemoved,
                    ctx.Command.SourceId, ctx.Command.SourceName, ctx.Actor.EntityId, spec.CancelOnEntityDeath, spec.CancelOnApplierDeath);
            }
        }
    }

    // Resolves and grants every passivesApplied[] entry, the same way as ApplyBuffsDebuffs and
    // ApplyRegensDrains - shared by every function that can rider one onto its action, called once
    // after the function has fully resolved its own damage/healing, no-ops when none were
    // authored, and throws when two entries target the same (entity, passive) pair. Unlike the
    // other two riders, a grant has no duration: PassiveTracker.Add either creates the record or,
    // if the entity already owns the passive (or the name is unrecognised), no-ops - only a
    // genuine new grant raises CombatEventBus.PassiveApplied.
    protected static void ApplyPassives(CombatFunctionContext ctx)
    {
        var specs = ctx.Parameters.PassivesApplied;
        if (specs is not { Count: > 0 })
            return;

        var applied = new HashSet<(string EntityId, string Passive)>();

        foreach (var spec in specs)
        {
            foreach (var entity in ctx.Roster.ResolveBuffDebuffTargets(ctx.Actor, spec.Target, ctx.Targets))
            {
                if (!applied.Add((entity.EntityId, spec.Passive)))
                    throw new InvalidOperationException(
                        $"{ctx.Command.CombatFunction} ('{ctx.Command.SourceId}'): two passivesApplied entries both target {entity.Name} with {spec.Passive}.");

                if (PassiveTracker.Add(spec.Passive, entity.EntityId))
                    CombatEventBus.RaisePassiveApplied(entity.EntityId, entity.Name, spec.Passive, ctx.Command.SourceId, ctx.Command.SourceName);
            }
        }
    }

    protected static void CalculateAndApplyDamage(CombatFunctionContext ctx)
    {
        // Element doesn't feed the formula yet - it's what the UI reports and what elemental
        // resistances will key off. A null element means non-elemental (physical).
        double               basePowerFactor = ctx.Parameters.PowerFactor ?? CombatBalance.Current.DefaultPowerFactor;
        DamageOrHealCalcType calcType        = ctx.Parameters.CalcType    ?? DamageOrHealCalcType.StandardFormula;

        foreach (var target in ctx.Targets)
        {
            // An evaded hit lands nothing at all. Any buffsDebuffs entries are unaffected - they're
            // a property of the action, applied once after the loop regardless of evasion.
            if (ctx.TryEvade(target))
                continue;

            double keywordBonus = ctx.Keywords.ApplyKeywordBonuses(
                ctx.ActiveKeywords, basePowerFactor, ctx.Actor, target, ctx.ActorIsAlly, ctx.Command.SourceId, ctx.Command.SourceName);
            double effectivePowerFactor = basePowerFactor + keywordBonus;

            int  damage = CombatMath.CalculateDamageAmount(ctx.Actor, target, effectivePowerFactor, calcType);
            bool isCrit = ctx.RollCrit();
            if (isCrit)
                damage = ctx.ApplyCritModifier(damage);

            target.TakeDamage(ctx.Actor, damage, ctx.Command.SourceId, ctx.Command.SourceName, isCrit);
        }
    }

    protected static void CalculateAndApplyHealing(CombatFunctionContext ctx)
    {
        // Element doesn't feed the formula yet - it's what the UI reports and what elemental
        // resistances will key off. A null element means non-elemental (physical).
        double               basePowerFactor = ctx.Parameters.PowerFactor ?? CombatBalance.Current.DefaultPowerFactor;
        DamageOrHealCalcType calcType        = ctx.Parameters.CalcType    ?? DamageOrHealCalcType.StandardFormula;

        foreach (var target in ctx.Targets)
        {
            if (target.IsDead)
                continue;

            double keywordBonus = ctx.Keywords.ApplyKeywordBonuses(
                ctx.ActiveKeywords, basePowerFactor, ctx.Actor, target, ctx.ActorIsAlly, ctx.Command.SourceId, ctx.Command.SourceName);
            double effectivePowerFactor = basePowerFactor + keywordBonus;

            int amount = CombatMath.CalculateHealAmount(ctx.Actor, target, effectivePowerFactor, calcType);
            target.Heal(ctx.Actor, amount, ctx.Command.SourceId, ctx.Command.SourceName);
        }
    }
}
