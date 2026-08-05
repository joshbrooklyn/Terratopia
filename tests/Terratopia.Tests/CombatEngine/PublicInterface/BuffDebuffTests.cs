using CombatEngine;
using CombatEngine.CombatFunctions;
using CombatEngine.DataClasses;
using CombatEngine.Engine;
using CombatEngine.Enums;

namespace Terratopia.Tests.CombatEngine.PublicInterface;

[Collection("CombatEngineSerial")]
public class BuffDebuffTests
{
    // Every buff moves its stat by CombatBalance.Current.TimedBuffPct, which defaults to 0.35 when
    // Configure was never called — the arrangement the CombatBalance header comment guarantees for
    // tests. The stat tests below pick base values that avoid a .5 rounding midpoint, so the
    // expected numbers are unambiguous regardless of the rounding mode.
    private const double BuffPct = 0.35;

    private static CombatEntity MakeEntity(int power = 20, int defense = 20, int speed = 20) =>
        new(entityId: "e", name: "Entity", level: 1,
            maxHp: 100, hp: 100, maxTp: 0, tp: 0,
            power: power, defense: defense, speed: speed,
            evasion: 0.0f, critChance: 0.0f, critModifier: 0.0f);

    // ---------------------------------------------------------------
    // The stat getters
    // ---------------------------------------------------------------

    [Theory]
    [InlineData(BuffDebuffStat.Power)]
    [InlineData(BuffDebuffStat.Defense)]
    [InlineData(BuffDebuffStat.Speed)]
    public void Buff_RaisesStat_AndDebuff_LowersIt_ByTimedBuffPct(BuffDebuffStat stat)
    {
        // What: verifies each of the three buffable stats reads back through the buff modifier,
        //       up by TimedBuffPct when positive and down by the same fraction when negative.
        // How:  a fresh entity with every stat at 20 gets one buff, then (on a second entity) one
        //       debuff, on the stat under test. 20 * 1.35 = 27 and 20 * 0.65 = 13, neither of
        //       which is a rounding midpoint. Asserting all three stats each time also proves the
        //       modifier is scoped to its own stat and doesn't leak onto the other two.
        CombatEventBus.Reset();

        var buffed = MakeEntity();
        buffed.AddBuffDebuff(stat, isPositive: true, roundsRemaining: 2, untilRemoved: false, "test", "Test");

        var debuffed = MakeEntity();
        debuffed.AddBuffDebuff(stat, isPositive: false, roundsRemaining: 2, untilRemoved: false, "test", "Test");

        Assert.Equal(stat == BuffDebuffStat.Power   ? 27 : 20, buffed.Power);
        Assert.Equal(stat == BuffDebuffStat.Defense ? 27 : 20, buffed.Defense);
        Assert.Equal(stat == BuffDebuffStat.Speed   ? 27 : 20, buffed.Speed);

        Assert.Equal(stat == BuffDebuffStat.Power   ? 13 : 20, debuffed.Power);
        Assert.Equal(stat == BuffDebuffStat.Defense ? 13 : 20, debuffed.Defense);
        Assert.Equal(stat == BuffDebuffStat.Speed   ? 13 : 20, debuffed.Speed);
    }

    [Fact]
    public void Buff_RaisesBuffDebuffApplied_WithBeforeAndAfterValues()
    {
        // What: verifies applying a buff tells the UI which stat moved, in which direction, for how
        //       long, and what the stat read before and after — everything a HUD needs without
        //       having to query the entity back.
        // How:  a single Power buff on a Power=20 entity should report oldValue 20, newValue 27.
        CombatEventBus.Reset();

        (BuffDebuffStat Stat, bool IsPositive, int Rounds, bool UntilRemoved, int Old, int New)? applied = null;
        CombatEventBus.BuffDebuffApplied += (_, _, stat, isPositive, rounds, untilRemoved, oldValue, newValue, _, _) =>
            applied = (stat, isPositive, rounds, untilRemoved, oldValue, newValue);

        MakeEntity().AddBuffDebuff(BuffDebuffStat.Power, isPositive: true, roundsRemaining: 3, untilRemoved: false, "test", "Test");

        Assert.Equal((BuffDebuffStat.Power, true, 3, false, 20, 27), applied);
    }

