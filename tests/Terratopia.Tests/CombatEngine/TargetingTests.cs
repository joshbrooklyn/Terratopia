using CombatEngine;
using CombatEngine.CombatFunctions;
using CombatEngine.DataClasses;
using CombatEngine.Engine;
using CombatEngine.Enums;

namespace Terratopia.Tests.CombatEngine;

[Collection("CombatEngineSerial")]
public class TargetingTests
{
    // Returns a fixed value for every NextSingle() call and 0 for every Next(int) call,
    // so AI/auto-target picks are deterministic (always pool[0], never evades/crits).
    private sealed class ControlledRandom(float single) : Random
    {
        public override float NextSingle() => single;
        public override int Next(int maxValue) => 0;
    }

    private static CombatEntity MakeEntity(string id, int speed, int hp = 100, int power = 10) => new(
        entityId: id, name: id, level: 1,
        maxHp: hp, hp: hp, maxTp: 0, tp: 0,
        power: power, defense: 0, speed: speed,
        evasion: 0.0f, critChance: 0.0f, critModifier: 0.0f);

    // Built purely to query GetValidTargets — it is never resolved, so the CombatFunction is
    // irrelevant here and NoOp keeps it honest.
    private static CombatCommand TargetQuery(string actorId, ValidTarget validTargets, LivingOrDead livingOrDead) => new()
    {
        ActorId        = actorId,
        ValidTargets   = validTargets,
        LivingOrDead   = livingOrDead,
        CombatFunction = NoOpFunction.FunctionName,
    };

    // ---------------------------------------------------------------
    // GetValidTargets: pool selection by ValidTarget (no BeginCombat needed —
    // this is a pure query against the state InitCombat already set up).
    // ---------------------------------------------------------------

    [Fact]
    public void GetValidTargets_Enemies_FromPlayerActor_ReturnsOnlyEnemies()
    {
        // What: verifies that querying ValidTarget.Enemies for a player-side actor returns the
        //       enemy roster, not the actor's own allies.
        // How:  InitCombat sets up one ally and two enemies. Calling GetValidTargets with a
        //       query for "ally" and ValidTarget.Enemies should map to the actor's opposing
        //       side, which for a player actor is the enemies list. No BeginCombat is needed
        //       here since GetValidTargets is a pure query over the entity lists InitCombat
        //       already populated. The test asserts the returned entity IDs are exactly
        //       ["enemy1", "enemy2"], in the order they were added.
        var engine = new CombatEngineClass(new Random(0));
        var ally   = MakeEntity("ally", speed: 10);
        var enemy1 = MakeEntity("enemy1", speed: 10);
        var enemy2 = MakeEntity("enemy2", speed: 10);
        engine.InitCombat(allies: [ally], enemies: [enemy1, enemy2]);

        var targets = engine.GetValidTargets(TargetQuery("ally", ValidTarget.Enemies, LivingOrDead.Living));

        Assert.Equal(["enemy1", "enemy2"], targets.Select(e => e.EntityId));
    }

    [Fact]
    public void GetValidTargets_Enemies_FromEnemyActor_ReturnsAllies()
    {
        // What: verifies that ValidTarget.Enemies is relative to the querying actor's own
        //       side, not an absolute label — an enemy-side actor's "Enemies" pool should be
        //       the player's allies, mirroring GetValidTargets_Enemies_FromPlayerActor above.
        // How:  InitCombat sets up two allies and one enemy. Calling GetValidTargets with a
        //       query for "enemy" (which IsPlayerEntity will classify as not a player actor)
        //       and ValidTarget.Enemies should resolve to the opposite side from the enemy's
        //       perspective, i.e. the allies list. The test asserts the returned IDs are
        //       exactly ["ally1", "ally2"], proving the pool selection flips based on which
        //       side the actor belongs to.
        var engine = new CombatEngineClass(new Random(0));
        var ally1 = MakeEntity("ally1", speed: 10);
        var ally2 = MakeEntity("ally2", speed: 10);
        var enemy = MakeEntity("enemy", speed: 10);
        engine.InitCombat(allies: [ally1, ally2], enemies: [enemy]);

        var targets = engine.GetValidTargets(TargetQuery("enemy", ValidTarget.Enemies, LivingOrDead.Living));

        Assert.Equal(["ally1", "ally2"], targets.Select(e => e.EntityId));
    }

