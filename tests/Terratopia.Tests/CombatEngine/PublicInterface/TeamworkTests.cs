using CombatEngine;
using CombatEngine.CombatFunctions;
using CombatEngine.DataClasses;
using CombatEngine.Engine;
using CombatEngine.Enums;
using CombatEngine.Keywords;

namespace Terratopia.Tests.CombatEngine.PublicInterface;

[Collection("CombatEngineSerial")]
public class TeamworkTests
{
    // Builds N allies (power=10, defense=0, halving speeds so they act in list order) against
    // a single enemy (defense=0, power=0 so its own hits are irrelevant/never reached in these
    // tests since combat ends once the enemy dies). Each ally's command is wired to fire in
    // the same order as allyConfigs the moment it's that entity's turn.
    //
    // TurnOrderManager scores speed with up to +/-25% random jitter, so speeds must differ by
    // more than a ~1.67x ratio between consecutive allies to guarantee a stable turn order
    // (halving each step, as used here, comfortably clears that).
    private static (CombatEngineClass engine, CombatEntity enemy) SetupAlliesVsEnemy(
        IReadOnlyList<(string id, bool teamwork, double powerFactor)> allyConfigs,
        int enemyHp)
    {
        var engine = new CombatEngineClass(new Random(0));

        var allies = allyConfigs
            .Select((c, i) => new CombatEntity(
                entityId: c.id, name: c.id, level: 1,
                maxHp: 1000, hp: 1000, maxTp: 0, tp: 0,
                power: 10, defense: 0, speed: 3200 >> i,
                evasion: 0f, critChance: 0f, critModifier: 0f))
            .ToList();

        var enemy = new CombatEntity(
            entityId: "enemy", name: "Enemy", level: 1,
            maxHp: enemyHp, hp: enemyHp, maxTp: 0, tp: 0,
            power: 0, defense: 0, speed: 0,
            evasion: 0f, critChance: 0f, critModifier: 0f);

        engine.InitCombat(allies: allies, enemies: [enemy]);

        var configById = allyConfigs.ToDictionary(c => c.id);

        // Wire AFTER InitCombat (which calls CombatEventBus.Reset()).
        CombatEventBus.WaitingForTurn += (entityId, _, _, isAlly) =>
        {
            if (!isAlly)
            {
                // Never actually reached in these tests: the enemy always dies to an ally's
                // hit before its own (lowest-speed) turn arrives.
                engine.SubmitCommand(new CombatCommand
                {
                    ActorId       = entityId,
                    TargetingType = TargetingType.Random,
                    ValidTargets  = ValidTarget.Enemies,
                    LivingOrDead  = LivingOrDead.Living,
                    TPCost        = 0,
                    CombatFunction = BasicDamageFunction.FunctionName,
                    Parameters = new CombatFunctionParameters { CalcType = DamageOrHealCalcType.StandardFormula, PowerFactor = 1.0 },
                });
                return;
            }

            var cfg = configById[entityId];
            engine.SubmitCommand(new CombatCommand
            {
                ActorId       = entityId,
                TargetingType = TargetingType.Random,
                ValidTargets  = ValidTarget.Enemies,
                LivingOrDead  = LivingOrDead.Living,
                TPCost        = 0,
                Keywords      = cfg.teamwork ? [TeamworkKeyword.KeywordName] : [],
                CombatFunction = BasicDamageFunction.FunctionName,
                Parameters = new CombatFunctionParameters { CalcType = DamageOrHealCalcType.StandardFormula, PowerFactor = cfg.powerFactor },
            });
        };

        return (engine, enemy);
    }

    [Fact]
    public void FirstTeamworkUse_GrantsFivePercentBonus()
    {
        // What: verifies the very first Teamwork use in a fight already gets +5% power, per
        //       "the first time it is used by any ally it provides a 5% power bonus."
        // How:  A single ally (power=10, level=1, defense=0) uses a Teamwork-keyword attack
        //       (PowerFactor=1.0). Without Teamwork this would deal the DamageTests baseline of
        //       25. Here the counter goes 0→1, bonus = min(0.05*1, min(1.0,0.5)) = 0.05, so
        //       effectivePowerFactor = 1.05 → actionPower = 10*1.05 = 10.5 →
        //       baseDamage = 10.5*2 + 1*5 = 26 → damage = 26 (defense 0). enemyHp is set to
        //       exactly 26 so this single hit ends the fight.
        var (engine, enemy) = SetupAlliesVsEnemy(
            [("ally1", teamwork: true, powerFactor: 1.0)], enemyHp: 26);

        int? damageDealt = null;
        CombatEventBus.EntityDamaged += (targetId, _, dmg, _, _, _, _, _, _, _) =>
        {
            if (targetId == "enemy") damageDealt ??= dmg;
        };

        engine.BeginCombat();

        Assert.NotNull(damageDealt);
        Assert.Equal(26, damageDealt.Value);
        Assert.True(enemy.IsDead);
    }