    [Fact]
    public void AddBuffDebuff_WithNonPositiveRounds_IsIgnored()
    {
        // What: verifies a zero/negative duration is a silent no-op rather than an entry that
        //       expires on its first tick, matching how SpendTp and Heal treat their no-op inputs.
        // How:  a 0-round buff is applied; the stat must be untouched and no event raised.
        CombatEventBus.Reset();

        bool raised = false;
        CombatEventBus.BuffDebuffApplied += (_, _, _, _, _, _, _, _, _, _) => raised = true;

        var entity = MakeEntity();
        entity.AddBuffDebuff(BuffDebuffStat.Power, isPositive: true, roundsRemaining: 0, untilRemoved: false, "test", "Test");

        Assert.Equal(20, entity.Power);
        Assert.False(raised);
    }

    [Fact]
    public void AddBuffDebuff_WithUntilRemovedTrue_AppliesEvenWithZeroRounds()
    {
        // What: verifies the non-positive-rounds no-op guard is specifically about *timed* rounds -
        //       an UntilRemoved entry always applies regardless of what Rounds was authored as,
        //       since Rounds is irrelevant once the entry never expires from the round clock.
        // How:  the same roundsRemaining: 0 input that's a no-op above must apply when
        //       untilRemoved: true - the stat moves and BuffDebuffApplied fires.
        CombatEventBus.Reset();

        bool raised = false;
        CombatEventBus.BuffDebuffApplied += (_, _, _, _, _, _, _, _, _, _) => raised = true;

        var entity = MakeEntity();
        entity.AddBuffDebuff(BuffDebuffStat.Power, isPositive: true, roundsRemaining: 0, untilRemoved: true, "test", "Test");

        Assert.Equal(27, entity.Power);
        Assert.True(raised);
    }

    // ---------------------------------------------------------------
    // Re-stacking
    // ---------------------------------------------------------------

    [Fact]
    public void SamePolarity_RefreshesDuration_WithoutCompoundingMagnitude()
    {
        // What: verifies re-applying the same polarity adds to the rounds remaining but leaves the
        //       magnitude alone — a stat holds at most one buff, so two Power buffs last longer,
        //       they do not hit harder.
        // How:  two +Power buffs of 2 and 3 rounds are applied. The reported roundsRemaining must
        //       be 5 and the stat must still read the single-buff value of 27, not 20 * 1.35².
        CombatEventBus.Reset();

        int lastRounds = 0;
        CombatEventBus.BuffDebuffApplied += (_, _, _, _, rounds, _, _, _, _, _) => lastRounds = rounds;

        var entity = MakeEntity();
        entity.AddBuffDebuff(BuffDebuffStat.Power, isPositive: true, roundsRemaining: 2, untilRemoved: false, "test", "Test");
        entity.AddBuffDebuff(BuffDebuffStat.Power, isPositive: true, roundsRemaining: 3, untilRemoved: false, "test", "Test");

        Assert.Equal(5, lastRounds);
        Assert.Equal(27, entity.Power);
    }

    [Fact]
    public void OppositePolarity_CancelsTheExistingEntry_AndRaisesExpired()
    {
        // What: verifies a debuff landing on an already-buffed stat annihilates the buff instead of
        //       replacing it or extending it — neither side survives and the stat returns to base.
        // How:  a +Power buff is followed by a -Power debuff. The stat must read 20 again, and the
        //       event raised must be BuffDebuffExpired (reporting the polarity of the entry that
        //       was removed, not the incoming one), never a second BuffDebuffApplied.
        CombatEventBus.Reset();

        int appliedCount = 0;
        (bool IsPositive, int Old, int New)? expired = null;
        CombatEventBus.BuffDebuffApplied += (_, _, _, _, _, _, _, _, _, _) => appliedCount++;
        CombatEventBus.BuffDebuffExpired += (_, _, _, isPositive, oldValue, newValue, _, _, _, _) =>
            expired = (isPositive, oldValue, newValue);

        var entity = MakeEntity();
        entity.AddBuffDebuff(BuffDebuffStat.Power, isPositive: true,  roundsRemaining: 2, untilRemoved: false, "test", "Test");
        entity.AddBuffDebuff(BuffDebuffStat.Power, isPositive: false, roundsRemaining: 9, untilRemoved: false, "test", "Test");

        Assert.Equal(20, entity.Power);
        Assert.Equal(1, appliedCount);
        Assert.Equal((true, 27, 20), expired);
    }

