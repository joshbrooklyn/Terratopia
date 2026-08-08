using CombatEngine;
using CombatEngine.CombatFunctions;
using CombatEngine.DataClasses;
using CombatEngine.Engine;
using CombatEngine.Enums;
using CombatEngine.TriggeredEffects;

namespace Terratopia.Tests.CombatEngine.PublicInterface;

[Collection("CombatEngineSerial")]
public class TriggeredEffectsAppliedTests
{
    // Mirrors RegenDrainTests.SetupCombat: one ally, one durable enemy, the ally spends its
    // opening move (carrying the triggeredEffectsApplied rider under test) then finishes the enemy
    // off with a FixedAmount blow so the fight terminates.
    private static (CombatEngineClass engine, CombatEntity ally, CombatEntity enemy) SetupCombat(
        CombatCommand openingMove)
    {
        var engine = new CombatEngineClass(new Random(0));

        var ally = new CombatEntity(
            entityId: "ally", name: "Ally", level: 1,
            maxHp: 100, hp: 100, maxTp: 50, tp: 50,
            power: 20, defense: 0, speed: 10,
            evasion: 0.0f, critChance: 0.0f, critModifier: 0.0f);

        var enemy = new CombatEntity(
            entityId: "enemy", name: "Enemy", level: 1,
            maxHp: 1000, hp: 1000, maxTp: 0, tp: 0,
            power: 0, defense: 20, speed: 5,
            evasion: 0.0f, critChance: 0.0f, critModifier: 0.0f);

        engine.InitCombat(allies: [ally], enemies: [enemy]);

        // Wire AFTER InitCombat, which calls CombatEventBus.Reset() and TriggeredEffectTracker.Reset().
        bool openingMoveUsed = false;
        CombatEventBus.WaitingForTurn += (entityId, _, _, isAlly) =>
        {
            if (!isAlly)
            {
                engine.SubmitCommand(new CombatCommand
                {
                    ActorId        = entityId,
                    TargetingType  = TargetingType.Self,
                    ValidTargets   = ValidTarget.Allies,
                    LivingOrDead   = LivingOrDead.Living,
                    CombatFunction = NoOpFunction.FunctionName,
                });
                return;
            }

            if (!openingMoveUsed)
            {
                openingMoveUsed = true;
                engine.SubmitCommand(openingMove);
                return;
            }

            engine.SubmitCommand(new CombatCommand
            {
                ActorId        = entityId,
                TargetingType  = TargetingType.Random,
                ValidTargets   = ValidTarget.Enemies,
                LivingOrDead   = LivingOrDead.Living,
                CombatFunction = BasicDamageFunction.FunctionName,
                Parameters     = new CombatFunctionParameters { CalcType = DamageOrHealCalcType.FixedAmount, PowerFactor = 100_000 },
            });
        };

        return (engine, ally, enemy);
    }

    private static CombatCommand MakeOpeningMove(BuffDebuffTarget target = BuffDebuffTarget.SelectedTargets, string sourceId = "grant_tech") =>
        new()
        {
            ActorId        = "ally",
            SourceId       = sourceId,
            TargetingType  = TargetingType.Self,
            ValidTargets   = ValidTarget.Allies,
            LivingOrDead   = LivingOrDead.Living,
            CombatFunction = NoDirectEffectsFunction.FunctionName,
            Parameters     = new CombatFunctionParameters
            {
                TriggeredEffectsApplied =
                [
                    new TriggeredEffectApplySpec { TriggeredEffect = LivingDeadTriggeredEffect.TriggeredEffectName, Target = target },
                ],
            },
        };

    [Fact]
    public void TriggeredEffectsApplied_GrantsTheTriggeredEffect_StampingTheCurrentRound()
    {
        // What: verifies a triggeredEffectsApplied rider on a NoDirectEffects action actually
        //       reaches TriggeredEffectTracker.Add - the entity ends up owning the triggered
        //       effect, stamped with the round the grant happened in.
        var (engine, _, _) = SetupCombat(MakeOpeningMove());

        engine.BeginCombat();

        var activation = TriggeredEffectTracker.Get(LivingDeadTriggeredEffect.TriggeredEffectName, "ally");
        Assert.Contains(TriggeredEffectRegistry.Resolve(LivingDeadTriggeredEffect.TriggeredEffectName), TriggeredEffectTracker.GetTriggeredEffects("ally"));
        Assert.Equal(1, activation.RoundApplied);
    }

    [Fact]
    public void TriggeredEffectApplied_FiresExactlyOncePerNewGrant()
    {
        // What: verifies CombatEventBus.TriggeredEffectApplied is raised for a genuine new grant,
        //       exactly once, naming the granted entity and the source that granted it.
        var (engine, _, _) = SetupCombat(MakeOpeningMove());

        var raised = new List<(string EntityId, string TriggeredEffect, string SourceId)>();
        CombatEventBus.TriggeredEffectApplied += (entityId, _, triggeredEffectName, sourceId, _) =>
            raised.Add((entityId, triggeredEffectName, sourceId));

        engine.BeginCombat();

        Assert.Equal([("ally", LivingDeadTriggeredEffect.TriggeredEffectName, "grant_tech")], raised);
    }