    [Fact]
    public void GetValidTargets_Allies_FromPlayerActor_ExcludesEnemies()
    {
        // What: verifies ValidTarget.Allies returns the actor's own side (including the actor
        //       itself) and excludes the opposing side entirely.
        // How:  InitCombat sets up two allies and one enemy. Calling GetValidTargets with a
        //       query for "ally1" and ValidTarget.Allies should map to the allies list, which
        //       includes "ally1" itself alongside "ally2" — self-targeting is allowed under
        //       the Allies pool. The test asserts the result is exactly ["ally1", "ally2"],
        //       with the enemy nowhere in the returned set.
        var engine = new CombatEngineClass(new Random(0));
        var ally1 = MakeEntity("ally1", speed: 10);
        var ally2 = MakeEntity("ally2", speed: 10);
        var enemy = MakeEntity("enemy", speed: 10);
        engine.InitCombat(allies: [ally1, ally2], enemies: [enemy]);

        var targets = engine.GetValidTargets(TargetQuery("ally1", ValidTarget.Allies, LivingOrDead.Living));

        Assert.Equal(["ally1", "ally2"], targets.Select(e => e.EntityId));
    }

    [Fact]
    public void GetValidTargets_Both_ReturnsAlliesAndEnemiesTogether()
    {
        // What: verifies ValidTarget.Both merges allies and enemies into a single pool,
        //       ignoring the side distinction entirely.
        // How:  InitCombat sets up one ally and one enemy. Calling GetValidTargets with
        //       ValidTarget.Both should pull from _allEntities.Values directly (every entity
        //       in the fight), rather than filtering by side at all. The test asserts the
        //       returned IDs include both "ally" and "enemy".
        var engine = new CombatEngineClass(new Random(0));
        var ally  = MakeEntity("ally", speed: 10);
        var enemy = MakeEntity("enemy", speed: 10);
        engine.InitCombat(allies: [ally], enemies: [enemy]);

        var targets = engine.GetValidTargets(TargetQuery("ally", ValidTarget.Both, LivingOrDead.Living));

        Assert.Equal(["ally", "enemy"], targets.Select(e => e.EntityId));
    }

    // ---------------------------------------------------------------
    // GetValidTargets: filtering by LivingOrDead
    // ---------------------------------------------------------------

    [Fact]
    public void GetValidTargets_Living_ExcludesDeadEntities()
    {
        // What: verifies that LivingOrDead.Living filters out any entity flagged as dead,
        //       leaving only entities still able to act as valid targets.
        // How:  InitCombat sets up an ally and two enemies, one of which has IsDead manually
        //       set to true before combat starts (simulating a pre-existing corpse rather than
        //       something the engine killed). Querying with LivingOrDead.Living should apply
        //       the `pool.Where(e => !e.IsDead)` filter, dropping "deadEnemy" from the result.
        //       The test asserts only "livingEnemy" comes back.
        var engine = new CombatEngineClass(new Random(0));
        var ally        = MakeEntity("ally", speed: 10);
        var deadEnemy   = MakeEntity("deadEnemy", speed: 10);
        deadEnemy.IsDead = true;
        var livingEnemy = MakeEntity("livingEnemy", speed: 10);
        engine.InitCombat(allies: [ally], enemies: [deadEnemy, livingEnemy]);

        var targets = engine.GetValidTargets(TargetQuery("ally", ValidTarget.Enemies, LivingOrDead.Living));

        Assert.Equal(["livingEnemy"], targets.Select(e => e.EntityId));
    }

    [Fact]
    public void GetValidTargets_Dead_ReturnsOnlyDeadEntities()
    {
        // What: verifies the inverse filter — LivingOrDead.Dead should return only entities
        //       flagged as dead, useful for effects like resurrection that must target corpses.
        // How:  Same setup as GetValidTargets_Living_ExcludesDeadEntities (one dead enemy, one
        //       living enemy), but this time the query uses LivingOrDead.Dead, which applies
        //       `pool.Where(e => e.IsDead)` instead. The test asserts only "deadEnemy" is
        //       returned, with the living enemy excluded.
        var engine = new CombatEngineClass(new Random(0));
        var ally        = MakeEntity("ally", speed: 10);
        var deadEnemy   = MakeEntity("deadEnemy", speed: 10);
        deadEnemy.IsDead = true;
        var livingEnemy = MakeEntity("livingEnemy", speed: 10);
        engine.InitCombat(allies: [ally], enemies: [deadEnemy, livingEnemy]);

        var targets = engine.GetValidTargets(TargetQuery("ally", ValidTarget.Enemies, LivingOrDead.Dead));

        Assert.Equal(["deadEnemy"], targets.Select(e => e.EntityId));
    }