    [Fact]
    public void UntilRemoved_IsCancelledByOppositePolarity()
    {
        // What: verifies cancellation doesn't special-case duration - an UntilRemoved entry is
        //       removed by an opposite-polarity application exactly like a timed one would be.
        // How:  an UntilRemoved +Power buff is followed by a -Power debuff. The stat must read 20
        //       again and BuffDebuffExpired must report the removed entry's original polarity.
        CombatEventBus.Reset();

        (bool IsPositive, int Old, int New)? expired = null;
        CombatEventBus.BuffDebuffExpired += (_, _, _, isPositive, oldValue, newValue, _, _, _, _) =>
            expired = (isPositive, oldValue, newValue);

        var entity = MakeEntity();
        entity.AddBuffDebuff(BuffDebuffStat.Power, isPositive: true,  roundsRemaining: 2, untilRemoved: true, "test", "Test");
        entity.AddBuffDebuff(BuffDebuffStat.Power, isPositive: false, roundsRemaining: 9, untilRemoved: false, "test", "Test");

        Assert.Equal(20, entity.Power);
        Assert.Equal((true, 27, 20), expired);
    }

    [Fact]
    public void SamePolarity_Merge_UntilRemovedIsSticky_TimedThenUntilRemoved()
    {
        // What: verifies a timed buff refreshed by an UntilRemoved one of the same polarity ends
        //       up UntilRemoved rather than just getting a longer, still-finite duration.
        // How:  a +Power buff (2 rounds) is refreshed by an UntilRemoved +Power application. Ticking
        //       several times afterward must never expire or tick it.
        CombatEventBus.Reset();

        var entity = MakeEntity();
        entity.AddBuffDebuff(BuffDebuffStat.Power, isPositive: true, roundsRemaining: 2, untilRemoved: false, "test", "Test");
        entity.AddBuffDebuff(BuffDebuffStat.Power, isPositive: true, roundsRemaining: 1, untilRemoved: true, "test", "Test");

        bool tickedOrExpired = false;
        CombatEventBus.BuffDebuffTicked  += (_, _, _, _, _, _, _) => tickedOrExpired = true;
        CombatEventBus.BuffDebuffExpired += (_, _, _, _, _, _, _, _, _, _) => tickedOrExpired = true;

        for (int i = 0; i < 5; i++) entity.TickBuffDebuffs();

        Assert.Equal(27, entity.Power);
        Assert.False(tickedOrExpired);
    }

    [Fact]
    public void SamePolarity_Merge_UntilRemovedIsSticky_UntilRemovedThenTimed()
    {
        // What: verifies an UntilRemoved buff refreshed by a timed one of the same polarity stays
        //       UntilRemoved rather than being shortened back down to the incoming rounds.
        // How:  an UntilRemoved +Power buff is refreshed by a 3-round +Power application. Ticking
        //       several times afterward must never expire or tick it.
        CombatEventBus.Reset();

        var entity = MakeEntity();
        entity.AddBuffDebuff(BuffDebuffStat.Power, isPositive: true, roundsRemaining: 1, untilRemoved: true, "test", "Test");
        entity.AddBuffDebuff(BuffDebuffStat.Power, isPositive: true, roundsRemaining: 3, untilRemoved: false, "test", "Test");

        bool tickedOrExpired = false;
        CombatEventBus.BuffDebuffTicked  += (_, _, _, _, _, _, _) => tickedOrExpired = true;
        CombatEventBus.BuffDebuffExpired += (_, _, _, _, _, _, _, _, _, _) => tickedOrExpired = true;

        for (int i = 0; i < 5; i++) entity.TickBuffDebuffs();

        Assert.Equal(27, entity.Power);
        Assert.False(tickedOrExpired);
    }

    // ---------------------------------------------------------------
    // Ticking down
    // ---------------------------------------------------------------

