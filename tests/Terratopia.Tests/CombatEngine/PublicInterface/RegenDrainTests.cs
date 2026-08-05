using CombatEngine;
using CombatEngine.CombatFunctions;
using CombatEngine.DataClasses;
using CombatEngine.Engine;
using CombatEngine.Enums;

namespace Terratopia.Tests.CombatEngine.PublicInterface;

[Collection("CombatEngineSerial")]
public class RegenDrainTests
{
    // Every regen/drain moves its resource by CombatBalance.Current.RegenDrainHpPct/RegenDrainTpPct,
    // which default to 0.10 when Configure was never called - the same guarantee CombatBalance's
    // header comment gives BuffDebuffTests for TimedBuffPct.
    private const double HpPct = 0.10;
    private const double TpPct = 0.10;

    private static CombatEntity MakeEntity(int maxHp = 100, int hp = 100, int maxTp = 50, int tp = 50) =>
        new(entityId: "e", name: "Entity", level: 1,
            maxHp: maxHp, hp: hp, maxTp: maxTp, tp: tp,
            power: 20, defense: 20, speed: 20,
            evasion: 0.0f, critChance: 0.0f, critModifier: 0.0f);

    // ---------------------------------------------------------------
    // Applying the per-round delta
    // ---------------------------------------------------------------

    [Fact]
    public void Regen_Heals_ByRegenDrainHpPct_AtRoundStart()
    {
        // What: verifies a positive Hp regen/drain heals MaxHp * RegenDrainHpPct once
        //       ProcessRegensDrains runs, with no elemental component and no Defense mitigation.
        // How:  MaxHp 100 * 0.10 = 10. An entity damaged down to 50 gets a Regen and one round
        //       start; it must land at exactly 60.
        CombatEventBus.Reset();

        var entity = MakeEntity(maxHp: 100, hp: 50);
        entity.AddRegenDrain(RegenDrainStat.Hp, isPositive: true, roundsRemaining: 2, untilRemoved: false, "test", "Test");

        entity.ProcessRegensDrains();

        Assert.Equal(60, entity.Hp);
    }

    [Fact]
    public void Drain_Damages_ByRegenDrainHpPct_AtRoundStart()
    {
        // What: verifies a negative Hp regen/drain deals MaxHp * RegenDrainHpPct as flat damage.
        // How:  a full-health 100-MaxHp entity takes one round of Drain and must read 90.
        CombatEventBus.Reset();

        var entity = MakeEntity(maxHp: 100, hp: 100);
        entity.AddRegenDrain(RegenDrainStat.Hp, isPositive: false, roundsRemaining: 2, untilRemoved: false, "test", "Test");

        entity.ProcessRegensDrains();

        Assert.Equal(90, entity.Hp);
    }

    [Fact]
    public void Regen_Restores_Tp_ByRegenDrainTpPct_AtRoundStart()
    {
        // What: verifies a positive Tp regen/drain restores MaxTp * RegenDrainTpPct.
        // How:  MaxTp 50 * 0.10 = 5. An entity at 20 Tp gets a Regen and one round start; it must
        //       read 25.
        CombatEventBus.Reset();

        var entity = MakeEntity(maxTp: 50, tp: 20);
        entity.AddRegenDrain(RegenDrainStat.Tp, isPositive: true, roundsRemaining: 2, untilRemoved: false, "test", "Test");

        entity.ProcessRegensDrains();

        Assert.Equal(25, entity.Tp);
    }

    [Fact]
    public void Drain_Spends_Tp_ByRegenDrainTpPct_AtRoundStart()
    {
        CombatEventBus.Reset();

        var entity = MakeEntity(maxTp: 50, tp: 20);
        entity.AddRegenDrain(RegenDrainStat.Tp, isPositive: false, roundsRemaining: 2, untilRemoved: false, "test", "Test");

        entity.ProcessRegensDrains();

        Assert.Equal(15, entity.Tp);
    }