    [Fact]
    public void GetValidTargets_Both_LivingOrDead_ReturnsRegardlessOfDeathState()
    {
        // What: verifies LivingOrDead.Both bypasses the death-state filter entirely, returning
        //       every entity in the ValidTarget pool regardless of whether it's alive or dead.
        // How:  Same one-dead/one-living enemy setup as the two tests above, but the query uses
        //       LivingOrDead.Both, which maps to `pool.ToList()` with no Where filter applied
        //       at all. The test asserts both "deadEnemy" and "livingEnemy" come back together,
        //       confirming the death-state filter is skipped rather than matching both branches
        //       independently.
        var engine = new CombatEngineClass(new Random(0));
        var ally        = MakeEntity("ally", speed: 10);
        var deadEnemy   = MakeEntity("deadEnemy", speed: 10);
        deadEnemy.IsDead = true;
        var livingEnemy = MakeEntity("livingEnemy", speed: 10);
        engine.InitCombat(allies: [ally], enemies: [deadEnemy, livingEnemy]);

        var targets = engine.GetValidTargets(TargetQuery("ally", ValidTarget.Enemies, LivingOrDead.Both));

        Assert.Equal(["deadEnemy", "livingEnemy"], targets.Select(e => e.EntityId));
    }

    [Fact]
    public void GetValidTargets_AllEnemiesDead_ReturnsEmptyWithoutThrowing()
    {
        // What: verifies an edge case — when every entity in the requested pool is dead (or
        //       otherwise excluded), GetValidTargets returns an empty list rather than
        //       throwing or returning null.
        // How:  InitCombat sets up an ally and a single enemy that is already dead. Querying
        //       for ValidTarget.Enemies + LivingOrDead.Living filters that lone enemy out,
        //       leaving nothing in the pool. The test asserts the result is empty (not null,
        //       not an exception), which matters because callers like AssignRandomAiTarget
        //       index into this list without a null-check.
        var engine   = new CombatEngineClass(new Random(0));
        var ally     = MakeEntity("ally", speed: 10);
        var deadOnly = MakeEntity("deadOnly", speed: 10);
        deadOnly.IsDead = true;
        engine.InitCombat(allies: [ally], enemies: [deadOnly]);

        var targets = engine.GetValidTargets(TargetQuery("ally", ValidTarget.Enemies, LivingOrDead.Living));

        Assert.Empty(targets);
    }

    // ---------------------------------------------------------------
    // AssignRandomAiTarget: non-player actors always auto-target, regardless of
    // the submitted command's TargetingType (verified via BeginCombat + WaitingForTurn,
    // since the method itself is private). ChosenTargets is captured immediately after
    // SubmitCommand returns — the flow machine mutates it synchronously before ResolvingAction.
    // ---------------------------------------------------------------

    [Fact]
    public void AiTargeting_SingleValidTarget_PicksThatTarget()
    {
        // What: verifies that a non-player (AI/enemy) actor auto-targets via
        //       AssignRandomAiTarget regardless of the TargetingType the submitted command
        //       claims — with only one valid target available, the AI should always land on it.
        // How:  A single ally and a single enemy are set up. The enemy is the only non-player
        //       actor, so every command it submits goes through AssignRandomAiTarget, which
        //       picks pool[rng.Next(pool.Count)] from GetValidTargets — here the pool has only
        //       one member ("ally"), so the pick is forced regardless of the RNG's value.
        //       AssignRandomAiTarget runs synchronously inside SubmitCommand's OnEntry-driven
        //       reentrant Fire() call, so the command's ChosenTargets isn't safely readable
        //       until after the outermost BeginCombat() call has fully returned — this test
        //       captures the command object itself during WaitingForTurn and reads
        //       ChosenTargets only afterward. The test asserts the enemy's captured command's
        //       single chosen target is "ally".
        var engine = new CombatEngineClass(new Random(0));
        var ally   = MakeEntity("ally", speed: 5, hp: 1, power: 0);   // dies in one hit, no threat back
        var enemy  = MakeEntity("enemy", speed: 10, power: 10);       // AI actor, goes first

        engine.InitCombat(allies: [ally], enemies: [enemy]);

        // Stateless queues reentrant Fire() calls made from inside an OnEntry handler (which is
        // where WaitingForTurn is raised) and only drains them once the outermost Fire() call
        // (here, BeginCombat()) unwinds — so ChosenTargets is not yet populated immediately after
        // SubmitCommand returns. Capture the command reference and read it only after BeginCombat().
        var enemyCmds = new List<CombatCommand>();
        CombatEventBus.WaitingForTurn += (entityId, _, _, isAlly) =>
        {
            if (isAlly)
            {
                engine.SubmitCommand(new CombatCommand
                {
                    ActorId = entityId, TargetingType = TargetingType.Self,
                    ValidTargets = ValidTarget.Allies, LivingOrDead = LivingOrDead.Living,
                    CombatFunction = NoOpFunction.FunctionName,
                });
            }
            else
            {
                var cmd = new CombatCommand
                {
                    ActorId = entityId, TargetingType = TargetingType.Random,
                    ValidTargets = ValidTarget.Enemies, LivingOrDead = LivingOrDead.Living,
                    CombatFunction = BasicDamageFunction.FunctionName,
                };
                engine.SubmitCommand(cmd);
                enemyCmds.Add(cmd);
            }
        };

        engine.BeginCombat();

        Assert.Equal(["ally"], enemyCmds.Select(c => c.ChosenTargets.Single()));
    }