    [Fact]
    public void TickBuffDebuffs_CountsDownThenExpires_RestoringTheBaseStat()
    {
        // What: verifies a 2-round buff survives one tick, reports the countdown, then expires on
        //       the second tick and hands the stat back at its base value.
        // How:  a +Power buff with rounds=2 is ticked twice. The first tick must raise
        //       BuffDebuffTicked with 1 round left and keep Power at 27; the second must raise
        //       BuffDebuffExpired with 27 → 20 and put Power back to 20. A third tick proves the
        //       entry is really gone rather than sitting at a non-positive count.
        CombatEventBus.Reset();

        var ticked  = new List<int>();
        var expired = new List<(int Old, int New)>();
        CombatEventBus.BuffDebuffTicked  += (_, _, _, _, rounds, _, _) => ticked.Add(rounds);
        CombatEventBus.BuffDebuffExpired += (_, _, _, _, oldValue, newValue, _, _, _, _) => expired.Add((oldValue, newValue));

        var entity = MakeEntity();
        entity.AddBuffDebuff(BuffDebuffStat.Power, isPositive: true, roundsRemaining: 2, untilRemoved: false, "test", "Test");

        entity.TickBuffDebuffs();
        Assert.Equal(27, entity.Power);
        Assert.Equal([1], ticked);
        Assert.Empty(expired);

        entity.TickBuffDebuffs();
        Assert.Equal(20, entity.Power);
        Assert.Equal([1], ticked);
        Assert.Equal([(27, 20)], expired);

        entity.TickBuffDebuffs();
        Assert.Equal([(27, 20)], expired);
    }

    [Fact]
    public void UntilRemoved_NeverTicksOrExpires_AcrossManyRounds()
    {
        // What: verifies the round clock has nothing to do with an UntilRemoved entry - it's not
        //       just "a very long duration," it's genuinely exempt from ticking.
        // How:  an UntilRemoved +Power buff is ticked several times in a row. The stat never moves
        //       off 27 and neither BuffDebuffTicked nor BuffDebuffExpired fires at any point.
        CombatEventBus.Reset();

        var ticked  = new List<int>();
        var expired = new List<(int Old, int New)>();
        CombatEventBus.BuffDebuffTicked  += (_, _, _, _, rounds, _, _) => ticked.Add(rounds);
        CombatEventBus.BuffDebuffExpired += (_, _, _, _, oldValue, newValue, _, _, _, _) => expired.Add((oldValue, newValue));

        var entity = MakeEntity();
        entity.AddBuffDebuff(BuffDebuffStat.Power, isPositive: true, roundsRemaining: 1, untilRemoved: true, "test", "Test");

        for (int i = 0; i < 5; i++) entity.TickBuffDebuffs();

        Assert.Equal(27, entity.Power);
        Assert.Empty(ticked);
        Assert.Empty(expired);
    }

    // ---------------------------------------------------------------
    // Reaching it from game data, through a CombatFunction
    // ---------------------------------------------------------------