    [Fact]
    public void TpRegenOrDrain_OnZeroMaxTpEntity_IsANoOp()
    {
        // What: verifies a Tp regen/drain on a monster (MaxTp is always 0 for enemies) computes a
        //       zero delta and never raises a change - it must not, say, push Tp negative.
        CombatEventBus.Reset();

        bool raised = false;
        CombatEventBus.EntityTpChanged += (_, _, _, _, _, _) => raised = true;

        var entity = MakeEntity(maxTp: 0, tp: 0);
        entity.AddRegenDrain(RegenDrainStat.Tp, isPositive: true, roundsRemaining: 2, untilRemoved: false, "test", "Test");

        entity.ProcessRegensDrains();

        Assert.Equal(0, entity.Tp);
        Assert.False(raised);
    }

    [Fact]
    public void RestoreTp_ClampsAtMaxTp()
    {
        // What: verifies RestoreTp mirrors Heal's clamp-at-max behavior - Tp can't be restored past
        //       MaxTp even when the amount would overshoot it.
        CombatEventBus.Reset();

        var entity = MakeEntity(maxTp: 50, tp: 48);
        entity.RestoreTp(10, "test", "Test");

        Assert.Equal(50, entity.Tp);
    }

    [Fact]
    public void SpendTp_ClampsAtZero()
    {
        // What: verifies SpendTp now floors at 0 rather than going negative, so a Tp Drain that
        //       outsizes the remaining pool can't leave Tp in an invalid state.
        CombatEventBus.Reset();

        var entity = MakeEntity(maxTp: 50, tp: 3);
        entity.SpendTp(10, "test", "Test");

        Assert.Equal(0, entity.Tp);
    }

    [Fact]
    public void AddRegenDrain_RaisesRegenDrainApplied_WithRoundsAndUntilRemoved()
    {
        // What: verifies applying a regen/drain tells the UI which resource, in which direction,
        //       and for how long - there is no oldValue/newValue here, unlike BuffDebuffApplied,
        //       since applying an entry doesn't move the resource by itself.
        CombatEventBus.Reset();

        (RegenDrainStat Stat, bool IsPositive, int Rounds, bool UntilRemoved)? applied = null;
        CombatEventBus.RegenDrainApplied += (_, _, stat, isPositive, rounds, untilRemoved, _, _) =>
            applied = (stat, isPositive, rounds, untilRemoved);

        MakeEntity().AddRegenDrain(RegenDrainStat.Hp, isPositive: true, roundsRemaining: 3, untilRemoved: false, "test", "Test");

        Assert.Equal((RegenDrainStat.Hp, true, 3, false), applied);
    }

    [Fact]
    public void AddRegenDrain_WithNonPositiveRounds_IsIgnored()
    {
        // What: verifies a zero/negative duration is a silent no-op, matching AddBuffDebuff.
        CombatEventBus.Reset();

        bool raised = false;
        CombatEventBus.RegenDrainApplied += (_, _, _, _, _, _, _, _) => raised = true;

        var entity = MakeEntity(maxHp: 100, hp: 100);
        entity.AddRegenDrain(RegenDrainStat.Hp, isPositive: false, roundsRemaining: 0, untilRemoved: false, "test", "Test");

        entity.ProcessRegensDrains();

        Assert.Equal(100, entity.Hp);
        Assert.False(raised);
    }

    [Fact]
    public void AddRegenDrain_WithUntilRemovedTrue_AppliesEvenWithZeroRounds()
    {
        // What: verifies the non-positive-rounds no-op guard is specifically about timed rounds -
        //       an UntilRemoved entry always applies regardless of what Rounds was authored as.
        CombatEventBus.Reset();

        bool raised = false;
        CombatEventBus.RegenDrainApplied += (_, _, _, _, _, _, _, _) => raised = true;

        var entity = MakeEntity(maxHp: 100, hp: 100);
        entity.AddRegenDrain(RegenDrainStat.Hp, isPositive: false, roundsRemaining: 0, untilRemoved: true, "test", "Test");

        entity.ProcessRegensDrains();

        Assert.Equal(90, entity.Hp);
        Assert.True(raised);
    }

