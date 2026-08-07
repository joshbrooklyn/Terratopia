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
            evasion: 0.0f, critChance: 0.0f, critModifier: 0.0f);

        engine.InitCombat(
            allies:  [attacker],
            enemies: [defender]);

        // Passives are granted after InitCombat (which resets PassiveTracker), mirroring how
        // GameEngineClass.InitSkirmishCombat grants monster passives.
        foreach (var name in defenderPassives ?? [])
            PassiveTracker.Add(name, "defender");

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
        //       hit, HP would drop to 0, but LivingDeadPassive.OnBeforeDeath intercepts it,
        //       removes itself from PassiveTracker (so HandleDefeat's dispatch loop won't find it
        //       again), and immediately bumps HP back up to 1, so the trace should show 0 then 1.
        //       On the next lethal hit the entity no longer owns LivingDead at all, so this time
        //       HP drops to 0 and stays there — a real death. The test asserts the HP trace is
        //       exactly [0, 1, 0], revivedCount is 1, deathCount is 1, the defender ends up dead
        //       at 0 HP, and the tracker no longer lists LivingDead among "defender"'s passives.
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
        Assert.Empty(PassiveTracker.GetPassives("defender"));
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
        //       passives granted at all and nothing intercepts HandleDefeat's death-prevention
        //       hook. The test counts EntityRevived and EntityDeath events raised for "defender"
        //       across the whole fight, then asserts EntityRevived never fired
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

    [Fact]
    public void LethalDamage_WithLivingDead_RaisesPassiveRemovedExactlyOnce()
    {
        // What: verifies CombatEventBus.PassiveRemoved fires when LivingDeadPassive strips its own
        //       ownership on firing, naming the defender and the passive - and that the second
        //       lethal hit (a real death, no passive left to remove) doesn't raise it again.
        var (engine, _, defender) = SetupCombat([LivingDeadPassive.PassiveName]);

        var raised = new List<(string EntityId, string EntityName, string Passive)>();
        CombatEventBus.PassiveRemoved += (entityId, entityName, passiveName) =>
            raised.Add((entityId, entityName, passiveName));

        engine.BeginCombat();

        Assert.Equal([("defender", "Defender", LivingDeadPassive.PassiveName)], raised);
    }

    [Fact]
    public void Remove_BeforeLethalHit_EntityDiesOutrightDespiteHavingBeenGrantedLivingDead()
    {
        // What: verifies PassiveTracker.Remove actually strips the passive from dispatch —
        //       an entity that was granted LivingDead but has it removed before its first
        //       lethal hit dies normally, the same as if it never had the passive.
        // How:  SetupCombat grants the defender LivingDead, then a RoundStarted handler removes
        //       it again on round 1 - before the attacker's turn resolves, since RoundStarted
        //       fires ahead of turn dispatch. The first (and only) lethal hit then finds no
        //       LivingDead entry in the tracker, so HandleDefeat's dispatch loop is empty and
        //       the entity dies outright.
        var (engine, _, defender) = SetupCombat([LivingDeadPassive.PassiveName]);

        CombatEventBus.RoundStarted += (round, _, _) =>
        {
            if (round == 1)
                PassiveTracker.Remove(LivingDeadPassive.PassiveName, "defender");
        };

        int revivedCount = 0;
        int deathCount = 0;
        CombatEventBus.EntityRevived += (id, _, _, _, _, _) => { if (id == "defender") revivedCount++; };
        CombatEventBus.EntityDeath   += (id, _, _, _) => { if (id == "defender") deathCount++; };

        engine.BeginCombat();

        Assert.Equal(0, revivedCount);
        Assert.Equal(1, deathCount);
        Assert.True(defender.IsDead);
        Assert.Empty(PassiveTracker.GetPassives("defender"));
    }

    [Fact]
    public void Remove_ThenAdd_ReArmsAConsumedPassive()
    {
        // What: verifies that removing and re-adding a passive resets its activation history —
        //       a LivingDead that already revived once (and would normally be consumed for the
        //       rest of the combat) fires again after being Remove()d and re-Add()ed. The
        //       attacker can never die (its damage is floored to 0 by the defender's defense), so
        //       the fight only ends once the defender runs out of revives - a re-arm just buys it
        //       one extra life, it doesn't make it unkillable.
        // How:  The defender starts with LivingDead and survives its first lethal hit via the
        //       usual revive - which already drops LivingDead's own PassiveTracker record (it
        //       removes itself on firing). The EntityRevived handler then immediately Removes
        //       (a no-op, since the record is already gone) and re-Adds the passive on "defender"
        //       - but only once, guarded by a flag - which rebuilds its record from scratch. The
        //       second lethal hit should therefore revive the defender again instead of killing
        //       it for real; the third lethal hit finds the entity no longer owns LivingDead at
        //       all (consumed again by the second revive) and it dies for real.
        var (engine, _, defender) = SetupCombat([LivingDeadPassive.PassiveName]);

        bool reArmed = false;
        CombatEventBus.EntityRevived += (id, _, _, _, _, _) =>
        {
            if (id == "defender" && !reArmed)
            {
                reArmed = true;
                PassiveTracker.Remove(LivingDeadPassive.PassiveName, "defender");
                PassiveTracker.Add(LivingDeadPassive.PassiveName, "defender");
            }
        };

        var hpTrace = new List<int>();
        int revivedCount = 0;
        int deathCount = 0;
        CombatEventBus.EntityDamaged += (id, _, _, _, _, _, _, _, _, newHp) => { if (id == "defender") hpTrace.Add(newHp); };
        CombatEventBus.EntityRevived += (id, _, _, newHp, _, _) => { if (id == "defender") { hpTrace.Add(newHp); revivedCount++; } };
        CombatEventBus.EntityDeath   += (id, _, _, _) => { if (id == "defender") deathCount++; };

        engine.BeginCombat();

        // Two revives (initial + one re-arm), then a real death on the third lethal hit.
        Assert.Equal([0, 1, 0, 1, 0], hpTrace);
        Assert.Equal(2, revivedCount);
        Assert.Equal(1, deathCount);
        Assert.True(defender.IsDead);
        Assert.Empty(PassiveTracker.GetPassives("defender"));
    }
}