    [Fact]
    public void SecondTeamworkUse_StacksToTenPercentBonus()
    {
        // What: verifies a second Teamwork use (by a different ally) stacks the bonus to +10%.
        // How:  Two allies both use Teamwork-keyword attacks (PowerFactor=1.0). ally1 (higher
        //       speed) acts first: counter 0→1, bonus 5%, damage 26 (same math as the first
        //       test). ally2 acts second: counter 1→2, bonus = min(0.10, 0.5) = 0.10,
        //       effectivePowerFactor = 1.10 → actionPower = 11 → baseDamage = 22+5 = 27 →
        //       damage 27. enemyHp = 26+27 = 53 so the enemy survives ally1's hit (down to 27
        //       HP) and dies exactly on ally2's hit, ending the fight before the enemy ever acts.
        var (engine, enemy) = SetupAlliesVsEnemy(
            [
                ("ally1", teamwork: true, powerFactor: 1.0),
                ("ally2", teamwork: true, powerFactor: 1.0),
            ],
            enemyHp: 53);

        var damages = new List<int>();
        CombatEventBus.EntityDamaged += (targetId, _, dmg, _, _, _, _, _, _, _) =>
        {
            if (targetId == "enemy") damages.Add(dmg);
        };

        engine.BeginCombat();

        Assert.Equal([26, 27], damages);
        Assert.True(enemy.IsDead);
    }

    [Fact]
    public void Bonus_CapsAtDoublingTheBaseWhenStacksExceedIt()
    {
        // What: verifies the cap rule — a low base PowerFactor caps its Teamwork bonus at
        //       min(basePowerFactor*2, basePowerFactor+0.5) rather than letting 5%-per-stack
        //       grow unbounded.
        // How:  Four "pump" allies with PowerFactor=1.0 and the Teamwork keyword drive the ally
        //       -side counter from 0 to 4 (dealing 26/27/28/29 damage respectively, by the same
        //       stacking math as the tests above). A fifth ally then uses a Teamwork attack with
        //       a low base PowerFactor of 0.1: counter goes 4→5, raw bonus = 0.05*5 = 0.25, but
        //       cap = min(0.1*2, 0.1+0.5) = 0.2, so the applied bonus is capped at 0.2 (tripling
        //       the base to 0.3, NOT the uncapped 0.35). actionPower = 10*0.3 = 3 →
        //       baseDamage = 3*2 + 1*5 = 11 → damage 11. If the cap were missing, damage would
        //       instead be baseDamage = (10*0.35)*2+5 = 12. enemyHp is set to the exact running
        //       total (26+27+28+29+11 = 121) so the fight ends right after the capped hit.
        var (engine, enemy) = SetupAlliesVsEnemy(
            [
                ("pump1", teamwork: true, powerFactor: 1.0),
                ("pump2", teamwork: true, powerFactor: 1.0),
                ("pump3", teamwork: true, powerFactor: 1.0),
                ("pump4", teamwork: true, powerFactor: 1.0),
                ("capped", teamwork: true, powerFactor: 0.1),
            ],
            enemyHp: 26 + 27 + 28 + 29 + 11);

        var damages = new List<int>();
        CombatEventBus.EntityDamaged += (targetId, _, dmg, _, _, _, _, _, _, _) =>
        {
            if (targetId == "enemy") damages.Add(dmg);
        };

        engine.BeginCombat();

        Assert.Equal([26, 27, 28, 29, 11], damages);
        Assert.True(enemy.IsDead);
    }

    [Fact]
    public void ZeroPowerFactor_GetsNoBonus_ButStillIncrementsTheCounter()
    {
        // What: verifies "no effect if the base power modifier is 0%" for the zero-factor
        //       action itself, while confirming that action still counts as a Teamwork "use"
        //       that benefits later allies.
        // How:  ally1 uses a Teamwork-keyword command with PowerFactor=0: the counter still
        //       goes 0→1 (OnUsed runs regardless of PowerFactor), but the cap for this effect is
        //       min(0, 0.5) = 0, so no bonus is added — actionPower stays 0, and damage is
        //       purely the level term: baseDamage = 0*2 + 1*5 = 5 → damage 5. ally2 then uses a
        //       normal Teamwork attack (PowerFactor=1.0): counter 1→2, bonus 10%, damage 27 —
        //       identical to the second-use case in SecondTeamworkUse_StacksToTenPercentBonus,
        //       proving ally1's zero-factor use still counted towards the shared counter.
        var (engine, enemy) = SetupAlliesVsEnemy(
            [
                ("ally1", teamwork: true, powerFactor: 0.0),
                ("ally2", teamwork: true, powerFactor: 1.0),
            ],
            enemyHp: 5 + 27);

        var damages = new List<int>();
        CombatEventBus.EntityDamaged += (targetId, _, dmg, _, _, _, _, _, _, _) =>
        {
            if (targetId == "enemy") damages.Add(dmg);
        };

        engine.BeginCombat();

        Assert.Equal([5, 27], damages);
        Assert.True(enemy.IsDead);
    }

