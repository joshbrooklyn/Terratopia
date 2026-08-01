using CombatEngine;
using CombatEngine.CombatFunctions;
using CombatEngine.DataClasses;
using CombatEngine.Engine;
using CombatEngine.Enums;
using CombatEngine.Keywords;

namespace Terratopia.Tests.CombatEngine;

[Collection("CombatEngineSerial")]
public class GrowthTests
{
    // A single ally repeatedly uses the same Growth-keyword action (ActionId "tech1") against a
    // single, harmless (power=0) enemy across multiple rounds, so the same actor+action pair
    // accumulates a Growth stack each round. The enemy's own attacks deal 0 damage (power=0,
    // defense=0, level=1 still contributes a small flat term) so it never threatens the ally's
    // large HP pool, letting combat run for exactly as many rounds as the test needs.
    private static (CombatEngineClass engine, CombatEntity ally, CombatEntity enemy) SetupGrowthDuel(
        int enemyHp, double powerFactor)
    {
        var engine = new CombatEngineClass(new Random(0));

        var ally = new CombatEntity(
            entityId: "ally", name: "Ally", level: 1,
            maxHp: 1000, hp: 1000, maxTp: 0, tp: 0,
            power: 10, defense: 0, speed: 100,
            evasion: 0f, critChance: 0f, critModifier: 0f);

        var enemy = new CombatEntity(
            entityId: "enemy", name: "Enemy", level: 1,
            maxHp: enemyHp, hp: enemyHp, maxTp: 0, tp: 0,
            power: 0, defense: 0, speed: 1,
            evasion: 0f, critChance: 0f, critModifier: 0f);

        engine.InitCombat(allies: [ally], enemies: [enemy]);

        CombatEventBus.WaitingForTurn += (entityId, _, _, isAlly) =>
        {
            if (isAlly)
            {
                engine.SubmitCommand(new CombatCommand
                {
                    ActorId       = entityId,
                    TargetingType = TargetingType.Random,
                    ValidTargets  = ValidTarget.Enemies,
                    LivingOrDead  = LivingOrDead.Living,
                    TPCost        = 0,
                    ActionId      = "tech1",
                    Keywords      = [GrowthKeyword.KeywordName],
                    CombatFunction = BasicDamageFunction.FunctionName,
                    Parameters = new CombatFunctionParameters { CalcType = DamageCalcType.StandardFormula, PowerFactor = powerFactor },
                });
            }
            else
            {
                engine.SubmitCommand(new CombatCommand
                {
                    ActorId       = entityId,
                    TargetingType = TargetingType.Random,
                    ValidTargets  = ValidTarget.Enemies,
                    LivingOrDead  = LivingOrDead.Living,
                    TPCost        = 0,
                    CombatFunction = BasicDamageFunction.FunctionName,
                    Parameters = new CombatFunctionParameters { CalcType = DamageCalcType.StandardFormula, PowerFactor = 1.0 },
                });
            }
        };

        return (engine, ally, enemy);
    }

    [Fact]
    public void RepeatedUsesOfSameAction_StackGrowthByTenPercentPerPriorUse()
    {
        // What: verifies Growth's bonus actually flows through the full damage pipeline (not
        //       just the keyword's own GetBonus in isolation) - repeated uses of the same
        //       action across combat rounds should measurably increase the damage each hit
        //       deals, growing by 10% of the base PowerFactor per prior use.
        // How:  ally power=10, level=1, defense=0, base PowerFactor=1.0.
        //       Use 1: growth bonus = 0 -> actionPower=10 -> baseDamage=10*2+5=25 -> damage 25.
        //       Use 2: growth bonus = 0.10*(2-1)=0.10 -> effectivePowerFactor=1.10 -> actionPower=11 ->
        //              baseDamage=22+5=27 -> damage 27.
        //       Use 3: growth bonus = 0.10*(3-1)=0.20 -> effectivePowerFactor=1.20 -> actionPower=12 ->
        //              baseDamage=24+5=29 -> damage 29.
        //       enemyHp = 25+27+29 = 81 so combat ends exactly after the ally's third hit. The
        //       test captures every EntityDamaged amount dealt to "enemy" and asserts the
        //       sequence is exactly [25, 27, 29].
        var (engine, _, enemy) = SetupGrowthDuel(enemyHp: 81, powerFactor: 1.0);

        var damages = new List<int>();
        CombatEventBus.EntityDamaged += (targetId, _, dmg, _, _, _) =>
        {
            if (targetId == "enemy") damages.Add(dmg);
        };

        engine.BeginCombat();

        Assert.Equal([25, 27, 29], damages);
        Assert.True(enemy.IsDead);
    }

    [Fact]
    public void Bonus_CapsAtBasePowerFactorWhenGrowthStacksExceedIt()
    {
        // What: verifies the shared power-keyword cap (min(basePowerFactor*2, basePowerFactor+0.5))
        //       actually clips Growth's bonus once enough stacks accumulate, rather than letting
        //       the 10%-per-use growth compound without bound - a low base PowerFactor should
        //       plateau in damage instead of climbing every single use.
        // How:  Low base PowerFactor=0.1 caps each use's Growth bonus at min(0.1*2, 0.1+0.5) = 0.2.
        //       Use 1: growth bonus = 0, capped bonus 0 -> actionPower=10*0.1=1 -> baseDamage=2+5=7.
        //       Use 2: raw growth bonus = 0.10*(2-1)=0.10, cap=0.2 -> applied 0.10 (not over the cap) ->
        //              effectivePowerFactor=0.2 -> actionPower=2 -> baseDamage=4+5=9.
        //       Use 3: raw growth bonus = 0.10*(3-1)=0.20, cap=0.2 -> applied 0.20 (exactly at the cap,
        //              not clipped) -> effectivePowerFactor=0.3 -> actionPower=3 -> baseDamage=6+5=11.
        //       Use 4: raw growth bonus = 0.10*(4-1)=0.30, cap=0.2 -> applied capped at 0.20 (NOT 0.30)
        //              -> effectivePowerFactor stays 0.3 -> actionPower=3 -> baseDamage=11 again.
        //       If the cap were missing, the fourth hit would instead be baseDamage=(10*0.4)*2+5=13.
        //       enemyHp is set to the exact running total (7+9+11+11=38) so combat ends right
        //       after the fourth hit, and the test asserts the damage sequence is [7, 9, 11, 11].
        var (engine, _, enemy) = SetupGrowthDuel(enemyHp: 7 + 9 + 11 + 11, powerFactor: 0.1);

        var damages = new List<int>();
        CombatEventBus.EntityDamaged += (targetId, _, dmg, _, _, _) =>
        {
            if (targetId == "enemy") damages.Add(dmg);
        };

        engine.BeginCombat();

        Assert.Equal([7, 9, 11, 11], damages);
        Assert.True(enemy.IsDead);
    }
}
