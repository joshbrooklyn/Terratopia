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

    // attacker → ally (player, Speed=10, goes first each round).
    // counter  → enemy (AI, Speed=5, goes second). Power=10/Level=1/Defense=0 against the attacker
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
            allies:          [attacker],
            enemies:         [counter],
            chooseAiCommand: e => new CombatCommand
            {
                ActorId       = e.EntityId,
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

        // Wire AFTER InitCombat (which calls CombatEventBus.Reset).
        CombatEventBus.WaitingForPlayerAction += (_, _, _) =>
        {
            engine.SubmitPlayerCommand(new CombatCommand
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
        };

        return (engine, attacker, counter);
    }

    [Fact]
    public void Damage_WithNoDefense_DealsExpectedAmount()
    {
        // actionPower=10; baseDamage=20+5=25; rawDamage=25/1.0−0=25; damage=25
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
        // actionPower=50; baseDamage=105; rawDamage=105/1.390625−25=50.51; damage=50
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
        // actionPower=5; baseDamage=15; rawDamage=7.5−64=−56.5→0
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
        // actionPower=10*2.0=20; baseDamage=40+5=45; rawDamage=45; damage=45
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
        // actionPower=0; baseDamage=0+(5*5)=25; rawDamage=25; damage=25
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
        // base=25; isCrit=NextSingle()<1.0f=true; damage=(int)(25*1.5f)=37
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
        // critChance=0.0 → NextSingle() < 0.0 is always false; isCrit=false, damage unmodified
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
        // rng=0.4; evasion: 0.4 < 0.0 = false; crit: 0.4 < 0.5 = true; damage=(int)(25*1.5f)=37
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
        // rng=0.5; evasion: 0.5 < 0.0 = false; crit: 0.5 < 0.5 = false; damage=25
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
        // critChance=1.0; critModifier=1.0 → multiplier=(1+1.0)=2.0; damage=(int)(25*2.0f)=50
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
        // actionPower=50; baseDamage=105; rawDamage=105/1.390625−25≈50; damage=50
        // isCrit=true; critDamage=(int)(50*1.5f)=75
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
