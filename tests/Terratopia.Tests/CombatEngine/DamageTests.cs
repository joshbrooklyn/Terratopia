using CombatEngine;
using CombatEngine.DataClasses;
using CombatEngine.Engine;
using CombatEngine.Enums;

namespace Terratopia.Tests.CombatEngine;

[Collection("CombatEngineSerial")]
public class DamageTests
{
    // Returns a fixed value for every NextSingle() call and 0 for every Next(int) call,
    // so AI always picks index 0 from the valid-targets pool.
    private sealed class ControlledRandom(float single) : Random
    {
        public override float NextSingle() => single;
        public override int Next(int maxValue) => 0;
    }

    // attacker → ally (Speed=10, goes first each round).
    // counter  → enemy (Speed=5, goes second). Power=10/Level=1/Defense=0 against the attacker
    //            so the counter always deals exactly 25 damage → kills attacker (MaxHp=25) after
    //            one round, ending combat regardless of how much damage the attacker dealt.
    private static (CombatEngineClass engine, CombatEntity attacker, CombatEntity counter)
        SetupCombat(int power, int level, float critChance, float critModifier,
                    int targetDefense, double powerFactor = 1.0, Random? rng = null)
    {
        var engine = new CombatEngineClass(rng ?? new Random(0));

        var attacker = new CombatEntity(
            entityId: "attacker", name: "Attacker", level: level,
            maxHp: 25, hp: 25, maxTp: 0, tp: 0,
            power: power, defense: 0, speed: 10,
            evasion: 0.0f, critChance: critChance, critModifier: critModifier);

        var counter = new CombatEntity(
            entityId: "counter", name: "Counter", level: 1,
            maxHp: 1000, hp: 1000, maxTp: 0, tp: 0,
            power: 10, defense: targetDefense, speed: 5,
            evasion: 0.0f, critChance: 0.0f, critModifier: 0.0f);

        engine.InitCombat(
            allies:  [attacker],
            enemies: [counter]);

        // Wire AFTER InitCombat (which calls CombatEventBus.Reset).
        CombatEventBus.WaitingForTurn += (_, _, _, isAlly) =>
        {
            if (isAlly)
            {
                engine.SubmitCommand(new CombatCommand
                {
                    ActorId       = "attacker",
                    TargetingType = TargetingType.Random,
                    ValidTargets  = ValidTarget.Enemies,
                    LivingOrDead  = LivingOrDead.Living,
                    TPCost        = 0,
                    DirectEffects =
                    [
                        new CombatDirectEffect
                        {
                            EffectType  = CombatDirectEffectType.Damage,
                            CalcType    = DamageCalcType.StandardFormula,
                            PowerFactor = powerFactor,
                        },
                    ],
                });
            }
            else
            {
                engine.SubmitCommand(new CombatCommand
                {
                    ActorId       = "counter",
                    TargetingType = TargetingType.Random,
                    ValidTargets  = ValidTarget.Enemies,
                    LivingOrDead  = LivingOrDead.Living,
                    TPCost        = 0,
                    DirectEffects =
                    [
                        new CombatDirectEffect
                        {
                            EffectType  = CombatDirectEffectType.Damage,
                            CalcType    = DamageCalcType.StandardFormula,
                            PowerFactor = 1.0,
                        },
                    ],
                });
            }
        };

        return (engine, attacker, counter);
    }

    [Fact]
    public void Damage_WithNoDefense_DealsExpectedAmount()
    {
        // What: verifies the standard damage formula deals the full computed amount when the
        //       target has zero defense, i.e. no damage reduction is applied at all.
        // How:  SetupCombat is called with power=10, level=1, and targetDefense=0. Working
        //       through the standard formula by hand: actionPower is just the attacker's power
        //       (10), so baseDamage = actionPower*2 + level*5 = 20 + 5 = 25. With defense at 0,
        //       rawDamage is baseDamage/1.0 minus 0, which stays 25, and since nothing floors or
        //       multiplies it further here, the final damage should land exactly on 25. The test
        //       subscribes to EntityDamaged, captures the first hit dealt to "counter", and
        //       asserts it equals 25.
        var (engine, _, _) = SetupCombat(
            power: 10, level: 1, critChance: 0.0f, critModifier: 0.0f, targetDefense: 0);

        int? damageDealt = null;
        CombatEventBus.EntityDamaged += (targetId, _, dmg, _, _, _) =>
        {
            if (targetId == "counter") damageDealt ??= dmg;
        };

        engine.BeginCombat();

        Assert.NotNull(damageDealt);
        Assert.Equal(25, damageDealt.Value);
    }