    // One ally and one durable enemy. The ally spends its first turn on `openingMove`, then
    // finishes the enemy off with a FixedAmount blow so the fight terminates; the enemy always
    // passes. Ally is Power=20/Level=1, so a StandardFormula hit has base damage
    // (20 * 1.0 * 2.0) + (1 * 5.0) = 45 before the target's Defense is applied.
    //
    // enemyEvasion defaults to 0 (every hit lands). Setting it to 1.0f guarantees the very first
    // roll against the enemy evades - Random.NextSingle() never returns exactly 1.0, so
    // `roll >= 1.0` can never be true - without pinning down how many further turns it takes the
    // fight to end, since TryEvade decays evasion 25% per dodge until a hit eventually lands.
    private static (CombatEngineClass engine, CombatEntity ally, CombatEntity enemy) SetupCombat(
        CombatCommand openingMove, int enemyDefense = 20, float enemyEvasion = 0.0f)
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
            power: 0, defense: enemyDefense, speed: 5,
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
    public void BasicDamage_DefenseDebuff_AppliesOncePerTarget()
    {
        // What: verifies a debuff on a multi-hit damage action lands exactly once no matter how
        //       many times the action strikes the same target, and that it never influences the
        //       action's own damage - buffsDebuffs entries apply once, after the whole action
        //       resolves, not interleaved with the hits.
        // How:  a 3-hit BasicDamage with allowMultipleAttackOnSameTarget hits the one enemy three
        //       times, carrying a Defense debuff. Base damage is 45 and the enemy's Defense is 20,
        //       so the formula 45 / ((D + 128) / 128) - D / 2 gives 28 for every hit - the debuff
        //       lands only after hit three, so it never gets a chance to lower hits one-three.
        //       Exactly one BuffDebuffApplied must be raised — without de-duplicating the resolved
        //       target the duration would be tripled to 6 rounds.
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
                BuffsDebuffs =
                [
                    new BuffDebuffSpec { Stat = BuffDebuffStat.Defense, Type = BuffDebuffType.Negative, Target = BuffDebuffTarget.SelectedTargets, Rounds = 2, UntilRemoved = false },
                ],
            },
        };

        var (engine, _, _) = SetupCombat(opening);

        var damages = new List<int>();
        var applied = new List<int>();
        CombatEventBus.EntityDamaged     += (targetId, _, amount, _, _, _, _, _, _, _) =>
        {
            if (targetId == "enemy" && damages.Count < 3) damages.Add(amount);
        };
        CombatEventBus.BuffDebuffApplied += (_, _, _, _, rounds, _, _, _, _, _) => applied.Add(rounds);

        engine.BeginCombat();

        Assert.Equal([28, 28, 28], damages);
        Assert.Equal([2], applied);
    }

    [Fact]
    public void BasicHeal_CanCarryABuff_ToItsTarget()
    {
        // What: verifies the rider works on the healing function too, not just on damage — a
        //       support action can restore HP and raise a stat in one move.
        // How:  the ally heals itself (Self targeting, SelectedTargets buff) carrying a +Power
        //       buff. Its Power must move from 20 to round(20 * 1.35) = 27 by the time the action
        //       resolves. The fight then ends in round 2 — the ally buffs in round 1 and kills the
        //       enemy on its next turn — so the 2-round buff has been ticked down to 1 and is
        //       still active at the end, which is what the closing assertions pin down.
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
                BuffsDebuffs =
                [
                    new BuffDebuffSpec { Stat = BuffDebuffStat.Power, Type = BuffDebuffType.Positive, Target = BuffDebuffTarget.SelectedTargets, Rounds = 2, UntilRemoved = false },
                ],
            },
        };

        var (engine, ally, _) = SetupCombat(opening);

        int? newPower = null;
        var  ticked   = new List<int>();
        CombatEventBus.BuffDebuffApplied += (_, _, _, _, _, _, _, value, _, _) => newPower ??= value;
        CombatEventBus.BuffDebuffTicked  += (_, _, _, _, rounds, _, _) => ticked.Add(rounds);

        engine.BeginCombat();

        Assert.Equal(27, newPower);
        Assert.Equal([1], ticked);      // round 2 ticked it down, one round still to run
        Assert.Equal(27, ally.Power);
    }

    [Fact]
    public void MultipleEntries_EachApply_ToTheirOwnTarget()
    {
        // What: verifies an action can carry more than one buffsDebuffs entry, each independently
        //       resolving its own target selector - a Defense debuff on the action's own target
        //       plus a Power buff on the actor itself, in one move.
        // How:  a single BasicDamage hit carries both entries. Both BuffDebuffApplied firings are
        //       captured; the enemy's Defense entry and the ally's Power entry must both appear.
        var opening = new CombatCommand
        {
            ActorId        = "ally",
            TargetingType  = TargetingType.Random,
            ValidTargets   = ValidTarget.Enemies,
            LivingOrDead   = LivingOrDead.Living,
            CombatFunction = BasicDamageFunction.FunctionName,
            Parameters     = new CombatFunctionParameters
            {
                PowerFactor  = 1.0,
                BuffsDebuffs =
                [
                    new BuffDebuffSpec { Stat = BuffDebuffStat.Defense, Type = BuffDebuffType.Negative, Target = BuffDebuffTarget.SelectedTargets, Rounds = 2, UntilRemoved = false },
                    new BuffDebuffSpec { Stat = BuffDebuffStat.Power,   Type = BuffDebuffType.Positive, Target = BuffDebuffTarget.Self,            Rounds = 2, UntilRemoved = false },
                ],
            },
        };

        var (engine, _, _) = SetupCombat(opening);

        var applied = new List<(string EntityId, BuffDebuffStat Stat)>();
        CombatEventBus.BuffDebuffApplied += (entityId, _, stat, _, _, _, _, _, _, _) => applied.Add((entityId, stat));

        engine.BeginCombat();

        Assert.Contains(("enemy", BuffDebuffStat.Defense), applied);
        Assert.Contains(("ally", BuffDebuffStat.Power), applied);
    }

    [Fact]
    public void SelfTarget_Buff_AppliesEvenWhenTheActionsOwnHitIsFullyEvaded()
    {
        // What: verifies a buffsDebuffs entry is a property of the action, not of any individual
        //       hit - it lands even when every one of the action's own attacks miss.
        // How:  the enemy's evasion is 1.0, which guarantees TryEvade returns true on the very
        //       first roll (Random.NextSingle() never returns exactly 1.0), so the opening
        //       BasicDamage attack always misses. Its Self Power+ buff must still land on the
        //       ally. Only the first BuffDebuffApplied/EntityDamaged pair is inspected - later
        //       turns, once evasion has decayed enough for a hit to land, aren't this test's
        //       concern.
        var opening = new CombatCommand
        {
            ActorId        = "ally",
            TargetingType  = TargetingType.Random,
            ValidTargets   = ValidTarget.Enemies,
            LivingOrDead   = LivingOrDead.Living,
            CombatFunction = BasicDamageFunction.FunctionName,
            Parameters     = new CombatFunctionParameters
            {
                PowerFactor  = 1.0,
                BuffsDebuffs =
                [
                    new BuffDebuffSpec { Stat = BuffDebuffStat.Power, Type = BuffDebuffType.Positive, Target = BuffDebuffTarget.Self, Rounds = 2, UntilRemoved = false },
                ],
            },
        };

        var (engine, _, _) = SetupCombat(opening, enemyEvasion: 1.0f);

        bool openingMoveSeen           = false;
        bool enemyDamagedByOpeningMove = false;
        int? newPower                  = null;
        CombatEventBus.EntityDamaged     += (targetId, _, _, _, _, _, _, _, _, _) =>
        {
            if (!openingMoveSeen && targetId == "enemy") enemyDamagedByOpeningMove = true;
        };
        CombatEventBus.BuffDebuffApplied += (_, _, _, _, _, _, _, value, _, _) =>
        {
            if (!openingMoveSeen) { newPower = value; openingMoveSeen = true; }
        };

        engine.BeginCombat();

        Assert.False(enemyDamagedByOpeningMove);
        Assert.Equal(27, newPower);
    }

    [Fact]
    public void RandomAlly_WithNoOtherLivingAlly_IsASilentNoOp()
    {
        // What: verifies RandomAlly excludes the actor, so a solo actor with no other living ally
        //       resolves to nobody rather than throwing or landing on itself.
        // How:  the ally fights alone; a RandomAlly Power entry on its opening move must never
        //       raise BuffDebuffApplied for Power over the whole fight (the only other action,
        //       the FixedAmount finishing blow, authors no buffsDebuffs at all).
        var opening = new CombatCommand
        {
            ActorId        = "ally",
            TargetingType  = TargetingType.Random,
            ValidTargets   = ValidTarget.Enemies,
            LivingOrDead   = LivingOrDead.Living,
            CombatFunction = BasicDamageFunction.FunctionName,
            Parameters     = new CombatFunctionParameters
            {
                PowerFactor  = 1.0,
                BuffsDebuffs =
                [
                    new BuffDebuffSpec { Stat = BuffDebuffStat.Power, Type = BuffDebuffType.Positive, Target = BuffDebuffTarget.RandomAlly, Rounds = 2, UntilRemoved = false },
                ],
            },
        };

        var (engine, _, _) = SetupCombat(opening);

        bool raised = false;
        CombatEventBus.BuffDebuffApplied += (_, _, stat, _, _, _, _, _, _, _) =>
        {
            if (stat == BuffDebuffStat.Power) raised = true;
        };

        engine.BeginCombat();

        Assert.False(raised);
    }

    [Fact]
    public void CollidingBuffDebuffEntries_Throw_NamingTheAction()
    {
        // What: verifies two entries that resolve to the same (entity, stat) pair are a data
        //       error, caught even though the JSON Schema's uniqueBy can only reject identical
        //       (stat, target) pairs - it can't see that two *different* targets resolve to the
        //       same entity at combat time.
        // How:  Self and AllAllies both move Power on a solo ally, so both entries land on the
        //       same entity's Power - the second Add into the (entityId, stat) set fails and the
        //       function must throw, naming the action id.
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
                BuffsDebuffs =
                [
                    new BuffDebuffSpec { Stat = BuffDebuffStat.Power, Type = BuffDebuffType.Positive, Target = BuffDebuffTarget.Self,      Rounds = 2, UntilRemoved = false },
                    new BuffDebuffSpec { Stat = BuffDebuffStat.Power, Type = BuffDebuffType.Positive, Target = BuffDebuffTarget.AllAllies, Rounds = 2, UntilRemoved = false },
                ],
            },
        };

        var (engine, _, _) = SetupCombat(opening);

        var ex = Assert.Throws<InvalidOperationException>(() => engine.BeginCombat());
        Assert.Contains("broken_tech", ex.Message);
    }
}