    [Fact]
    public void AiTargeting_MultipleValidTargets_WithControlledRandom_AlwaysPicksFirstLivingInPoolOrder()
    {
        // What: verifies AssignRandomAiTarget's pick is genuinely driven by rng.Next(pool.Count)
        //       rather than some fixed or hardcoded target — with a controlled RNG that always
        //       returns index 0, the AI should always hit whichever entity currently occupies
        //       pool[0], and that identity should change as earlier entries die.
        // How:  ControlledRandom.Next(maxValue) always returns 0, so every AssignRandomAiTarget
        //       call resolves to pool[0] from GetValidTargets' current living pool. Two allies
        //       with only 1 HP each face a single enemy; on the first enemy turn the pool is
        //       [allyA, allyB], so pool[0] is "allyA" — that hit kills allyA immediately since
        //       it only has 1 HP. On the enemy's next turn, allyA is now dead and excluded from
        //       GetValidTargets, so the pool has shrunk to just [allyB], and pool[0] is now
        //       "allyB". If the AI were ignoring rng.Next and always targeting a fixed name, the
        //       second pick wouldn't track this shift. The test asserts the sequence of chosen
        //       targets across the enemy's two turns is exactly ["allyA", "allyB"].
        var engine = new CombatEngineClass(new ControlledRandom(0.0f));
        var allyA = MakeEntity("allyA", speed: 5, hp: 1, power: 0);
        var allyB = MakeEntity("allyB", speed: 5, hp: 1, power: 0);
        var enemy = MakeEntity("enemy", speed: 10, power: 10);

        engine.InitCombat(allies: [allyA, allyB], enemies: [enemy]);

        var enemyCmds = new List<CombatCommand>();
        CombatEventBus.WaitingForTurn += (entityId, _, _, isAlly) =>
        {
            if (isAlly)
            {
                engine.SubmitCommand(new CombatCommand
                {
                    ActorId = entityId, TargetingType = TargetingType.Self,
                    ValidTargets = ValidTarget.Allies, LivingOrDead = LivingOrDead.Living,
                    CombatFunction = NoOpFunction.FunctionName,
                });
            }
            else
            {
                var cmd = new CombatCommand
                {
                    ActorId = entityId, TargetingType = TargetingType.Random,
                    ValidTargets = ValidTarget.Enemies, LivingOrDead = LivingOrDead.Living,
                    CombatFunction = BasicDamageFunction.FunctionName,
                };
                engine.SubmitCommand(cmd);
                enemyCmds.Add(cmd);
            }
        };

        engine.BeginCombat();

        Assert.Equal(["allyA", "allyB"], enemyCmds.Select(c => c.ChosenTargets.Single()));
    }

    // ---------------------------------------------------------------
    // ExpandAutoTargets: player-actor auto-expansion (TargetingType.All / Self / Random).
    // Same capture technique as above — private method, exercised via the public flow.
    // ---------------------------------------------------------------

