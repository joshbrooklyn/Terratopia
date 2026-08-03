using CombatEngine;
using CombatEngine.CombatFunctions;
using CombatEngine.DataClasses;
using CombatEngine.Engine;
using CombatEngine.Enums;

namespace Terratopia.Tests.CombatEngine.PublicInterface;

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
        CombatFunction = BasicDamageFunction.FunctionName,
        Parameters     = new CombatFunctionParameters
        {
            CalcType    = DamageOrHealCalcType.StandardFormula,
            PowerFactor = 1.0,
        },
    };

    // Creates a fresh (non-singleton) engine with a controlled RNG.
    // Defender → _allies (isAlly = true → shows up as the ally branch of WaitingForTurn).
    // Attacker → _enemies (isAlly = false → enemy branch of WaitingForTurn, targets _allies[0] = defender).
    // A WaitingForTurn subscription is wired so each side's turn is handled:
    //   defender's turn is a no-op (TargetingType.Single has no case in ExpandAutoTargets →
    //   ChosenTargets stays null, but NoOp means ResolveAction never reads it anyway);
    //   attacker's turn always submits MakeMeleeCommand(attacker).
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
            allies:  [defender],
            enemies: [attacker]);

        // Wire both sides' turns AFTER InitCombat (which calls CombatEventBus.Reset).
        CombatEventBus.WaitingForTurn += (_, _, _, isAlly) =>
        {
            if (isAlly)
            {
                engine.SubmitCommand(new CombatCommand
                {
                    ActorId       = "defender",
                    TargetingType = TargetingType.Self,
                    ValidTargets  = ValidTarget.Allies,
                    LivingOrDead  = LivingOrDead.Living,
                    CombatFunction = NoOpFunction.FunctionName,
                });
            }
            else
            {
                engine.SubmitCommand(MakeMeleeCommand(attacker));
            }
        };

        return (engine, attacker, defender);
    }

    [Fact]
    public void Attack_IsEvaded_WhenRandomValueBelowEvasion()
    {
        // What: verifies that when the random evasion roll lands below the defender's evasion
        //       stat, the attack is evaded (AttackEvaded fires) instead of landing as damage.
        // How:  SetupCombat is given a ControlledRandom fixed at 0.05 and a defender evasion of
        //       0.1, so every NextSingle() call the engine makes returns 0.05. The engine's
        //       evasion check is `rng.NextSingle() < target.Evasion`, i.e. 0.05 < 0.1, which is
        //       true, so the very first attack against the defender should evade rather than
        //       deal damage. The test subscribes to both AttackEvaded and EntityDamaged and
        //       tracks which one fires first for "defender", then asserts AttackEvaded fired
        //       and EntityDamaged never fired before it.
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
        // What: verifies the counterpart case to the evasion test above — when the roll is
        //       NOT below the defender's evasion, the attack should land as damage and
        //       AttackEvaded should never fire at all.
        // How:  SetupCombat is given a ControlledRandom fixed at 0.5 and a defender evasion of
        //       0.0, so the evasion check `0.5 < 0.0` is always false. With evasion never
        //       triggering, the attack falls through to the normal damage path immediately.
        //       The test subscribes to EntityDamaged and AttackEvaded for "defender" and
        //       asserts damage was received while evasion never occurred.
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
        // What: verifies that each successful dodge permanently reduces the defender's own
        //       evasion stat by a flat 0.25, rather than evasion staying constant across
        //       repeated dodges.
        // How:  SetupCombat uses a ControlledRandom fixed at 0.05 and gives the defender a
        //       very high evasion of 1.0, so the roll (0.05 < 1.0) is guaranteed true and the
        //       first attack against the defender always evades. In the engine, evasion is
        //       decremented by 0.25 (floored at 0) before AttackEvaded is raised, so by the
        //       time this test's handler runs, defender.Evasion should already reflect the
        //       post-dodge value of 1.0 − 0.25 = 0.75. The test captures defender.Evasion
        //       inside the AttackEvaded handler and asserts it equals 0.75.
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
        // What: verifies an evaded attack truly deals zero damage — evasion isn't just a flag
        //       that gets reported while HP still ticks down underneath it.
        // How:  As in the previous test, SetupCombat is given a ControlledRandom fixed at 0.05
        //       and a defender evasion of 1.0, so the very first attack against the defender is
        //       guaranteed to evade (0.05 < 1.0). The test captures defender.Hp at the moment
        //       AttackEvaded first fires and asserts it is still 100 (the defender's starting
        //       HP), confirming the evaded hit never touched HP at all.
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
        // What: verifies the 0.25 evasion degradation is clamped at zero rather than allowed
        //       to go negative when the defender's remaining evasion is smaller than 0.25.
        // How:  SetupCombat is given a ControlledRandom fixed at 0.05 and a defender evasion of
        //       just 0.2. The dodge still fires because 0.05 < 0.2, but subtracting the usual
        //       0.25 from 0.2 would produce −0.05, which the engine instead clamps to 0.0 via
        //       Math.Max. The test captures defender.Evasion inside the AttackEvaded handler
        //       and asserts it equals 0.0, and additionally asserts it is never below zero as a
        //       belt-and-suspenders check on the clamping behavior.
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