    [Fact]
    public void Damage_IsReducedByDefense()
    {
        // What: verifies that a target's defense stat reduces incoming damage below the raw
        //       amount the attacker's power would otherwise deal.
        // How:  SetupCombat is called with power=50, level=1, and targetDefense=50 this time.
        //       baseDamage = 50*2 + 1*5 = 105, same as before. Defense feeds into both a divisor
        //       and a flat subtraction in the formula: rawDamage = baseDamage/(1 + defense/128)
        //       − defense/2, which here is 105/(1 + 50/128) − 25 = 105/1.390625 − 25 ≈ 50.51,
        //       truncated to 50 by the engine's integer damage math. The test captures the first
        //       EntityDamaged hit on "counter" and asserts it equals 50, confirming defense
        //       meaningfully cuts down the 105 raw power would otherwise imply.
        var (engine, _, _) = SetupCombat(
            power: 50, level: 1, critChance: 0.0f, critModifier: 0.0f, targetDefense: 50);

        int? damageDealt = null;
        CombatEventBus.EntityDamaged += (targetId, _, dmg, _, _, _) =>
        {
            if (targetId == "counter") damageDealt ??= dmg;
        };

        engine.BeginCombat();

        Assert.NotNull(damageDealt);
        Assert.Equal(50, damageDealt.Value);
    }

    [Fact]
    public void Damage_FloorsAtZero_WhenDefenseOverwhelms()
    {
        // What: verifies damage never goes negative — an overwhelmingly high defense should
        //       floor the hit at 0 rather than produce a negative or nonsensical value.
        // How:  SetupCombat is called with a weak attacker (power=5, level=1) against a very
        //       tanky target (targetDefense=128). baseDamage = 5*2 + 1*5 = 15. Plugging
        //       defense=128 into the formula gives rawDamage = 15/((128+128)/128) − 128/2 =
        //       15/2 − 64 = 7.5 − 64 = −56.5, a clearly negative number. The engine clamps
        //       damage to Math.Max(0, rawDamage) before casting to int, so the test asserts the
        //       EntityDamaged event for "counter" reports exactly 0, not a negative number.
        var (engine, _, _) = SetupCombat(
            power: 5, level: 1, critChance: 0.0f, critModifier: 0.0f, targetDefense: 128);

        int? damageDealt = null;
        CombatEventBus.EntityDamaged += (targetId, _, dmg, _, _, _) =>
        {
            if (targetId == "counter") damageDealt ??= dmg;
        };

        engine.BeginCombat();

        Assert.NotNull(damageDealt);
        Assert.Equal(0, damageDealt.Value);
    }

    [Fact]
    public void Damage_ScalesWithPowerFactor()
    {
        // What: verifies a CombatDirectEffect's PowerFactor scales the attacker's effective
        //       power before the damage formula runs — e.g. a "2x power" skill should hit
        //       noticeably harder than a plain attack from the same attacker.
        // How:  SetupCombat is called with power=10 and powerFactor=2.0, which SetupCombat
        //       plugs into the attacker's DirectEffect as PowerFactor. Internally,
        //       actionPower = actor.Power * PowerFactor = 10 * 2.0 = 20, so
        //       baseDamage = 20*2 + 1*5 = 45. With targetDefense=0 nothing reduces it further,
        //       so the expected damage is 45 — noticeably more than the 25 a powerFactor of 1.0
        //       would produce with the same base power. The test asserts the captured
        //       EntityDamaged amount for "counter" equals 45.
        var (engine, _, _) = SetupCombat(
            power: 10, level: 1, critChance: 0.0f, critModifier: 0.0f,
            targetDefense: 0, powerFactor: 2.0);

        int? damageDealt = null;
        CombatEventBus.EntityDamaged += (targetId, _, dmg, _, _, _) =>
        {
            if (targetId == "counter") damageDealt ??= dmg;
        };

        engine.BeginCombat();

        Assert.NotNull(damageDealt);
        Assert.Equal(45, damageDealt.Value);
    }

    [Fact]
    public void Damage_ScalesWithLevel()
    {
        // What: verifies the attacker's level contributes to damage independently of power —
        //       even an attacker with zero power should still deal some damage from leveling.
        // How:  SetupCombat is called with power=0 (so the power term of the formula
        //       contributes nothing) and level=5. actionPower = 0, so
        //       baseDamage = 0*2 + 5*5 = 25 comes entirely from the level term. With
        //       targetDefense=0, rawDamage stays 25 and nothing reduces it further, so the
        //       expected damage is 25 purely from leveling. The test asserts the captured
        //       EntityDamaged amount for "counter" equals 25, proving level alone can deal
        //       damage.
        var (engine, _, _) = SetupCombat(
            power: 0, level: 5, critChance: 0.0f, critModifier: 0.0f, targetDefense: 0);

        int? damageDealt = null;
        CombatEventBus.EntityDamaged += (targetId, _, dmg, _, _, _) =>
        {
            if (targetId == "counter") damageDealt ??= dmg;
        };

        engine.BeginCombat();

        Assert.NotNull(damageDealt);
        Assert.Equal(25, damageDealt.Value);
    }