[Collection("CombatEngineSerial")]
public class LivingDeadPassiveTests
{
    private static CombatEntity MakeEntity(string id, int hp) => new(
        entityId: id, name: id, level: 1,
        maxHp: 10, hp: hp, maxTp: 0, tp: 0,
        power: 0, defense: 0, speed: 0,
        evasion: 0f, critChance: 0f, critModifier: 0f);

    [Fact]
    public void HandleDefeat_FirstCall_RevivesAtOneHp()
    {
        // What: drives LivingDeadPassive through the real combat entity (TakeDamage ->
        //       HandleDefeat -> OnBeforeDeath) to verify that on the entity's first lethal hit
        //       it revives at 1 HP instead of dying.
        // How:  PassiveTracker.Reset() clears any state left by other tests (the store is
        //       static), then MakeEntity("killme", 1) builds a CombatEntity at 1 HP and
        //       PassiveTracker.Add grants it LivingDead. killme.TakeDamage(attacker, 1, ...)
        //       brings it to 0 HP, which triggers HandleDefeat -> LivingDeadPassive.OnBeforeDeath,
        //       which should remove its own record from PassiveTracker (marking it used) and
        //       report (true, 1 HP) so the engine sets Hp back to 1 instead of marking the entity
        //       dead. The test asserts killme is not dead, killme.Hp is 1, and the tracker no
        //       longer lists LivingDead among "killme"'s passives.
        PassiveTracker.Reset();

        var killme   = MakeEntity("killme", 1);
        var attacker = MakeEntity("attacker", 1);
        PassiveTracker.Add(LivingDeadPassive.PassiveName, "killme");

        killme.TakeDamage(attacker, 1, "test", "Test"); // simulate lethal damage

        Assert.False(killme.IsDead);
        Assert.Equal(1, killme.Hp);
        Assert.Empty(PassiveTracker.GetPassives("killme"));
    }