    // ---------------------------------------------------------------
    // Re-stacking
    // ---------------------------------------------------------------

    [Fact]
    public void SamePolarity_RefreshesDuration_WithoutStackingTheAmount()
    {
        // What: verifies re-applying the same polarity adds to the rounds remaining but a resource
        //       still only takes ONE regen/drain's worth of damage/healing per round - a resource
        //       holds at most one entry, exactly like a stat holds at most one buff.
        // How:  two -Hp drains of 2 and 3 rounds are applied. The reported roundsRemaining must be
        //       5, and a single round start must deal exactly one 10-Hp hit, not two.
        CombatEventBus.Reset();

        int lastRounds = 0;
        CombatEventBus.RegenDrainApplied += (_, _, _, _, rounds, _, _, _) => lastRounds = rounds;

        var entity = MakeEntity(maxHp: 100, hp: 100);
        entity.AddRegenDrain(RegenDrainStat.Hp, isPositive: false, roundsRemaining: 2, untilRemoved: false, "test", "Test");
        entity.AddRegenDrain(RegenDrainStat.Hp, isPositive: false, roundsRemaining: 3, untilRemoved: false, "test", "Test");

        entity.ProcessRegensDrains();

        Assert.Equal(5, lastRounds);
        Assert.Equal(90, entity.Hp);
    }

    [Fact]
    public void OppositePolarity_CancelsTheExistingEntry_AndNeitherApplies()
    {
        // What: verifies a Drain landing on an already-Regen'd resource annihilates the entry
        //       instead of stacking or replacing it - neither side ever gets a chance to apply.
        CombatEventBus.Reset();

        int appliedCount = 0;
        bool? expiredIsPositive = null;
        CombatEventBus.RegenDrainApplied += (_, _, _, _, _, _, _, _) => appliedCount++;
        CombatEventBus.RegenDrainExpired += (_, _, _, isPositive, _, _, _, _) => expiredIsPositive = isPositive;

        var entity = MakeEntity(maxHp: 100, hp: 100);
        entity.AddRegenDrain(RegenDrainStat.Hp, isPositive: true,  roundsRemaining: 2, untilRemoved: false, "test", "Test");
        entity.AddRegenDrain(RegenDrainStat.Hp, isPositive: false, roundsRemaining: 9, untilRemoved: false, "test", "Test");

        entity.ProcessRegensDrains();

        Assert.Equal(100, entity.Hp);
        Assert.Equal(1, appliedCount);
        Assert.Equal(true, expiredIsPositive);
    }

    [Fact]
    public void UntilRemoved_IsCancelledByOppositePolarity()
    {
        CombatEventBus.Reset();

        bool? expiredIsPositive = null;
        CombatEventBus.RegenDrainExpired += (_, _, _, isPositive, _, _, _, _) => expiredIsPositive = isPositive;

        var entity = MakeEntity(maxHp: 100, hp: 100);
        entity.AddRegenDrain(RegenDrainStat.Hp, isPositive: true,  roundsRemaining: 2, untilRemoved: true, "test", "Test");
        entity.AddRegenDrain(RegenDrainStat.Hp, isPositive: false, roundsRemaining: 9, untilRemoved: false, "test", "Test");

        entity.ProcessRegensDrains();

        Assert.Equal(100, entity.Hp);
        Assert.Equal(true, expiredIsPositive);
    }

    [Fact]
    public void SamePolarity_Merge_UntilRemovedIsSticky()
    {
        // What: verifies a timed entry refreshed by an UntilRemoved one of the same polarity ends
        //       up UntilRemoved, but keeps applying every round rather than merely never expiring.
        CombatEventBus.Reset();

        var entity = MakeEntity(maxHp: 100, hp: 100);
        entity.AddRegenDrain(RegenDrainStat.Hp, isPositive: false, roundsRemaining: 2, untilRemoved: false, "test", "Test");
        entity.AddRegenDrain(RegenDrainStat.Hp, isPositive: false, roundsRemaining: 1, untilRemoved: true, "test", "Test");

        bool tickedOrExpired = false;
        CombatEventBus.RegenDrainTicked  += (_, _, _, _, _, _, _) => tickedOrExpired = true;
        CombatEventBus.RegenDrainExpired += (_, _, _, _, _, _, _, _) => tickedOrExpired = true;

        for (int i = 0; i < 5; i++) entity.ProcessRegensDrains();

        Assert.False(tickedOrExpired);
        Assert.Equal(50, entity.Hp); // 5 rounds of a single 10-Hp hit each: 100 -> 90 -> 80 -> 70 -> 60 -> 50
    }