    [Fact]
    public void ExpandAutoTargets_All_TargetsEveryLivingEnemy_ExcludingDead()
    {
        // What: verifies TargetingType.All expands to every living entity in the requested
        //       ValidTarget pool, while still excluding any entity that's already dead — an
        //       "All" attack shouldn't waste a hit on a corpse.
        // How:  A player-side ally submits a TargetingType.All command targeting
        //       ValidTarget.Enemies, against three enemies where the third ("e3") is manually
        //       marked dead before combat even starts. ExpandAutoTargets' All case pulls from
        //       GetLivingEnemies() (or GetLivingAllies(), depending on side), which already
        //       excludes dead entities, and sets ChosenTargets to every ID in that pool. So the
        //       expansion should include "e1" and "e2" but skip "e3" entirely. As in the AI
        //       -targeting tests above, the command reference is captured during WaitingForTurn
        //       and ChosenTargets is only read after BeginCombat() fully unwinds, since
        //       ExpandAutoTargets also runs inside the reentrant Fire() call.
        var engine = new CombatEngineClass(new Random(0));
        var ally = MakeEntity("ally", speed: 10);
        var e1   = MakeEntity("e1", speed: 5, hp: 1, power: 0);
        var e2   = MakeEntity("e2", speed: 4, hp: 1, power: 0);
        var e3   = MakeEntity("e3", speed: 3, hp: 1, power: 0);
        e3.IsDead = true; // pre-dead: must never appear in an "All" expansion

        engine.InitCombat(allies: [ally], enemies: [e1, e2, e3]);

        // See the reentrancy note in AiTargeting_MultipleValidTargets_... — capture the command
        // reference and read ChosenTargets only after BeginCombat() has fully unwound.
        CombatCommand? capturedCmd = null;
        CombatEventBus.WaitingForTurn += (entityId, _, _, isAlly) =>
        {
            if (isAlly)
            {
                var cmd = new CombatCommand
                {
                    ActorId = entityId, TargetingType = TargetingType.All,
                    ValidTargets = ValidTarget.Enemies, LivingOrDead = LivingOrDead.Living,
                    CombatFunction = BasicDamageFunction.FunctionName,
                };
                engine.SubmitCommand(cmd);
                capturedCmd ??= cmd;
            }
            else
            {
                engine.SubmitCommand(new CombatCommand
                {
                    ActorId = entityId, TargetingType = TargetingType.Self,
                    ValidTargets = ValidTarget.Enemies, LivingOrDead = LivingOrDead.Living,
                    CombatFunction = NoOpFunction.FunctionName,
                });
            }
        };

        engine.BeginCombat();

        Assert.Equal(["e1", "e2"], capturedCmd!.ChosenTargets);
    }

    [Fact]
    public void ExpandAutoTargets_Self_TargetsOnlyTheActor_RegardlessOfValidTargets()
    {
        // What: verifies TargetingType.Self always resolves to the actor itself, ignoring
        //       whatever ValidTarget pool the command specifies — even a deliberately
        //       "wrong" pool like ValidTarget.Enemies shouldn't redirect a Self-targeted
        //       command onto an enemy.
        // How:  The ally submits a command with TargetingType.Self but ValidTargets set to
        //       ValidTarget.Enemies (an intentionally mismatched pool, to prove Self doesn't
        //       even consult it). ExpandAutoTargets' Self case just sets
        //       ChosenTargets = [cmd.ActorId] directly, bypassing any pool lookup entirely. The
        //       test captures the command and, after BeginCombat() unwinds, asserts
        //       ChosenTargets is exactly ["ally"] — not "enemy" — confirming ValidTargets is
        //       irrelevant for Self.
        var engine = new CombatEngineClass(new Random(0));
        var ally  = MakeEntity("ally", speed: 10, hp: 1, power: 0);
        var enemy = MakeEntity("enemy", speed: 5, power: 10);

        engine.InitCombat(allies: [ally], enemies: [enemy]);

        CombatCommand? capturedCmd = null;
        CombatEventBus.WaitingForTurn += (entityId, _, _, isAlly) =>
        {
            if (isAlly)
            {
                var cmd = new CombatCommand
                {
                    ActorId = entityId, TargetingType = TargetingType.Self,
                    ValidTargets = ValidTarget.Enemies, // deliberately "wrong" pool — Self must ignore it
                    LivingOrDead = LivingOrDead.Living,
                    CombatFunction = NoOpFunction.FunctionName,
                };
                engine.SubmitCommand(cmd);
                capturedCmd ??= cmd;
            }
            else
            {
                engine.SubmitCommand(new CombatCommand
                {
                    ActorId = entityId, TargetingType = TargetingType.Random,
                    ValidTargets = ValidTarget.Enemies, LivingOrDead = LivingOrDead.Living,
                    CombatFunction = BasicDamageFunction.FunctionName,
                });
            }
        };

        engine.BeginCombat();

        Assert.Equal(["ally"], capturedCmd!.ChosenTargets);
    }

