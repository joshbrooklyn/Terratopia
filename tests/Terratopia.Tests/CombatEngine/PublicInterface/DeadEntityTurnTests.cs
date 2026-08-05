using CombatEngine;
using CombatEngine.CombatFunctions;
using CombatEngine.DataClasses;
using CombatEngine.Engine;
using CombatEngine.Enums;

namespace Terratopia.Tests.CombatEngine.PublicInterface;

[Collection("CombatEngineSerial")]
public class DeadEntityTurnTests
{
    // A round's turn queue is a snapshot of who was alive when the round was built - an entity
    // killed by an earlier turn in that same round is still sitting in the queue for "its" turn
    // later on. CombatFlowMachine must skip that turn outright rather than letting a corpse act.
    [Fact]
    public void EntityKilledMidRound_NeverGetsItsOwnQueuedTurn()
    {
        // What: verifies a dead entity's turn is skipped - no TurnStarted/WaitingForTurn for it,
        //       and combat still resolves normally afterward.
        // How:  ally (speed 3200) acts before enemy1 (speed 1600) and enemy2 (speed 800), so
        //       round 1's queue is built as [ally, enemy1, enemy2]. ally one-shots enemy1
        //       (power 10, level 1, defense 0 -> baseDamage 25 > enemy1's 1 HP) on its own turn,
        //       so by the time the queue reaches "enemy1's turn" later in round 1, enemy1 is
        //       already dead. enemy1 and enemy2 both answer any turn they do get with a harmless
        //       self-targeted NoOp, so if the guard regressed and let dead enemy1 act, the test
        //       would still finish instead of hanging - the regression is caught by the
        //       TurnStarted/WaitingForTurn assertions below, not by a hang or a crash. Once
        //       enemy1 is confirmed dead, ally's round-2 attack is redirected at enemy2 (also
        //       1 HP), ending combat.
        var engine = new CombatEngineClass(new Random(0));

        var ally = new CombatEntity(
            entityId: "ally", name: "Ally", level: 1,
            maxHp: 1000, hp: 1000, maxTp: 0, tp: 0,
            power: 10, defense: 0, speed: 3200,
            evasion: 0f, critChance: 0f, critModifier: 0f);

        var enemy1 = new CombatEntity(
            entityId: "enemy1", name: "Enemy1", level: 1,
            maxHp: 1, hp: 1, maxTp: 0, tp: 0,
            power: 0, defense: 0, speed: 1600,
            evasion: 0f, critChance: 0f, critModifier: 0f);

        var enemy2 = new CombatEntity(
            entityId: "enemy2", name: "Enemy2", level: 1,
            maxHp: 1, hp: 1, maxTp: 0, tp: 0,
            power: 0, defense: 0, speed: 800,
            evasion: 0f, critChance: 0f, critModifier: 0f);

        engine.InitCombat(allies: [ally], enemies: [enemy1, enemy2]);

        var turnStartedIds    = new List<string>();
        var waitingForTurnIds = new List<string>();
        bool? playerWon = null;

        CombatEventBus.TurnStarted    += (entityId, _) => turnStartedIds.Add(entityId);
        CombatEventBus.WaitingForTurn += (entityId, _, _, _) => waitingForTurnIds.Add(entityId);
        CombatEventBus.CombatOver     += won => playerWon = won;

        CombatEventBus.TargetSelectionRequested += (actorId, _, _, _, _, _, _) =>
        {
            if (actorId != ally.EntityId)
                return;

            string targetId = enemy1.IsDead ? enemy2.EntityId : enemy1.EntityId;
            engine.SubmitTargets([targetId]);
        };

        CombatEventBus.WaitingForTurn += (entityId, _, _, isAlly) =>
        {
            if (isAlly)
            {
                engine.SubmitCommand(new CombatCommand
                {
                    ActorId       = entityId,
                    TargetingType = TargetingType.Choose,
                    ValidTargets  = ValidTarget.Enemies,
                    LivingOrDead  = LivingOrDead.Living,
                    CombatFunction = BasicDamageFunction.FunctionName,
                    Parameters = new CombatFunctionParameters { CalcType = DamageOrHealCalcType.StandardFormula, PowerFactor = 1.0 },
                });
                return;
            }

            // enemy1/enemy2's own turn - harmless regardless of which one it is, so a
            // regression that lets dead enemy1 act doesn't hang or crash the test.
            engine.SubmitCommand(new CombatCommand
            {
                ActorId       = entityId,
                TargetingType = TargetingType.Self,
                ValidTargets  = ValidTarget.Allies,
                LivingOrDead  = LivingOrDead.Both,
                CombatFunction = NoOpFunction.FunctionName,
            });
        };

        engine.BeginCombat();

        Assert.True(enemy1.IsDead);
        Assert.True(enemy2.IsDead);
        Assert.True(playerWon);

        Assert.DoesNotContain("enemy1", turnStartedIds);
        Assert.DoesNotContain("enemy1", waitingForTurnIds);
    }
}