    // ---------------------------------------------------------------
    // Ticking down / the round-start ordering
    // ---------------------------------------------------------------

    [Fact]
    public void OneRoundDrain_AppliesExactlyOnce_ThenExpires()
    {
        // What: pins down the round-start ordering decision - ApplyRegensDrains must land before
        //       TickRegensDrains removes the entry, so a rounds:1 entry deals its damage exactly
        //       once instead of expiring before it gets a turn.
        CombatEventBus.Reset();

        var expired = new List<bool>();
        CombatEventBus.RegenDrainExpired += (_, _, _, isPositive, _, _, _, _) => expired.Add(isPositive);

        var entity = MakeEntity(maxHp: 100, hp: 100);
        entity.AddRegenDrain(RegenDrainStat.Hp, isPositive: false, roundsRemaining: 1, untilRemoved: false, "test", "Test");

        entity.ProcessRegensDrains();
        Assert.Equal(90, entity.Hp);
        Assert.Equal([false], expired);

        entity.ProcessRegensDrains();
        Assert.Equal(90, entity.Hp); // gone - no further damage
        Assert.Equal([false], expired);
    }

    [Fact]
    public void TickRegensDrains_CountsDownThenExpires()
    {
        CombatEventBus.Reset();

        var ticked  = new List<int>();
        var expired = new List<bool>();
        CombatEventBus.RegenDrainTicked  += (_, _, _, _, rounds, _, _) => ticked.Add(rounds);
        CombatEventBus.RegenDrainExpired += (_, _, _, isPositive, _, _, _, _) => expired.Add(isPositive);

        var entity = MakeEntity(maxHp: 100, hp: 100);
        entity.AddRegenDrain(RegenDrainStat.Hp, isPositive: false, roundsRemaining: 2, untilRemoved: false, "test", "Test");

        entity.ProcessRegensDrains();
        Assert.Equal(90, entity.Hp);
        Assert.Equal([1], ticked);
        Assert.Empty(expired);

        entity.ProcessRegensDrains();
        Assert.Equal(80, entity.Hp);
        Assert.Equal([1], ticked);
        Assert.Equal([false], expired);

        entity.ProcessRegensDrains();
        Assert.Equal(80, entity.Hp); // expired - no more damage
    }

    [Fact]
    public void UntilRemoved_NeverTicksOrExpires_ButKeepsApplyingEachRound()
    {
        CombatEventBus.Reset();

        var ticked  = new List<int>();
        var expired = new List<bool>();
        CombatEventBus.RegenDrainTicked  += (_, _, _, _, rounds, _, _) => ticked.Add(rounds);
        CombatEventBus.RegenDrainExpired += (_, _, _, isPositive, _, _, _, _) => expired.Add(isPositive);

        var entity = MakeEntity(maxHp: 1000, hp: 100);
        entity.AddRegenDrain(RegenDrainStat.Hp, isPositive: true, roundsRemaining: 1, untilRemoved: true, "test", "Test");

        for (int i = 0; i < 5; i++) entity.ProcessRegensDrains();

        Assert.Equal(600, entity.Hp); // 5 rounds of +100 (10% of 1000) each, never expiring
        Assert.Empty(ticked);
        Assert.Empty(expired);
    }

    // ---------------------------------------------------------------
    // Lethality and dead-target handling
    // ---------------------------------------------------------------