    [Fact]
    public void AllyAndEnemySideCounters_AreIndependent()
    {
        // What: verifies the ally-side and enemy-side Teamwork counters are tracked
        //       independently — an ally's bonus should reflect only how many times its own
        //       side has used Teamwork, not the opposing side's count.
        // How:  Two enemies (higher speed, so they act first) each use a Teamwork attack
        //       (PowerFactor irrelevant to the ally's outcome) against the sole ally, pumping
        //       the enemy-side counter to 2. Neither hit is lethal (ally has 1000 HP and takes
        //       trivial level-only chip damage from power=0 enemies). The ally then acts once
        //       with TargetingType.All against both enemies, using a Teamwork attack with
        //       PowerFactor=1.0: since the ally-side counter is independent, this is the ally
        //       side's *first* Teamwork use (0→1), so it should get exactly the +5% bonus (same
        //       26-damage math as FirstTeamworkUse_GrantsFivePercentBonus), NOT a bonus
        //       reflecting the enemy side's count of 2 (which would instead produce 27 damage
        //       per the stacking math, if the counters were incorrectly shared). Both enemies'
        //       maxHp are set to exactly 26 so the ally's single multi-target hit kills both,
        //       ending the fight immediately after the ally's one turn.
        var engine = new CombatEngineClass(new Random(0));

        // Speeds are spaced with a >1.67x ratio between each (halving-and-more), so
        // TurnOrderManager's +/-25% random jitter can't flip the intended act order:
        // enemy1, then enemy2, then ally.
        var ally = new CombatEntity(
            entityId: "ally", name: "Ally", level: 1,
            maxHp: 1000, hp: 1000, maxTp: 0, tp: 0,
            power: 10, defense: 0, speed: 1,
            evasion: 0f, critChance: 0f, critModifier: 0f);

        var enemy1 = new CombatEntity(
            entityId: "enemy1", name: "Enemy1", level: 1,
            maxHp: 26, hp: 26, maxTp: 0, tp: 0,
            power: 0, defense: 0, speed: 1000,
            evasion: 0f, critChance: 0f, critModifier: 0f);

        var enemy2 = new CombatEntity(
            entityId: "enemy2", name: "Enemy2", level: 1,
            maxHp: 26, hp: 26, maxTp: 0, tp: 0,
            power: 0, defense: 0, speed: 500,
            evasion: 0f, critChance: 0f, critModifier: 0f);

        engine.InitCombat(allies: [ally], enemies: [enemy1, enemy2]);

        CombatEventBus.WaitingForTurn += (entityId, _, _, isAlly) =>
        {
            if (isAlly)
            {
                engine.SubmitCommand(new CombatCommand
                {
                    ActorId       = entityId,
                    TargetingType = TargetingType.All,
                    ValidTargets  = ValidTarget.Enemies,
                    LivingOrDead  = LivingOrDead.Living,
                    TPCost        = 0,
                    Keywords      = [TeamworkKeyword.KeywordName],
                    CombatFunction = BasicDamageFunction.FunctionName,
                    Parameters = new CombatFunctionParameters { CalcType = DamageOrHealCalcType.StandardFormula, PowerFactor = 1.0 },
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
                    Keywords      = [TeamworkKeyword.KeywordName],
                    CombatFunction = BasicDamageFunction.FunctionName,
                    Parameters = new CombatFunctionParameters { CalcType = DamageOrHealCalcType.StandardFormula, PowerFactor = 1.0 },
                });
            }
        };

        var allyHits = new List<int>();
        CombatEventBus.EntityDamaged += (targetId, _, dmg, _, _, _, _, _, _, _) =>
        {
            if (targetId is "enemy1" or "enemy2") allyHits.Add(dmg);
        };

        engine.BeginCombat();

        Assert.Equal([26, 26], allyHits);
        Assert.True(enemy1.IsDead);
        Assert.True(enemy2.IsDead);
    }
}
