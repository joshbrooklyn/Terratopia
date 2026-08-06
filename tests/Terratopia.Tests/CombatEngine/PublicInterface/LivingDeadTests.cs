using CombatEngine;
using CombatEngine.CombatFunctions;
using CombatEngine.DataClasses;
using CombatEngine.Engine;
using CombatEngine.Enums;
using CombatEngine.Passives;

namespace Terratopia.Tests.CombatEngine.PublicInterface;

[Collection("CombatEngineSerial")]
public class LivingDeadTests
{
    // attacker → ally (Speed=10, goes first). Power=10/Level=1 against Defense=0 deals exactly
    // 25 damage per hit. defender → enemy (Speed=5, MaxHp=25) so the first hit is exactly lethal.
    // defender's own counterattack (Power=5) is floored to 0 by the attacker's Defense=128, so the
    // attacker never dies and combat keeps running across multiple rounds.
    private static (CombatEngineClass engine, CombatEntity attacker, CombatEntity defender)
        SetupCombat(IReadOnlyList<string>? defenderPassives)
    {
        var engine = new CombatEngineClass(new Random(0));

        var attacker = new CombatEntity(
            entityId: "attacker", name: "Attacker", level: 1,
            maxHp: 25, hp: 25, maxTp: 0, tp: 0,
            power: 10, defense: 128, speed: 10,
            evasion: 0.0f, critChance: 0.0f, critModifier: 0.0f);

        var defender = new CombatEntity(
            entityId: "defender", name: "Defender", level: 1,
            maxHp: 25, hp: 25, maxTp: 0, tp: 0,
            power: 5, defense: 0, speed: 5,
            evasion: 0.0f, critChance: 0.0f, critModifier: 0.0f,
            passives: defenderPassives);

        engine.InitCombat(
            allies:  [attacker],
            enemies: [defender]);

        // Wire AFTER InitCombat (which calls CombatEventBus.Reset()).
        CombatEventBus.WaitingForTurn += (_, _, _, isAlly) =>
        {
            engine.SubmitCommand(new CombatCommand
            {
                ActorId       = isAlly ? "attacker" : "defender",
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
            });
        };

        return (engine, attacker, defender);
    }

    [Fact]
    public void LethalDamage_WithLivingDead_RevivesOnceThenDiesOnNextLethalHit()
    {
        // What: verifies the LivingDead passive saves an entity from its first lethal hit by
        //       reviving it at 1 HP, but is consumed after that single use — a second lethal
        //       hit should then kill the entity for real.
        // How:  SetupCombat gives the defender the LivingDead passive and pits it against an
        //       attacker whose hits are always exactly lethal (25 damage vs 25 max HP), while
        //       the defender's own counterattacks are floored to 0 damage by the attacker's
        //       high defense — so only the attacker's hits matter and combat runs across
        //       multiple rounds. The test records every EntityDamaged/EntityRevived new-HP value
        //       for "defender" plus counts of EntityRevived and EntityDeath. On the first lethal
        //       hit, HP would drop to 0, but LivingDeadPassive.TryPreventDeath intercepts it and
        //       immediately bumps HP back up to 1, so the trace should show 0 then 1. On the next lethal
        //       hit the passive is already consumed (ConsumedPassives already contains its
        //       name), so this time HP drops to 0 and stays there — a real death. The test
        //       asserts the HP trace is exactly [0, 1, 0], revivedCount is 1, deathCount is 1,
        //       the defender ends up dead at 0 HP, and its ConsumedPassives set contains the
        //       LivingDead passive name.
        var (engine, _, defender) = SetupCombat([LivingDeadPassive.PassiveName]);

        var hpTrace = new List<int>();
        int revivedCount = 0;
        int deathCount = 0;

        CombatEventBus.EntityDamaged += (id, _, _, _, _, _, _, _, _, newHp) => { if (id == "defender") hpTrace.Add(newHp); };
        CombatEventBus.EntityRevived += (id, _, _, newHp, _, _) => { if (id == "defender") { hpTrace.Add(newHp); revivedCount++; } };
        CombatEventBus.EntityDeath   += (id, _, _, _) => { if (id == "defender") deathCount++; };

        engine.BeginCombat();

        // First lethal hit: revive to 1 HP (0, then 1). Second lethal hit: real death (0).
        Assert.Equal([0, 1, 0], hpTrace);
        Assert.Equal(1, revivedCount);
        Assert.Equal(1, deathCount);
        Assert.True(defender.IsDead);
        Assert.Equal(0, defender.Hp);
        Assert.Contains(LivingDeadPassive.PassiveName, defender.ConsumedPassives);
    }