    [Fact]
    public void OnBeforeDeath_FirstCall_RemovesOwnershipSoDispatchWontFireAgain()
    {
        // What: verifies LivingDeadPassive's single-use guarantee is enforced by ownership, not
        //       by an internal already-triggered flag — firing OnBeforeDeath once removes the
        //       passive from PassiveTracker outright, which is what stops HandleDefeat's dispatch
        //       loop (PassiveTracker.GetPassives) from ever finding it on the entity again for a
        //       later lethal hit. Calling OnBeforeDeath again directly on the same instance, as
        //       opposed to going through the dispatch loop, is not itself guarded — nothing in
        //       the engine does that, since HandleDefeat only invokes passives it reads back from
        //       PassiveTracker.
        // How:  PassiveTracker.Reset() clears state left by other tests. PassiveTracker.Add
        //       grants "entity" LivingDead, then OnBeforeDeath is called directly once. The test
        //       asserts it reports deathPrevented: true with the configured revive HP, and that
        //       the tracker no longer lists LivingDead among "entity"'s passives afterward.
        PassiveTracker.Reset();

        var entity  = MakeEntity("entity", 1);
        var passive = new LivingDeadPassive();
        PassiveTracker.Add(LivingDeadPassive.PassiveName, "entity");

        var (prevented, reviveHp) = passive.OnBeforeDeath(entity.EntityId, entity.Name);

        Assert.True(prevented);
        Assert.Equal(CombatBalance.Current.LivingDeadReviveHp, reviveHp);
        Assert.Empty(PassiveTracker.GetPassives("entity"));
    }

    [Fact]
    public void RemoveFrom_OnAnEntityThatDoesNotOwnIt_RaisesNoPassiveRemoved()
    {
        // What: verifies Passive.RemoveFrom only raises CombatEventBus.PassiveRemoved on a genuine
        //       removal - calling it a second time, after the passive already removed itself
        //       (PassiveTracker.Remove is then a no-op), raises nothing.
        PassiveTracker.Reset();
        CombatEventBus.Reset(); // no engine.InitCombat in this test to do it for us

        var entity  = MakeEntity("entity", 1);
        var passive = new LivingDeadPassive();
        PassiveTracker.Add(LivingDeadPassive.PassiveName, "entity");

        int raisedCount = 0;
        CombatEventBus.PassiveRemoved += (_, _, _) => raisedCount++;

        passive.RemoveFrom(entity.EntityId, entity.Name); // first call - genuine
        Assert.Equal(1, raisedCount);

        passive.RemoveFrom(entity.EntityId, entity.Name); // already gone - no-op
        Assert.Equal(1, raisedCount);
    }
}

[Collection("CombatEngineSerial")]
public class PassiveTrackerTests
{
    [Fact]
    public void RecordActivation_AccumulatesTotalWhileBeginRoundResetsPerRoundCount()
    {
        // What: TotalApplications is a running combat-long count; ApplicationsThisRound resets
        //       every round.
        PassiveTracker.Reset();
        var passive = new LivingDeadPassive();
        PassiveTracker.Add(LivingDeadPassive.PassiveName, "e1");

        PassiveTracker.RecordActivation(passive, "e1");
        PassiveTracker.RecordActivation(passive, "e1");
        var afterRound1 = PassiveTracker.Get(LivingDeadPassive.PassiveName, "e1");
        Assert.Equal(2, afterRound1.ApplicationsThisRound);
        Assert.Equal(2, afterRound1.TotalApplications);

        PassiveTracker.BeginRound(2);
        var afterBeginRound2 = PassiveTracker.Get(LivingDeadPassive.PassiveName, "e1");
        Assert.Equal(0, afterBeginRound2.ApplicationsThisRound);
        Assert.Equal(2, afterBeginRound2.TotalApplications);

        PassiveTracker.RecordActivation(passive, "e1");
        var afterRound2 = PassiveTracker.Get(LivingDeadPassive.PassiveName, "e1");
        Assert.Equal(1, afterRound2.ApplicationsThisRound);
        Assert.Equal(3, afterRound2.TotalApplications);
    }

