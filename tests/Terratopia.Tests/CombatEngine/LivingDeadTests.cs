using CombatEngine;
using CombatEngine.DataClasses;
using CombatEngine.Engine;
using CombatEngine.Enums;
using CombatEngine.Passives;

namespace Terratopia.Tests.CombatEngine;

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
        };

        return (engine, attacker, defender);
    }

    [Fact]
    public void LethalDamage_WithLivingDead_RevivesOnceThenDiesOnNextLethalHit()
    {
        var (engine, _, defender) = SetupCombat([LivingDeadPassive.PassiveName]);

        var hpTrace = new List<int>();
        int revivedCount = 0;
        int deathCount = 0;

        CombatEventBus.EntityHpChanged += (id, _, _, newHp) => { if (id == "defender") hpTrace.Add(newHp); };
        CombatEventBus.EntityRevived   += (id, _) => { if (id == "defender") revivedCount++; };
        CombatEventBus.EntityDeath     += (id, _) => { if (id == "defender") deathCount++; };

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
        var (engine, _, defender) = SetupCombat(defenderPassives: null);

        int revivedCount = 0;
        int deathCount = 0;

        CombatEventBus.EntityRevived += (id, _) => { if (id == "defender") revivedCount++; };
        CombatEventBus.EntityDeath   += (id, _) => { if (id == "defender") deathCount++; };

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
    private static CombatEntity MakeEntity() => new(
        entityId: "e", name: "E", level: 1,
        maxHp: 10, hp: 0, maxTp: 0, tp: 0,
        power: 0, defense: 0, speed: 0,
        evasion: 0f, critChance: 0f, critModifier: 0f);

    [Fact]
    public void TryPreventDeath_FirstCall_RevivesAtOneHp()
    {
        var entity  = MakeEntity();
        var passive = new LivingDeadPassive();

        bool prevented = passive.TryPreventDeath(entity);

        Assert.True(prevented);
        Assert.Equal(1, entity.Hp);
        Assert.Contains(LivingDeadPassive.PassiveName, entity.ConsumedPassives);
    }

    [Fact]
    public void TryPreventDeath_SecondCall_ReturnsFalseAndLeavesHpUnchanged()
    {
        var entity  = MakeEntity();
        var passive = new LivingDeadPassive();
        passive.TryPreventDeath(entity);

        entity.Hp = 0; // simulate a second lethal hit
        bool preventedAgain = passive.TryPreventDeath(entity);

        Assert.False(preventedAgain);
        Assert.Equal(0, entity.Hp);
    }
}