    [Fact]
    public void LethalDamage_WithoutLivingDead_DiesOnFirstHit()
    {
        // What: confirms the baseline behavior an entity with no death-preventing passive
        //       simply dies on the first lethal hit, with no revive — this is the contrast
        //       case for LethalDamage_WithLivingDead_RevivesOnceThenDiesOnNextLethalHit above,
        //       which proves LivingDead is actually doing something rather than death
        //       -prevention being the engine's default behavior.
        // How:  SetupCombat is called with defenderPassives: null, so the defender has no
        //       passives attached at all and nothing intercepts HandleEntityDefeated's death
        //       -prevention hook. The test counts EntityRevived and EntityDeath events raised
        //       for "defender" across the whole fight, then asserts EntityRevived never fired
        //       (revivedCount == 0), EntityDeath fired exactly once (deathCount == 1), and the
        //       defender ends the fight dead at 0 HP.
        var (engine, _, defender) = SetupCombat(defenderPassives: null);

        int revivedCount = 0;
        int deathCount = 0;

        CombatEventBus.EntityRevived += (id, _, _, _, _, _) => { if (id == "defender") revivedCount++; };
        CombatEventBus.EntityDeath   += (id, _, _, _) => { if (id == "defender") deathCount++; };

        engine.BeginCombat();

        Assert.Equal(0, revivedCount);
        Assert.Equal(1, deathCount);
        Assert.True(defender.IsDead);
        Assert.Equal(0, defender.Hp);
    }
}

[Collection("CombatEngineSerial")]
public class LivingDeadPassiveTests
{
    private static CombatEntity MakeEntity(int hp) => new(
        entityId: "e", name: "E", level: 1,
        maxHp: 10, hp: hp, maxTp: 0, tp: 0,
        power: 0, defense: 0, speed: 0,
        evasion: 0f, critChance: 0f, critModifier: 0f, passives: [LivingDeadPassive.PassiveName]);

    [Fact]
    public void HandleDefeat_FirstCall_RevivesAtOneHp()
    {
        // What: drives LivingDeadPassive through the real combat entity (TakeDamage ->
        //       HandleDefeat -> OnBeforeDeath) to verify that on the entity's first lethal hit
        //       it revives at 1 HP instead of dying.
        // How:  MakeEntity(1) builds a CombatEntity at 1 HP carrying the LivingDead passive.
        //       killme.TakeDamage(attacker, 1, ...) brings it to 0 HP, which triggers
        //       HandleDefeat -> LivingDeadPassive.OnBeforeDeath, which should add the passive's
        //       name to killme.ConsumedPassives (marking it used) and report (true, 1 HP) so the
        //       engine sets Hp back to 1 instead of marking the entity dead. The test asserts
        //       killme is not dead, killme.Hp is 1, and ConsumedPassives contains the LivingDead
        //       passive name.
        var killme  = MakeEntity(1);
        var attacker = MakeEntity(1);

        killme.TakeDamage(attacker, 1, "test", "Test"); // simulate lethal damage

        Assert.False(killme.IsDead);
        Assert.Equal(1, killme.Hp);
        Assert.Contains(LivingDeadPassive.PassiveName, killme.ConsumedPassives);
    }

    [Fact]
    public void OnBeforeDeath_SecondCall_ReturnsFalseAndLeavesHpUnchanged()
    {
        // What: verifies LivingDeadPassive is single-use — a second call to OnBeforeDeath on
        //       the same entity must report deathPrevented: false and leave HP at 0, rather than
        //       reviving again.
        // How:  The passive is invoked once up front to consume it (mirroring the setup in
        //       HandleDefeat_FirstCall_RevivesAtOneHp), then entity.TakeDamage brings HP back to
        //       0 to simulate a second lethal hit. Calling OnBeforeDeath(entity) again should hit
        //       the internal `HasConsumedPassive` guard — since the name is already in
        //       ConsumedPassives, the method returns (false, 0) immediately without touching HP.
        //       The test asserts the second call's deathPrevented is false and entity.Hp is
        //       still 0.
        var entity  = MakeEntity(1);
        var passive = new LivingDeadPassive();
        passive.OnBeforeDeath(entity);

        entity.TakeDamage(entity, entity.Hp, "test", "Test"); // simulate a second lethal hit
        var (preventedAgain, _) = passive.OnBeforeDeath(entity);

        Assert.False(preventedAgain);
        Assert.Equal(0, entity.Hp);
    }
}
