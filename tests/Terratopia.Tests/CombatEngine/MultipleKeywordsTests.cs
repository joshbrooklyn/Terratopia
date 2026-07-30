using CombatEngine;
using CombatEngine.DataClasses;
using CombatEngine.Engine;
using CombatEngine.Enums;
using CombatEngine.Keywords;

namespace Terratopia.Tests.CombatEngine;

[Collection("CombatEngineSerial")]
public class MultipleKeywordsTests
{
    [Fact]
    public void EngageAndStoic_BothActiveOnSameAction_StackToDoubleTheBonusFromEitherAlone()
    {
        // What: reproduces docs/keywords.md rule 3's own worked example: "a 80% Power Mod Tech
        //       with Engage and Stoic can go up to 180%" - i.e. two keywords active on the same
        //       action are each capped independently, and their capped bonuses are summed
        //       rather than the combination being capped as a single 50%-over-base unit.
        // How:  Base PowerFactor=0.8, so the shared per-keyword cap is
        //       min(0.8*2, 0.8+0.5)=min(1.6,1.3)=1.3, but each individual keyword's raw bonus
        //       (Engage and Stoic both cap their own contribution at 0.5) never gets close to
        //       that ceiling, so both apply in full. Stoic requires the actor at <=25% of max
        //       HP (ally hp=200 of maxHp=1000 -> 20%, condition met); Engage requires the target
        //       at >=75% of max HP (enemy stays at full HP until the single lethal hit,
        //       condition met). Base 80% + Engage's +50% + Stoic's +50% = 180% power mod ->
        //       actionPower = 10*1.8 = 18 -> baseDamage = 18*2 + 1*5 = 41 -> damage 41. enemyHp
        //       is set to exactly 41 so this single hit ends the fight before the enemy ever acts.
        var engine = new CombatEngineClass(new Random(0));

        // Stoic requires the actor at <=25% of max HP.
        var ally = new CombatEntity(
            entityId: "ally", name: "Ally", level: 1,
            maxHp: 1000, hp: 200, maxTp: 0, tp: 0,
            power: 10, defense: 0, speed: 100,
            evasion: 0f, critChance: 0f, critModifier: 0f);

        // Engage requires the target at >=75% of max HP. maxHp is set to exactly the expected
        // damage so the single hit kills it, ending combat before the enemy ever acts.
        var enemy = new CombatEntity(
            entityId: "enemy", name: "Enemy", level: 1,
            maxHp: 41, hp: 41, maxTp: 0, tp: 0,
            power: 0, defense: 0, speed: 1,
            evasion: 0f, critChance: 0f, critModifier: 0f);

        engine.InitCombat(allies: [ally], enemies: [enemy]);

        CombatEventBus.WaitingForTurn += (entityId, _, _, isAlly) =>
        {
            if (!isAlly) return; // enemy dies to the ally's hit before ever getting a turn.

            engine.SubmitCommand(new CombatCommand
            {
                ActorId       = entityId,
                TargetingType = TargetingType.Random,
                ValidTargets  = ValidTarget.Enemies,
                LivingOrDead  = LivingOrDead.Living,
                TPCost        = 0,
                Keywords      = [EngageKeyword.KeywordName, StoicKeyword.KeywordName],
                DirectEffects = [new CombatDirectEffect { EffectType = CombatDirectEffectType.Damage, CalcType = DamageCalcType.StandardFormula, PowerFactor = 0.8 }],
            });
        };

        int? damageDealt = null;
        CombatEventBus.EntityDamaged += (targetId, _, dmg, _, _, _) =>
        {
            if (targetId == "enemy") damageDealt ??= dmg;
        };

        engine.BeginCombat();

        Assert.NotNull(damageDealt);
        Assert.Equal(41, damageDealt.Value);
        Assert.True(enemy.IsDead);
    }

    [Fact]
    public void TeamworkAndGrowth_BothActiveOnSameCommand_TrackIndependentCountersAndSum()
    {
        // What: verifies two differently-scoped power keywords (Teamwork, keyed per side;
        //       Growth, keyed per actor+action) coexist on the same command without their usage
        //       counters colliding, and that their bonuses simply sum (each independently
        //       capped) rather than one clobbering the other.
        // How:  Base PowerFactor=0.3, so cap = min(0.3, 0.5) = 0.3 for both keywords on every
        //       hit. Use 1: Teamwork raw = 0.05*1 = 0.05 (capped 0.05, "ally:Teamwork" key).
        //       Growth raw = 0.10*(1-1) = 0 (capped 0, "growth:ally:tech1" key).
        //       effectivePowerFactor = 0.3 + 0.05 = 0.35 -> actionPower = 3.5 ->
        //       baseDamage = 3.5*2 + 5 = 12 -> damage 12. Use 2: Teamwork raw = 0.05*2 = 0.10
        //       (capped 0.10). Growth raw = 0.10*(2-1) = 0.10 (capped 0.10).
        //       effectivePowerFactor = 0.3 + 0.10 + 0.10 = 0.5 -> actionPower = 5 ->
        //       baseDamage = 10 + 5 = 15 -> damage 15. enemyHp = 12+15 = 27 so combat ends
        //       exactly after the second hit. The test asserts the damage sequence is [12, 15],
        //       confirming the two keywords' usage keys don't collide - each stacks correctly
        //       across uses independent of the other.
        var engine = new CombatEngineClass(new Random(0));

        var ally = new CombatEntity(
            entityId: "ally", name: "Ally", level: 1,
            maxHp: 1000, hp: 1000, maxTp: 0, tp: 0,
            power: 10, defense: 0, speed: 100,
            evasion: 0f, critChance: 0f, critModifier: 0f);

        var enemy = new CombatEntity(
            entityId: "enemy", name: "Enemy", level: 1,
            maxHp: 12 + 15, hp: 12 + 15, maxTp: 0, tp: 0,
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
                    Keywords      = [TeamworkKeyword.KeywordName, GrowthKeyword.KeywordName],
                    DirectEffects = [new CombatDirectEffect { EffectType = CombatDirectEffectType.Damage, CalcType = DamageCalcType.StandardFormula, PowerFactor = 0.3 }],
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
                    DirectEffects = [new CombatDirectEffect { EffectType = CombatDirectEffectType.Damage, CalcType = DamageCalcType.StandardFormula, PowerFactor = 1.0 }],
                });
            }
        };

        var damages = new List<int>();
        CombatEventBus.EntityDamaged += (targetId, _, dmg, _, _, _) =>
        {
            if (targetId == "enemy") damages.Add(dmg);
        };

        engine.BeginCombat();

        Assert.Equal([12, 15], damages);
        Assert.True(enemy.IsDead);
    }
}
