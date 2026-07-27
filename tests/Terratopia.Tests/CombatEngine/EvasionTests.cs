using CombatEngine;
using CombatEngine.DataClasses;
using CombatEngine.Engine;
using CombatEngine.Enums;

namespace Terratopia.Tests.CombatEngine;

[Collection("CombatEngineSerial")]
public class EvasionTests
{
    // Returns a fixed value for every NextSingle() call and 0 for every Next(int) call,
    // so AI always picks index 0 from the valid-targets pool.
    private sealed class ControlledRandom(float single) : Random
    {
        public override float NextSingle() => single;
        public override int Next(int maxValue) => 0;
    }

    // The attacker (AI/enemy) uses this command each turn: single-target damage against enemies.
    private static CombatCommand MakeMeleeCommand(CombatEntity actor) => new()
    {
        ActorId       = actor.EntityId,
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
            }
        ],
    };

    // Creates a fresh (non-singleton) engine with a controlled RNG.
    // Defender → _allies (isPlayerEntity = true → WaitingForPlayerAction).
    // Attacker → _enemies (isPlayerEntity = false → AIDeciding, targets _allies[0] = defender).
    // A WaitingForPlayerAction subscription is wired so the defender's turn is a no-op:
    //   TargetingType.Single has no case in ExpandAutoTargets → ChosenTargets stays null,
    //   but DirectEffects = [] means ResolveAction never reads ChosenTargets anyway.
    // Subscribe to other CombatEventBus events AFTER this method returns, before BeginCombat.
    private static (CombatEngineClass engine, CombatEntity attacker, CombatEntity defender)
        SetupCombat(float rngValue, float defenderEvasion)
    {
        var rng    = new ControlledRandom(rngValue);
        var engine = new CombatEngineClass(rng);

        // Speed=10 → score 7.75 with rng=0.05; goes first every round.
        var attacker = new CombatEntity(
            entityId: "attacker", name: "Attacker", level: 1,
            maxHp: 100, hp: 100, maxTp: 0, tp: 0,
            power: 10, defense: 0, speed: 10,
            evasion: 0.0f, critChance: 0.0f, critModifier: 0.0f);

        // Speed=5 → score 3.875; goes second. Power=0 → does no useful damage.
        var defender = new CombatEntity(
            entityId: "defender", name: "Defender", level: 1,
            maxHp: 100, hp: 100, maxTp: 0, tp: 0,
            power: 0, defense: 0, speed: 5,
            evasion: defenderEvasion, critChance: 0.0f, critModifier: 0.0f);

        engine.InitCombat(
            allies:          [defender],
            enemies:         [attacker],
            chooseAiCommand: MakeMeleeCommand);

        // Wire defender's no-op turn AFTER InitCombat (which calls CombatEventBus.Reset).
        CombatEventBus.WaitingForPlayerAction += (_, _, _) =>
        {
            engine.SubmitPlayerCommand(new CombatCommand
            {
                ActorId       = "defender",
                TargetingType = TargetingType.Self,
                ValidTargets  = ValidTarget.Allies,
                LivingOrDead  = LivingOrDead.Living,
                DirectEffects = [],
            });
        };

        return (engine, attacker, defender);
    }

    [Fact]
    public void Attack_IsEvaded_WhenRandomValueBelowEvasion()
    {
        // 0.05 < 0.1 → evasion fires before any damage on the first round
        var (engine, _, _) = SetupCombat(rngValue: 0.05f, defenderEvasion: 0.1f);

        bool evadeBeforeHit = false;
        bool hitBeforeEvade = false;

        CombatEventBus.AttackEvaded += (_, _, targetId, _) =>
        {
            if (targetId == "defender") evadeBeforeHit = true;
        };
        CombatEventBus.EntityDamaged += (targetId, _, _, _, _, _) =>
        {
            if (targetId == "defender" && !evadeBeforeHit) hitBeforeEvade = true;
        };

        engine.BeginCombat();

        Assert.True(evadeBeforeHit,  "AttackEvaded should fire for the defender.");
        Assert.False(hitBeforeEvade, "EntityDamaged should not fire before AttackEvaded fires.");
    }

    [Fact]
    public void Attack_HitsTarget_WhenEvasionBelowRandomValue()
    {
        // 0.5 < 0.0 is false → evasion never fires; damage lands immediately
        var (engine, _, _) = SetupCombat(rngValue: 0.5f, defenderEvasion: 0.0f);

        bool damageReceived = false;
        bool evadeOccurred  = false;

        CombatEventBus.EntityDamaged += (targetId, _, _, _, _, _) =>
        {
            if (targetId == "defender") damageReceived = true;
        };
        CombatEventBus.AttackEvaded += (_, _, targetId, _) =>
        {
            if (targetId == "defender") evadeOccurred = true;
        };

        engine.BeginCombat();

        Assert.True(damageReceived, "EntityDamaged should fire for the defender.");
        Assert.False(evadeOccurred, "AttackEvaded should never fire when evasion = 0.");
    }

    [Fact]
    public void Evasion_DegradesBy025_OnSuccessfulDodge()
    {
        // 0.05 < 1.0 → first dodge reduces 1.0 by 0.25 → 0.75
        // AttackEvaded is raised AFTER target.Evasion is already reduced, so
        // reading defender.Evasion inside the handler gives the post-degradation value.
        var (engine, _, defender) = SetupCombat(rngValue: 0.05f, defenderEvasion: 1.0f);

        float? capturedEvasion = null;

        CombatEventBus.AttackEvaded += (_, _, targetId, _) =>
        {
            if (targetId == "defender") capturedEvasion ??= defender.Evasion;
        };

        engine.BeginCombat();

        Assert.NotNull(capturedEvasion);
        Assert.Equal(0.75f, capturedEvasion.Value, precision: 5);
    }

    [Fact]
    public void Evaded_Attack_DealsNoDamage()
    {
        // 0.05 < 1.0 → always evades; defender HP must stay at 100 after each evaded hit
        var (engine, _, defender) = SetupCombat(rngValue: 0.05f, defenderEvasion: 1.0f);

        int? hpAfterFirstEvade = null;

        CombatEventBus.AttackEvaded += (_, _, targetId, _) =>
        {
            if (targetId == "defender") hpAfterFirstEvade ??= defender.Hp;
        };

        engine.BeginCombat();

        Assert.NotNull(hpAfterFirstEvade);
        Assert.Equal(100, hpAfterFirstEvade.Value);
    }

    [Fact]
    public void Evasion_ClampsAtZero_WhenDegradationWouldGoNegative()
    {
        // 0.05 < 0.2 → dodge fires; 0.2 - 0.25 = -0.05 → clamped to 0.0
        var (engine, _, defender) = SetupCombat(rngValue: 0.05f, defenderEvasion: 0.2f);

        float? capturedEvasion = null;

        CombatEventBus.AttackEvaded += (_, _, targetId, _) =>
        {
            if (targetId == "defender") capturedEvasion ??= defender.Evasion;
        };

        engine.BeginCombat();

        Assert.NotNull(capturedEvasion);
        Assert.Equal(0.0f, capturedEvasion.Value, precision: 5);
        Assert.True(capturedEvasion.Value >= 0.0f, "Evasion must never go below zero.");
    }
}

[CollectionDefinition("CombatEngineSerial", DisableParallelization = true)]
public class CombatEngineSerialCollection { }