    [Fact]
    public void HpDrain_CanReduceHpToZero_AndRaiseEntityDeath()
    {
        // What: verifies Hp Drain routes through the normal TakeDamage path, so it can be the
        //       killing blow and death handling (HandleDefeat/EntityDeath) fires exactly like any
        //       other damage source.
        CombatEventBus.Reset();

        bool died = false;
        CombatEventBus.EntityDeath += (_, _, _, _) => died = true;

        var entity = MakeEntity(maxHp: 100, hp: 5);
        entity.AddRegenDrain(RegenDrainStat.Hp, isPositive: false, roundsRemaining: 1, untilRemoved: false, "test", "Test");

        entity.ProcessRegensDrains();

        Assert.Equal(0, entity.Hp);
        Assert.True(entity.IsDead);
        Assert.True(died);
    }

    [Fact]
    public void ApplyRegensDrains_OnADeadEntity_IsANoOp()
    {
        // What: verifies ProcessRegensDrains never revives or further affects an already-dead
        //       entity, even when it carries an active regen/drain entry.
        CombatEventBus.Reset();

        var entity = MakeEntity(maxHp: 100, hp: 5);
        entity.AddRegenDrain(RegenDrainStat.Hp, isPositive: false, roundsRemaining: 1, untilRemoved: false, "test", "Test");
        entity.ProcessRegensDrains(); // kills it
        Assert.True(entity.IsDead);

        entity.AddRegenDrain(RegenDrainStat.Hp, isPositive: true, roundsRemaining: 1, untilRemoved: true, "test", "Test");

        bool healed = false;
        CombatEventBus.EntityHealed += (_, _, _, _, _, _, _, _, _) => healed = true;

        entity.ProcessRegensDrains();

        Assert.Equal(0, entity.Hp);
        Assert.False(healed);
    }

    // ---------------------------------------------------------------
    // Reaching it from game data, through a CombatFunction
    // ---------------------------------------------------------------

    // Mirrors BuffDebuffTests.SetupCombat: one ally, one durable enemy, the ally spends its opening
    // move then finishes the enemy off with a FixedAmount blow so the fight terminates.
    private static (CombatEngineClass engine, CombatEntity ally, CombatEntity enemy) SetupCombat(
        CombatCommand openingMove, float enemyEvasion = 0.0f)
    {
        var engine = new CombatEngineClass(new Random(0));

        var ally = new CombatEntity(
            entityId: "ally", name: "Ally", level: 1,
            maxHp: 100, hp: 100, maxTp: 50, tp: 50,
            power: 20, defense: 0, speed: 10,
            evasion: 0.0f, critChance: 0.0f, critModifier: 0.0f);

        var enemy = new CombatEntity(
            entityId: "enemy", name: "Enemy", level: 1,
            maxHp: 1000, hp: 1000, maxTp: 0, tp: 0,
            power: 0, defense: 20, speed: 5,
            evasion: enemyEvasion, critChance: 0.0f, critModifier: 0.0f);

        engine.InitCombat(allies: [ally], enemies: [enemy]);

        // Wire AFTER InitCombat, which calls CombatEventBus.Reset().
        bool openingMoveUsed = false;
        CombatEventBus.WaitingForTurn += (entityId, _, _, isAlly) =>
        {
            if (!isAlly)
            {
                engine.SubmitCommand(new CombatCommand
                {
                    ActorId        = entityId,
                    TargetingType  = TargetingType.Self,
                    ValidTargets   = ValidTarget.Allies,
                    LivingOrDead   = LivingOrDead.Living,
                    CombatFunction = NoOpFunction.FunctionName,
                });
                return;
            }

            if (!openingMoveUsed)
            {
                openingMoveUsed = true;
                engine.SubmitCommand(openingMove);
                return;
            }

            engine.SubmitCommand(new CombatCommand
            {
                ActorId        = entityId,
                TargetingType  = TargetingType.Random,
                ValidTargets   = ValidTarget.Enemies,
                LivingOrDead   = LivingOrDead.Living,
                CombatFunction = BasicDamageFunction.FunctionName,
                Parameters     = new CombatFunctionParameters { CalcType = DamageOrHealCalcType.FixedAmount, PowerFactor = 100_000 },
            });
        };

        return (engine, ally, enemy);
    }