    [Fact]
    public void CriticalHit_MultipliesDamageByOnePlusCritModifier()
    {
        // What: verifies that when a critical hit occurs, the base damage is multiplied by
        //       (1 + critModifier) rather than replaced by some fixed bonus.
        // How:  SetupCombat is called with power=10, level=1, targetDefense=0 (the same setup
        //       as the very first test, so base damage before any crit is 25), plus
        //       critChance=1.0 and critModifier=0.5. Because critChance is 1.0, the engine's
        //       roll (rng.NextSingle() < critChance) is guaranteed true regardless of the
        //       actual random value, so every hit crits. The crit multiplier applied is
        //       1 + critModifier = 1.5, so the final damage is (int)(25 * 1.5) = 37. The test
        //       captures both the damage amount and the isCrit flag from EntityDamaged and
        //       asserts damage equals 37 and isCrit is true.
        var (engine, _, _) = SetupCombat(
            power: 10, level: 1, critChance: 1.0f, critModifier: 0.5f, targetDefense: 0);

        int?  damageDealt = null;
        bool? wasCrit     = null;
        CombatEventBus.EntityDamaged += (targetId, _, dmg, _, _, isCrit) =>
        {
            if (targetId != "counter") return;
            damageDealt ??= dmg;
            wasCrit     ??= isCrit;
        };

        engine.BeginCombat();

        Assert.NotNull(damageDealt);
        Assert.Equal(37, damageDealt.Value);
        Assert.NotNull(wasCrit);
        Assert.True(wasCrit.Value, "EntityDamaged isCrit should be true when CritChance=1.0.");
    }

    [Fact]
    public void NoCriticalHit_ReportsIsCritFalse_WhenCritChanceIsZero()
    {
        // What: verifies that with a zero crit chance, hits are never reported as critical and
        //       damage is left completely unmodified by the crit multiplier.
        // How:  SetupCombat is called with the same base setup as the first test (power=10,
        //       level=1, targetDefense=0 → base damage 25), but critChance=0.0 and a nonzero
        //       critModifier=0.5 to make sure the modifier really isn't being applied. Since
        //       the engine's crit check is `rng.NextSingle() < critChance`, and NextSingle()
        //       always returns a value in [0, 1), the comparison against 0.0 can never be true,
        //       so isCrit is always false and damage stays at the un-multiplied 25. The test
        //       asserts the captured damage equals 25 and isCrit is false.
        var (engine, _, _) = SetupCombat(
            power: 10, level: 1, critChance: 0.0f, critModifier: 0.5f, targetDefense: 0);

        int?  damageDealt = null;
        bool? wasCrit     = null;
        CombatEventBus.EntityDamaged += (targetId, _, dmg, _, _, isCrit) =>
        {
            if (targetId != "counter") return;
            damageDealt ??= dmg;
            wasCrit     ??= isCrit;
        };

        engine.BeginCombat();

        Assert.NotNull(damageDealt);
        Assert.Equal(25, damageDealt.Value);
        Assert.NotNull(wasCrit);
        Assert.False(wasCrit.Value, "EntityDamaged isCrit should be false when CritChance=0.0.");
    }

    [Fact]
    public void CriticalHit_Fires_WhenRollIsBelowCritChance()
    {
        // What: verifies a crit fires whenever the random roll lands strictly below the
        //       attacker's crit chance, using a fixed (non-1.0/non-0.0) crit chance so the
        //       comparison itself is meaningfully exercised rather than trivially always-true
        //       or always-false.
        // How:  SetupCombat is given a ControlledRandom fixed at 0.4, so every NextSingle()
        //       call in the engine returns exactly 0.4. The engine calls NextSingle() twice per
        //       hit: once for the evasion check (0.4 < target.Evasion, which is 0.0, so this is
        //       false and the attack lands) and once for the crit check (0.4 < critChance=0.5,
        //       which is true). Base damage is 25 as in the first test, so the crit multiplier
        //       (1 + critModifier = 1.5) brings it to (int)(25*1.5) = 37. The test asserts the
        //       captured damage is 37 and isCrit is true.
        var (engine, _, _) = SetupCombat(
            power: 10, level: 1, critChance: 0.5f, critModifier: 0.5f, targetDefense: 0,
            rng: new ControlledRandom(0.4f));

        int?  damageDealt = null;
        bool? wasCrit     = null;
        CombatEventBus.EntityDamaged += (targetId, _, dmg, _, _, isCrit) =>
        {
            if (targetId != "counter") return;
            damageDealt ??= dmg;
            wasCrit     ??= isCrit;
        };

        engine.BeginCombat();

        Assert.NotNull(damageDealt);
        Assert.Equal(37, damageDealt.Value);
        Assert.NotNull(wasCrit);
        Assert.True(wasCrit.Value, "Crit should fire when roll (0.4) < CritChance (0.5).");
    }