    // Shared rig for the TargetingType.Random cases below: ally (fast, harmless) vs e1/e2.
    // e1 kills the ally on its own turn right after, which ends combat deterministically
    // without needing the ally's random pick itself to matter for damage.
    private static List<string> CaptureAllyRandomTargets(int numAttacks, bool allowMultiple)
    {
        var engine = new CombatEngineClass(new ControlledRandom(0.1f));
        var ally = MakeEntity("ally", speed: 10, hp: 1, power: 0);
        var e1   = MakeEntity("e1", speed: 7, power: 10);
        var e2   = MakeEntity("e2", speed: 5, power: 10);

        engine.InitCombat(allies: [ally], enemies: [e1, e2]);

        CombatCommand? capturedCmd = null;
        CombatEventBus.WaitingForTurn += (entityId, _, _, isAlly) =>
        {
            if (isAlly)
            {
                var cmd = new CombatCommand
                {
                    ActorId = entityId, TargetingType = TargetingType.Random,
                    ValidTargets = ValidTarget.Enemies, LivingOrDead = LivingOrDead.Living,
                    NumAttacks = numAttacks, AllowMultipleAttackOnSameTarget = allowMultiple,
                    CombatFunction = NoOpFunction.FunctionName, // only ChosenTargets selection is under test here
                };
                engine.SubmitCommand(cmd);
                capturedCmd ??= cmd;
            }
            else if (entityId == "e1")
            {
                // e1 kills the (1-HP) ally on its own turn, ending combat after this round.
                engine.SubmitCommand(new CombatCommand
                {
                    ActorId = entityId, TargetingType = TargetingType.Random,
                    ValidTargets = ValidTarget.Enemies, LivingOrDead = LivingOrDead.Living,
                    CombatFunction = BasicDamageFunction.FunctionName,
                });
            }
            else
            {
                engine.SubmitCommand(new CombatCommand
                {
                    ActorId = entityId, TargetingType = TargetingType.Self,
                    ValidTargets = ValidTarget.Enemies, LivingOrDead = LivingOrDead.Living,
                    CombatFunction = NoOpFunction.FunctionName,
                });
            }
        };

        engine.BeginCombat();
        return capturedCmd!.ChosenTargets;
    }

    [Fact]
    public void ExpandAutoTargets_Random_NumAttacksOne_PicksSingleTarget()
    {
        // What: verifies the baseline case of TargetingType.Random expansion — with
        //       numAttacks=1, exactly one target should be chosen from the living-enemy pool.
        // How:  Delegates to CaptureAllyRandomTargets(numAttacks: 1, allowMultiple: false),
        //       which uses a ControlledRandom fixed at 0.1 (so Next(maxValue) always resolves
        //       to index 0) against enemies e1 (pool[0]) and e2 (pool[1]). With only one attack
        //       requested, ExpandAutoTargets' Random case should produce a single-element
        //       ChosenTargets list pointing at pool[0], i.e. "e1". The test asserts the picks
        //       list is exactly ["e1"].
        var picks = CaptureAllyRandomTargets(numAttacks: 1, allowMultiple: false);

        Assert.Equal(["e1"], picks);
    }

    [Fact]
    public void ExpandAutoTargets_Random_WithReplacement_CanRepeatSameTarget()
    {
        // What: verifies that when AllowMultipleAttackOnSameTarget is true, a multi-attack
        //       Random command is allowed to pick the same target repeatedly (sampling with
        //       replacement) rather than being forced to spread hits across distinct enemies.
        // How:  CaptureAllyRandomTargets(numAttacks: 3, allowMultiple: true) uses the same
        //       ControlledRandom fixed at 0.1, so every one of the 3 picks resolves to
        //       pool[Next(maxValue)] = pool[0]. Since replacement is allowed, nothing prevents
        //       the same "e1" from being chosen all three times — this is exactly what should
        //       happen given the fixed RNG, and it demonstrates that duplicate picks are
        //       possible at all under this mode (as opposed to always being deduplicated). The
        //       test asserts the picks list is exactly ["e1", "e1", "e1"].
        var picks = CaptureAllyRandomTargets(numAttacks: 3, allowMultiple: true);

        Assert.Equal(["e1", "e1", "e1"], picks);
    }