    [Fact]
    public void BasicDamage_HpDrain_AppliesOncePerTarget()
    {
        // What: verifies a regensDrains entry on a multi-hit damage action lands exactly once no
        //       matter how many times the action strikes the same target - it's a property of the
        //       action, applied once after the whole action resolves, not interleaved with the hits.
        var opening = new CombatCommand
        {
            ActorId                        = "ally",
            TargetingType                  = TargetingType.Random,
            ValidTargets                   = ValidTarget.Enemies,
            LivingOrDead                   = LivingOrDead.Living,
            NumAttacks                     = 3,
            AllowMultipleAttackOnSameTarget = true,
            CombatFunction                 = BasicDamageFunction.FunctionName,
            Parameters                     = new CombatFunctionParameters
            {
                PowerFactor  = 1.0,
                RegensDrains =
                [
                    new RegenDrainSpec { Stat = RegenDrainStat.Hp, Type = RegenDrainType.Negative, Target = BuffDebuffTarget.SelectedTargets, Rounds = 2, UntilRemoved = false },
                ],
            },
        };

        var (engine, _, _) = SetupCombat(opening);

        var applied = new List<int>();
        CombatEventBus.RegenDrainApplied += (_, _, _, _, rounds, _, _, _) => applied.Add(rounds);

        engine.BeginCombat();

        Assert.Equal([2], applied);
    }

    [Fact]
    public void BasicHeal_CanCarryARegen_ToItsTarget()
    {
        // What: verifies the rider works on the healing function too, not just on damage.
        var opening = new CombatCommand
        {
            ActorId        = "ally",
            TargetingType  = TargetingType.Self,
            ValidTargets   = ValidTarget.Allies,
            LivingOrDead   = LivingOrDead.Living,
            CombatFunction = BasicHealFunction.FunctionName,
            Parameters     = new CombatFunctionParameters
            {
                PowerFactor  = 1.0,
                RegensDrains =
                [
                    new RegenDrainSpec { Stat = RegenDrainStat.Hp, Type = RegenDrainType.Positive, Target = BuffDebuffTarget.SelectedTargets, Rounds = 2, UntilRemoved = false },
                ],
            },
        };

        var (engine, ally, _) = SetupCombat(opening);

        bool applied = false;
        CombatEventBus.RegenDrainApplied += (entityId, _, stat, isPositive, _, _, _, _) =>
        {
            if (entityId == "ally" && stat == RegenDrainStat.Hp && isPositive) applied = true;
        };

        engine.BeginCombat();

        Assert.True(applied);
    }

    [Fact]
    public void CollidingRegenDrainEntries_Throw_NamingTheAction()
    {
        // What: verifies two entries that resolve to the same (entity, stat) pair are a data error,
        //       caught even though the JSON Schema's uniqueBy can only reject identical
        //       (stat, target) pairs.
        var opening = new CombatCommand
        {
            ActorId        = "ally",
            SourceId       = "broken_tech",
            TargetingType  = TargetingType.Self,
            ValidTargets   = ValidTarget.Allies,
            LivingOrDead   = LivingOrDead.Living,
            CombatFunction = BasicHealFunction.FunctionName,
            Parameters     = new CombatFunctionParameters
            {
                PowerFactor  = 1.0,
                RegensDrains =
                [
                    new RegenDrainSpec { Stat = RegenDrainStat.Hp, Type = RegenDrainType.Positive, Target = BuffDebuffTarget.Self,      Rounds = 2, UntilRemoved = false },
                    new RegenDrainSpec { Stat = RegenDrainStat.Hp, Type = RegenDrainType.Positive, Target = BuffDebuffTarget.AllAllies, Rounds = 2, UntilRemoved = false },
                ],
            },
        };

        var (engine, _, _) = SetupCombat(opening);

        var ex = Assert.Throws<InvalidOperationException>(() => engine.BeginCombat());
        Assert.Contains("broken_tech", ex.Message);
    }
}