    [Fact]
    public void CriticalHit_DoesNotFire_WhenRollEqualsCritChance()
    {
        // What: verifies the crit check is a strict less-than comparison — a roll exactly
        //       equal to critChance should NOT count as a crit (an off-by-one/boundary check).
        // How:  SetupCombat is given a ControlledRandom fixed at 0.5, matching critChance=0.5
        //       exactly. The evasion roll (0.5 < target.Evasion=0.0) is false, so the attack
        //       lands normally, and the crit roll is `0.5 < 0.5`, which is false in C# for
        //       equal floats, so no crit should fire. Damage should therefore stay at the
        //       un-multiplied base of 25. The test asserts damage equals 25 and isCrit is
        //       false, pinning down that the boundary case favors "no crit".
        var (engine, _, _) = SetupCombat(
            power: 10, level: 1, critChance: 0.5f, critModifier: 0.5f, targetDefense: 0,
            rng: new ControlledRandom(0.5f));

        int?  damageDealt = null;
        bool? wasCrit     = null;
        CombatEventBus.EntityDamaged += (targetId, _, dmg, _, _, isCrit) =>
        {
            if (targetId != "counter") return;
            damageDealt ??= dmg;
            wasCrit     ??= isCrit;
        };

        engine.BeginCombat();

        Assert.NotNull(damageDealt);
        Assert.Equal(25, damageDealt.Value);
        Assert.NotNull(wasCrit);
        Assert.False(wasCrit.Value, "Crit should not fire when roll (0.5) == CritChance (0.5) — check is strictly less-than.");
    }

    [Fact]
    public void CriticalHit_ScalesWithCustomCritModifier()
    {
        // What: verifies the crit multiplier genuinely scales with critModifier rather than
        //       always applying a hardcoded 1.5x — a modifier of 1.0 should double damage.
        // How:  SetupCombat is called with critChance=1.0 (guaranteeing every hit crits) and
        //       critModifier=1.0, so the multiplier is 1 + critModifier = 2.0. With base damage
        //       25 (same power=10/level=1/targetDefense=0 setup as the first test), the
        //       expected crit damage is (int)(25*2.0) = 50 — double the base, not the 1.5x seen
        //       in the critModifier=0.5 test above. The test asserts damage equals 50 and
        //       isCrit is true.
        var (engine, _, _) = SetupCombat(
            power: 10, level: 1, critChance: 1.0f, critModifier: 1.0f, targetDefense: 0);

        int?  damageDealt = null;
        bool? wasCrit     = null;
        CombatEventBus.EntityDamaged += (targetId, _, dmg, _, _, isCrit) =>
        {
            if (targetId != "counter") return;
            damageDealt ??= dmg;
            wasCrit     ??= isCrit;
        };

        engine.BeginCombat();

        Assert.NotNull(damageDealt);
        Assert.Equal(50, damageDealt.Value);
        Assert.NotNull(wasCrit);
        Assert.True(wasCrit.Value);
    }

    [Fact]
    public void CriticalHit_AppliesToPostDefenseDamage()
    {
        // What: verifies the crit multiplier is applied AFTER defense has already reduced the
        //       raw damage, not before — i.e. crits multiply the final mitigated damage, not
        //       the attacker's raw power.
        // How:  SetupCombat reuses the same power=50/level=1/targetDefense=50 setup as
        //       Damage_IsReducedByDefense, where defense mitigation alone brings 105 raw damage
        //       down to 50. Here critChance=1.0 guarantees a crit on top of that already
        //       -mitigated 50, and critModifier=0.5 gives a 1.5x multiplier, so the expected
        //       final damage is (int)(50*1.5) = 75. If the engine incorrectly applied the crit
        //       multiplier to the pre-defense raw damage instead, the result would come out
        //       differently, so this test pins down the intended order of operations. The test
        //       asserts damage equals 75 and isCrit is true.
        var (engine, _, _) = SetupCombat(
            power: 50, level: 1, critChance: 1.0f, critModifier: 0.5f, targetDefense: 50);

        int?  damageDealt = null;
        bool? wasCrit     = null;
        CombatEventBus.EntityDamaged += (targetId, _, dmg, _, _, isCrit) =>
        {
            if (targetId != "counter") return;
            damageDealt ??= dmg;
            wasCrit     ??= isCrit;
        };

        engine.BeginCombat();

        Assert.NotNull(damageDealt);
        Assert.Equal(75, damageDealt.Value);
        Assert.NotNull(wasCrit);
        Assert.True(wasCrit.Value, "Crit multiplier should apply to damage after defense reduction.");
    }
}