    [Fact]
    public void ExpandAutoTargets_Random_WithoutReplacement_CapsAtPoolSizeAndPicksDistinct()
    {
        // What: verifies that when AllowMultipleAttackOnSameTarget is false, a multi-attack
        //       Random command cannot pick more targets than the pool actually contains, and
        //       every pick within that cap must be a distinct entity.
        // How:  CaptureAllyRandomTargets(numAttacks: 3, allowMultiple: false) requests 3 hits
        //       against a pool that only has 2 living enemies (e1, e2). Since duplicates are
        //       disallowed, the picks must be capped at the pool size (2) rather than somehow
        //       forcing a third pick or throwing, and each of those 2 picks must be a different
        //       enemy. The test asserts the picks list is exactly ["e1", "e2"] — both members
        //       of the pool, each exactly once.
        var picks = CaptureAllyRandomTargets(numAttacks: 3, allowMultiple: false);

        Assert.Equal(["e1", "e2"], picks);
    }

    // ---------------------------------------------------------------
    // TargetingType.Choose: routes through CombatFlowState.WaitingForTargetSelection
    // instead of auto-expanding, pausing the flow machine until
    // CombatEngineClass.SubmitTargets(...) is called.
    // ---------------------------------------------------------------

    [Fact]
    public void Choose_PausesForTargetSelection_ThenAppliesChosenTargetOnSubmit()
    {
        // What: verifies that TargetingType.Choose pauses combat and asks the caller to pick a
        //       target (via TargetSelectionRequested) instead of auto-expanding — and that
        //       once a valid choice is submitted via SubmitTargets, the action actually
        //       resolves and applies to the chosen target.
        // How:  The ally submits a Choose command against two enemies (e1, e2). Because Choose
        //       isn't handled by ExpandAutoTargets, the flow machine should transition to
        //       WaitingForTargetSelection and raise TargetSelectionRequested with the actor id,
        //       targeting type, the valid target id pool, and the requested attack count — the
        //       test captures all of that. Critically, ActionResolved must NOT have fired yet
        //       at this point, since the action can't resolve until a target is chosen. The
        //       test then asserts the request matches expectations (actor "ally", type Choose,
        //       valid ids ["e1", "e2"], numAttacks 1) and that no action has resolved. Only
        //       after calling engine.SubmitTargets(["e2"]) should the action actually resolve
        //       and damage land on "e2" — the test asserts both of those become true only
        //       after the explicit SubmitTargets call.
        var engine = new CombatEngineClass(new Random(0));
        var ally = MakeEntity("ally", speed: 10);
        var e1   = MakeEntity("e1", speed: 5);
        var e2   = MakeEntity("e2", speed: 4);
        engine.InitCombat(allies: [ally], enemies: [e1, e2]);

        (string actorId, TargetingType type, IReadOnlyList<string> validIds, int numAttacks)? request = null;
        CombatEventBus.TargetSelectionRequested += (actorId, _, type, validIds, _, numAttacks, _) =>
            request ??= (actorId, type, validIds, numAttacks);

        bool actionResolved = false;
        CombatEventBus.ActionResolved += (_, _) => actionResolved = true;

        CombatEventBus.WaitingForTurn += (entityId, _, _, isAlly) =>
        {
            if (isAlly)
            {
                engine.SubmitCommand(new CombatCommand
                {
                    ActorId = entityId, TargetingType = TargetingType.Choose,
                    ValidTargets = ValidTarget.Enemies, LivingOrDead = LivingOrDead.Living,
                    CombatFunction = BasicDamageFunction.FunctionName,
                });
            }
            else
            {
                engine.SubmitCommand(new CombatCommand
                {
                    ActorId = entityId, TargetingType = TargetingType.Self,
                    ValidTargets = ValidTarget.Enemies, LivingOrDead = LivingOrDead.Living,
                    CombatFunction = NoOpFunction.FunctionName,
                });
            }
        };

        engine.BeginCombat(); // ally acts first and pauses here — no enemy turn has happened yet

        Assert.NotNull(request);
        Assert.Equal("ally", request!.Value.actorId);
        Assert.Equal(TargetingType.Choose, request.Value.type);
        Assert.Equal(["e1", "e2"], request.Value.validIds);
        Assert.Equal(1, request.Value.numAttacks);
        Assert.False(actionResolved, "Combat must not resolve the action before targets are submitted.");

        string? damagedId = null;
        CombatEventBus.EntityDamaged += (targetId, _, _, _, _, _) => damagedId ??= targetId;

        engine.SubmitTargets(["e2"]);

        Assert.True(actionResolved);
        Assert.Equal("e2", damagedId);
    }

