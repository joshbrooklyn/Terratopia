using CombatEngine;
using CombatEngine.CombatFunctions;
using CombatEngine.DataClasses;
using CombatEngine.Engine;
using CombatEngine.Enums;
using CombatEngine.Passives;

namespace Terratopia.Tests.CombatEngine.PublicInterface;

[Collection("CombatEngineSerial")]
public class PassivesAppliedTests
{
    // Mirrors RegenDrainTests.SetupCombat: one ally, one durable enemy, the ally spends its
    // opening move (carrying the passivesApplied rider under test) then finishes the enemy off
    // with a FixedAmount blow so the fight terminates.
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

        // Wire AFTER InitCombat, which calls CombatEventBus.Reset() and PassiveTracker.Reset().
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
                PassivesApplied =
                [
                    new PassiveApplySpec { Passive = LivingDeadPassive.PassiveName, Target = target },
                ],
            },
        };

    [Fact]
    public void PassivesApplied_GrantsThePassive_StampingTheCurrentRound()
    {
        // What: verifies a passivesApplied rider on a NoDirectEffects action actually reaches
        //       PassiveTracker.Add - the entity ends up owning the passive, stamped with the
        //       round the grant happened in.
        var (engine, _, _) = SetupCombat(MakeOpeningMove());

        engine.BeginCombat();

        var activation = PassiveTracker.Get(LivingDeadPassive.PassiveName, "ally");
        Assert.Contains(PassiveRegistry.Resolve(LivingDeadPassive.PassiveName), PassiveTracker.GetPassives("ally"));
        Assert.Equal(1, activation.RoundApplied);
    }

    [Fact]
    public void PassiveApplied_FiresExactlyOncePerNewGrant()
    {
        // What: verifies CombatEventBus.PassiveApplied is raised for a genuine new grant, exactly
        //       once, naming the granted entity and the source that granted it.
        var (engine, _, _) = SetupCombat(MakeOpeningMove());

        var raised = new List<(string EntityId, string Passive, string SourceId)>();
        CombatEventBus.PassiveApplied += (entityId, _, passiveName, sourceId, _) =>
            raised.Add((entityId, passiveName, sourceId));

        engine.BeginCombat();

        Assert.Equal([("ally", LivingDeadPassive.PassiveName, "grant_tech")], raised);
    }

    [Fact]
    public void ReGranting_AnAlreadyOwnedPassive_IsANoOp_AndDoesNotRaisePassiveApplied()
    {
        // What: verifies granting a passive the entity already owns leaves RoundApplied/counts
        //       untouched (PassiveTracker.Add's existing no-op guard) and does not re-raise
        //       PassiveApplied - only a genuine new grant is reported.
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

        // Grant it up front, same as a monster's Monster.Passives would be at combat setup.
        PassiveTracker.Add(LivingDeadPassive.PassiveName, "ally");
        var before = PassiveTracker.Get(LivingDeadPassive.PassiveName, "ally");

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
        CombatEventBus.PassiveApplied += (_, _, _, _, _) => raised = true;

        engine.BeginCombat();

        var after = PassiveTracker.Get(LivingDeadPassive.PassiveName, "ally");
        Assert.Equal(before.RoundApplied, after.RoundApplied);
        Assert.Equal(before.TotalApplications, after.TotalApplications);
        Assert.False(raised);
    }

    [Fact]
    public void UnrecognisedPassiveName_IsSilentlyDropped()
    {
        // What: mirrors PassiveTracker.Add's own no-op guard - an authored passivesApplied entry
        //       naming an unregistered passive grants nothing and raises nothing, rather than
        //       throwing (the same tolerance PowerKeywordRegistry gives bad keyword names).
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
                PassivesApplied = [new PassiveApplySpec { Passive = "NotARealPassive", Target = BuffDebuffTarget.SelectedTargets }],
            },
        };

        var (engine, _, _) = SetupCombat(opening);

        bool raised = false;
        CombatEventBus.PassiveApplied += (_, _, _, _, _) => raised = true;

        engine.BeginCombat();

        Assert.Empty(PassiveTracker.GetPassives("ally"));
        Assert.False(raised);
    }

    [Fact]
    public void CollidingPassivesAppliedEntries_Throw_NamingTheAction()
    {
        // What: verifies two entries that resolve to the same (entity, passive) pair are a data
        //       error, caught even though the JSON Schema's uniqueBy can only reject identical
        //       (passive, target) pairs - mirrors CollidingRegenDrainEntries_Throw_NamingTheAction.
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
                PassivesApplied =
                [
                    new PassiveApplySpec { Passive = LivingDeadPassive.PassiveName, Target = BuffDebuffTarget.Self },
                    new PassiveApplySpec { Passive = LivingDeadPassive.PassiveName, Target = BuffDebuffTarget.AllAllies },
                ],
            },
        };

        var (engine, _, _) = SetupCombat(opening);

        var ex = Assert.Throws<InvalidOperationException>(() => engine.BeginCombat());
        Assert.Contains("broken_tech", ex.Message);
    }
}