    [Fact]
    public void RoundApplied_ReflectsTheRoundAddWasCalled()
    {
        // What: RoundApplied stamps whichever round Add() happened in, so
        //       CurrentRound - RoundApplied gives "rounds elapsed since acquired" at any point.
        PassiveTracker.Reset();
        Assert.Equal(1, PassiveTracker.CurrentRound);

        PassiveTracker.Add(LivingDeadPassive.PassiveName, "e1");
        Assert.Equal(1, PassiveTracker.Get(LivingDeadPassive.PassiveName, "e1").RoundApplied);

        PassiveTracker.BeginRound(3);
        Assert.Equal(3, PassiveTracker.CurrentRound);
        Assert.Equal(2, PassiveTracker.CurrentRound - PassiveTracker.Get(LivingDeadPassive.PassiveName, "e1").RoundApplied);

        PassiveTracker.Add(LivingDeadPassive.PassiveName, "e2");
        Assert.Equal(3, PassiveTracker.Get(LivingDeadPassive.PassiveName, "e2").RoundApplied);
    }

    [Fact]
    public void Reset_ClearsOwnershipAndHistory()
    {
        PassiveTracker.Reset();
        PassiveTracker.Add(LivingDeadPassive.PassiveName, "e1");
        PassiveTracker.RecordActivation(new LivingDeadPassive(), "e1");

        PassiveTracker.Reset();

        Assert.Empty(PassiveTracker.GetPassives("e1"));
        Assert.Equal(0, PassiveTracker.Get(LivingDeadPassive.PassiveName, "e1").TotalApplications);
        Assert.Equal(1, PassiveTracker.CurrentRound);
    }

    [Fact]
    public void Add_UnrecognisedName_CreatesNoRecord()
    {
        // What: mirrors PassiveRegistry.Resolve's old silent-drop behavior for bad names -
        //       granting a name the registry doesn't know about is a no-op, not an error.
        PassiveTracker.Reset();

        PassiveTracker.Add("NotARealPassive", "e1");

        Assert.Empty(PassiveTracker.GetPassives("e1"));
    }

    [Fact]
    public void Remove_ReturnsTrueOnlyOnAGenuineRemoval()
    {
        // What: Remove's bool return is what lets Passive.RemoveFrom tell a real removal apart
        //       from a no-op and raise CombatEventBus.PassiveRemoved only for the former.
        PassiveTracker.Reset();
        PassiveTracker.Add(LivingDeadPassive.PassiveName, "e1");

        Assert.True(PassiveTracker.Remove(LivingDeadPassive.PassiveName, "e1"));
        Assert.False(PassiveTracker.Remove(LivingDeadPassive.PassiveName, "e1")); // already gone
        Assert.False(PassiveTracker.Remove(LivingDeadPassive.PassiveName, "never-owned"));
    }

    [Fact]
    public void Remove_ThenAdd_RestartsHistoryFromZero()
    {
        PassiveTracker.Reset();
        PassiveTracker.Add(LivingDeadPassive.PassiveName, "e1");
        PassiveTracker.RecordActivation(new LivingDeadPassive(), "e1");
        Assert.Equal(1, PassiveTracker.Get(LivingDeadPassive.PassiveName, "e1").TotalApplications);

        PassiveTracker.Remove(LivingDeadPassive.PassiveName, "e1");
        Assert.Empty(PassiveTracker.GetPassives("e1"));

        PassiveTracker.BeginRound(4);
        PassiveTracker.Add(LivingDeadPassive.PassiveName, "e1");

        var reAdded = PassiveTracker.Get(LivingDeadPassive.PassiveName, "e1");
        Assert.Equal(0, reAdded.TotalApplications);
        Assert.Equal(4, reAdded.RoundApplied);
    }
}