    [Fact]
    public void SubmitTargets_WithIdOutsideTheOfferedPool_Throws()
    {
        // What: verifies SubmitTargets rejects a submission if it contains any id that wasn't
        //       part of the originally offered valid-targets pool — you can't retarget an
        //       ally-only Choose command onto an ally.
        // How:  The ally submits a Choose command against a single enemy, so the offered pool
        //       (surfaced via TargetSelectionRequested, though not directly asserted here) is
        //       just ["enemy"]. SubmitTargets recomputes the valid id set via the same
        //       GetValidTargets call and rejects any submitted id not present in it. The test
        //       calls engine.SubmitTargets(["ally"]) — "ally" was never a valid target for this
        //       command — and asserts this throws InvalidOperationException.
        var engine = new CombatEngineClass(new Random(0));
        var ally  = MakeEntity("ally", speed: 10);
        var enemy = MakeEntity("enemy", speed: 5, power: 0);
        engine.InitCombat(allies: [ally], enemies: [enemy]);

        CombatEventBus.WaitingForTurn += (entityId, _, _, isAlly) =>
        {
            if (isAlly)
            {
                engine.SubmitCommand(new CombatCommand
                {
                    ActorId = entityId, TargetingType = TargetingType.Choose,
                    ValidTargets = ValidTarget.Enemies, LivingOrDead = LivingOrDead.Living,
                    CombatFunction = BasicDamageFunction.FunctionName,
                });
            }
            else
            {
                engine.SubmitCommand(new CombatCommand
                {
                    ActorId = entityId, TargetingType = TargetingType.Self,
                    ValidTargets = ValidTarget.Enemies, LivingOrDead = LivingOrDead.Living,
                    CombatFunction = NoOpFunction.FunctionName,
                });
            }
        };

        engine.BeginCombat(); // pauses, offered pool was ["enemy"]

        // "ally" was never offered — not in the ["enemy"] pool.
        Assert.Throws<InvalidOperationException>(() => engine.SubmitTargets(["ally"]));
    }

    [Fact]
    public void SubmitTargets_WithOneValidAndOneInvalidId_Throws()
    {
        // What: verifies the id validation in SubmitTargets checks every submitted id, not
        //       just whether at least one is valid — a mixed submission (one legitimate target
        //       plus one bogus one) must be rejected in full, not partially accepted.
        // How:  The ally submits a Choose command (NumAttacks=2) against two enemies
        //       (e1, e2), so the offered pool is ["e1", "e2"]. SubmitTargets computes the
        //       invalid subset as `chosenTargets.Where(id => !validIds.Contains(id))`, so even
        //       though "e1" is legitimate, including "ally" (never offered) in the same call
        //       means the invalid list is non-empty and the whole call should throw. The test
        //       calls engine.SubmitTargets(["e1", "ally"]) and asserts it throws
        //       InvalidOperationException, confirming partial validity isn't good enough.
        var engine = new CombatEngineClass(new Random(0));
        var ally = MakeEntity("ally", speed: 10);
        var e1   = MakeEntity("e1", speed: 5);
        var e2   = MakeEntity("e2", speed: 4);
        engine.InitCombat(allies: [ally], enemies: [e1, e2]);

        CombatEventBus.WaitingForTurn += (entityId, _, _, isAlly) =>
        {
            if (isAlly)
            {
                engine.SubmitCommand(new CombatCommand
                {
                    ActorId = entityId, TargetingType = TargetingType.Choose,
                    ValidTargets = ValidTarget.Enemies, LivingOrDead = LivingOrDead.Living,
                    NumAttacks = 2, CombatFunction = BasicDamageFunction.FunctionName,
                });
            }
            else
            {
                engine.SubmitCommand(new CombatCommand
                {
                    ActorId = entityId, TargetingType = TargetingType.Self,
                    ValidTargets = ValidTarget.Enemies, LivingOrDead = LivingOrDead.Living,
                    CombatFunction = NoOpFunction.FunctionName,
                });
            }
        };

        engine.BeginCombat(); // pauses, offered pool was ["e1", "e2"]

        // "e1" is valid but "ally" is not — the whole submission must be rejected.
        Assert.Throws<InvalidOperationException>(() => engine.SubmitTargets(["e1", "ally"]));
    }
}