    [Fact]
    public void ReGranting_AnAlreadyOwnedTriggeredEffect_IsANoOp_AndDoesNotRaiseTriggeredEffectApplied()
    {
        // What: verifies granting a triggered effect the entity already owns leaves
        //       RoundApplied/counts untouched (TriggeredEffectTracker.Add's existing no-op guard)
        //       and does not re-raise TriggeredEffectApplied - only a genuine new grant is
        //       reported.
        var ally = new CombatEntity(
            entityId: "ally", name: "Ally", level: 1,
            maxHp: 100, hp: 100, maxTp: 50, tp: 50,
            power: 20, defense: 0, speed: 10,
            evasion: 0.0f, critChance: 0.0f, critModifier: 0.0f);
        var enemy = new CombatEntity(
            entityId: "enemy", name: "Enemy", level: 1,
            maxHp: 1000, hp: 1000, maxTp: 0, tp: 0,
            power: 0, defense: 20, speed: 5,
            evasion: 0.0f, critChance: 0.0f, critModifier: 0.0f);

        var engine = new CombatEngineClass(new Random(0));
        engine.InitCombat(allies: [ally], enemies: [enemy]);

        // Grant it up front, same as a monster's Monster.TriggeredEffects would be at combat setup.
        TriggeredEffectTracker.Add(LivingDeadTriggeredEffect.TriggeredEffectName, "ally");
        var before = TriggeredEffectTracker.Get(LivingDeadTriggeredEffect.TriggeredEffectName, "ally");

        bool openingMoveUsed = false;
        CombatEventBus.WaitingForTurn += (entityId, _, _, isAlly) =>
        {
            if (!isAlly)
            {
                engine.SubmitCommand(new CombatCommand
                {
                    ActorId = entityId, TargetingType = TargetingType.Self, ValidTargets = ValidTarget.Allies,
                    LivingOrDead = LivingOrDead.Living, CombatFunction = NoOpFunction.FunctionName,
                });
                return;
            }

            if (!openingMoveUsed)
            {
                openingMoveUsed = true;
                engine.SubmitCommand(MakeOpeningMove());
                return;
            }

            engine.SubmitCommand(new CombatCommand
            {
                ActorId = entityId, TargetingType = TargetingType.Random, ValidTargets = ValidTarget.Enemies,
                LivingOrDead = LivingOrDead.Living, CombatFunction = BasicDamageFunction.FunctionName,
                Parameters = new CombatFunctionParameters { CalcType = DamageOrHealCalcType.FixedAmount, PowerFactor = 100_000 },
            });
        };

        bool raised = false;
        CombatEventBus.TriggeredEffectApplied += (_, _, _, _, _) => raised = true;

        engine.BeginCombat();

        var after = TriggeredEffectTracker.Get(LivingDeadTriggeredEffect.TriggeredEffectName, "ally");
        Assert.Equal(before.RoundApplied, after.RoundApplied);
        Assert.Equal(before.TotalApplications, after.TotalApplications);
        Assert.False(raised);
    }

    [Fact]
    public void UnrecognisedTriggeredEffectName_IsSilentlyDropped()
    {
        // What: mirrors TriggeredEffectTracker.Add's own no-op guard - an authored
        //       triggeredEffectsApplied entry naming an unregistered triggered effect grants
        //       nothing and raises nothing, rather than throwing (the same tolerance
        //       PowerKeywordRegistry gives bad keyword names).
        var opening = new CombatCommand
        {
            ActorId        = "ally",
            SourceId       = "grant_tech",
            TargetingType  = TargetingType.Self,
            ValidTargets   = ValidTarget.Allies,
            LivingOrDead   = LivingOrDead.Living,
            CombatFunction = NoDirectEffectsFunction.FunctionName,
            Parameters     = new CombatFunctionParameters
            {
                TriggeredEffectsApplied = [new TriggeredEffectApplySpec { TriggeredEffect = "NotARealTriggeredEffect", Target = BuffDebuffTarget.SelectedTargets }],
            },
        };

        var (engine, _, _) = SetupCombat(opening);

        bool raised = false;
        CombatEventBus.TriggeredEffectApplied += (_, _, _, _, _) => raised = true;

        engine.BeginCombat();

        Assert.Empty(TriggeredEffectTracker.GetTriggeredEffects("ally"));
        Assert.False(raised);
    }

    [Fact]
    public void CollidingTriggeredEffectsAppliedEntries_Throw_NamingTheAction()
    {
        // What: verifies two entries that resolve to the same (entity, triggeredEffect) pair are a
        //       data error, caught even though the JSON Schema's uniqueBy can only reject identical
        //       (triggeredEffect, target) pairs - mirrors CollidingRegenDrainEntries_Throw_NamingTheAction.
        var opening = new CombatCommand
        {
            ActorId        = "ally",
            SourceId       = "broken_tech",
            TargetingType  = TargetingType.Self,
            ValidTargets   = ValidTarget.Allies,
            LivingOrDead   = LivingOrDead.Living,
            CombatFunction = NoDirectEffectsFunction.FunctionName,
            Parameters     = new CombatFunctionParameters
            {
                TriggeredEffectsApplied =
                [
                    new TriggeredEffectApplySpec { TriggeredEffect = LivingDeadTriggeredEffect.TriggeredEffectName, Target = BuffDebuffTarget.Self },
                    new TriggeredEffectApplySpec { TriggeredEffect = LivingDeadTriggeredEffect.TriggeredEffectName, Target = BuffDebuffTarget.AllAllies },
                ],
            },
        };

        var (engine, _, _) = SetupCombat(opening);

        var ex = Assert.Throws<InvalidOperationException>(() => engine.BeginCombat());
        Assert.Contains("broken_tech", ex.Message);
    }
}
