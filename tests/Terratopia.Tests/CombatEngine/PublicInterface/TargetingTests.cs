using CombatEngine;
using CombatEngine.CombatFunctions;
using CombatEngine.DataClasses;
using CombatEngine.Engine;
using CombatEngine.Enums;

namespace Terratopia.Tests.CombatEngine.PublicInterface;

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

    // Drives a single Choose command from "actorId" through the public flow and returns the
    // valid-target id pool straight off TargetSelectionRequested — CombatFlowMachine builds that
    // event's validTargetIds by calling the same (internal) GetValidTargets the actor's command
    // would resolve against, so this is the public-interface equivalent of querying it directly.
    // Every other roster member just passes with a harmless self-targeted NoOp, so whichever
    // turn order the (unseeded-for-this-purpose) RNG picks, the flow always pauses here the
    // moment it's actorId's turn — no entity ever takes damage, so there's no risk of combat
    // ending before that happens.
    private static IReadOnlyList<string> CaptureChooseValidIds(
        string actorId, ValidTarget validTargets, LivingOrDead livingOrDead,
        IReadOnlyList<CombatEntity> allies, IReadOnlyList<CombatEntity> enemies)
    {
        var engine = new CombatEngineClass(new Random(0));
        engine.InitCombat(allies: allies, enemies: enemies);

        IReadOnlyList<string>? capturedIds = null;
        CombatEventBus.TargetSelectionRequested += (_, _, _, validIds, _, _, _) => capturedIds ??= validIds;

        CombatEventBus.WaitingForTurn += (entityId, _, _, _) =>
        {
            if (entityId != actorId)
            {
                engine.SubmitCommand(new CombatCommand
                {
                    ActorId = entityId, TargetingType = TargetingType.Self,
                    ValidTargets = ValidTarget.Allies, LivingOrDead = LivingOrDead.Living,
                    CombatFunction = NoOpFunction.FunctionName,
                });
                return;
            }

            engine.SubmitCommand(new CombatCommand
            {
                ActorId = actorId, TargetingType = TargetingType.Choose,
                ValidTargets = validTargets, LivingOrDead = livingOrDead,
                CombatFunction = NoOpFunction.FunctionName,
            });
        };

        engine.BeginCombat();
        return capturedIds!;
    }

    // ---------------------------------------------------------------
    // GetValidTargets: pool selection by ValidTarget/LivingOrDead, observed through the public
    // Choose flow instead of calling the internal GetValidTargets method directly (contrast with
    // Internal/TargetingQueryTests.cs, which keeps the one case — a non-player querying actor —
    // that has no public equivalent, since TargetSelectionRequested never fires for AI actors).
    // ---------------------------------------------------------------

    [Fact]
    public void GetValidTargets_Enemies_FromPlayerActor_ReturnsOnlyEnemies()
    {
        // What: verifies that querying ValidTarget.Enemies for a player-side actor returns the
        //       enemy roster, not the actor's own allies.
        // How:  One ally and two enemies. The ally submits a Choose command for
        //       ValidTarget.Enemies + LivingOrDead.Living; the pool CombatFlowMachine offers
        //       through TargetSelectionRequested should be exactly ["enemy1", "enemy2"], in the
        //       order they were added.
        var ally   = MakeEntity("ally", speed: 10);
        var enemy1 = MakeEntity("enemy1", speed: 10);
        var enemy2 = MakeEntity("enemy2", speed: 10);

        var validIds = CaptureChooseValidIds("ally", ValidTarget.Enemies, LivingOrDead.Living,
            allies: [ally], enemies: [enemy1, enemy2]);

        Assert.Equal(["enemy1", "enemy2"], validIds);
    }

    [Fact]
    public void GetValidTargets_Allies_FromPlayerActor_ExcludesEnemies()
    {
        // What: verifies ValidTarget.Allies returns the actor's own side (including the actor
        //       itself) and excludes the opposing side entirely.
        // How:  Two allies and one enemy. "ally1" submits a Choose command for
        //       ValidTarget.Allies + LivingOrDead.Living; the offered pool should be exactly
        //       ["ally1", "ally2"] — self-targeting is allowed under the Allies pool — with the
        //       enemy nowhere in it.
        var ally1 = MakeEntity("ally1", speed: 10);
        var ally2 = MakeEntity("ally2", speed: 10);
        var enemy = MakeEntity("enemy", speed: 10);

        var validIds = CaptureChooseValidIds("ally1", ValidTarget.Allies, LivingOrDead.Living,
            allies: [ally1, ally2], enemies: [enemy]);

        Assert.Equal(["ally1", "ally2"], validIds);
    }

    [Fact]
    public void GetValidTargets_Both_ReturnsAlliesAndEnemiesTogether()
    {
        // What: verifies ValidTarget.Both merges allies and enemies into a single pool,
        //       ignoring the side distinction entirely.
        // How:  One ally and one enemy. The ally submits a Choose command for
        //       ValidTarget.Both + LivingOrDead.Living; the offered pool should include both
        //       "ally" and "enemy".
        var ally  = MakeEntity("ally", speed: 10);
        var enemy = MakeEntity("enemy", speed: 10);

        var validIds = CaptureChooseValidIds("ally", ValidTarget.Both, LivingOrDead.Living,
            allies: [ally], enemies: [enemy]);

        Assert.Equal(["ally", "enemy"], validIds);
    }

    [Fact]
    public void GetValidTargets_Living_ExcludesDeadEntities()
    {
        // What: verifies that LivingOrDead.Living filters out any entity flagged as dead,
        //       leaving only entities still able to act as valid targets.
        // How:  One ally and two enemies, one of which has IsDead manually set to true before
        //       combat starts (simulating a pre-existing corpse rather than something the
        //       engine killed — the public interface offers no other way to arrange a
        //       pre-dead entity). The ally's Choose command for ValidTarget.Enemies +
        //       LivingOrDead.Living should offer only "livingEnemy".
        var ally        = MakeEntity("ally", speed: 10);
        var deadEnemy   = MakeEntity("deadEnemy", speed: 10);
        deadEnemy.MarkDead();
        var livingEnemy = MakeEntity("livingEnemy", speed: 10);

        var validIds = CaptureChooseValidIds("ally", ValidTarget.Enemies, LivingOrDead.Living,
            allies: [ally], enemies: [deadEnemy, livingEnemy]);

        Assert.Equal(["livingEnemy"], validIds);
    }

    [Fact]
    public void GetValidTargets_Dead_ReturnsOnlyDeadEntities()
    {
        // What: verifies the inverse filter — LivingOrDead.Dead should return only entities
        //       flagged as dead, useful for effects like resurrection that must target corpses.
        // How:  Same one-dead/one-living enemy setup as GetValidTargets_Living_ExcludesDeadEntities,
        //       but the ally's Choose command uses LivingOrDead.Dead instead, so the offered
        //       pool should be exactly ["deadEnemy"].
        var ally        = MakeEntity("ally", speed: 10);
        var deadEnemy   = MakeEntity("deadEnemy", speed: 10);
        deadEnemy.MarkDead();
        var livingEnemy = MakeEntity("livingEnemy", speed: 10);

        var validIds = CaptureChooseValidIds("ally", ValidTarget.Enemies, LivingOrDead.Dead,
            allies: [ally], enemies: [deadEnemy, livingEnemy]);

        Assert.Equal(["deadEnemy"], validIds);
    }

    [Fact]
    public void GetValidTargets_Both_LivingOrDead_ReturnsRegardlessOfDeathState()
    {
        // What: verifies LivingOrDead.Both bypasses the death-state filter entirely, returning
        //       every entity in the ValidTarget pool regardless of whether it's alive or dead.
        // How:  Same one-dead/one-living enemy setup, but the ally's Choose command uses
        //       LivingOrDead.Both, so the offered pool should include both "deadEnemy" and
        //       "livingEnemy" together.
        var ally        = MakeEntity("ally", speed: 10);
        var deadEnemy   = MakeEntity("deadEnemy", speed: 10);
        deadEnemy.MarkDead();
        var livingEnemy = MakeEntity("livingEnemy", speed: 10);

        var validIds = CaptureChooseValidIds("ally", ValidTarget.Enemies, LivingOrDead.Both,
            allies: [ally], enemies: [deadEnemy, livingEnemy]);

        Assert.Equal(["deadEnemy", "livingEnemy"], validIds);
    }

    [Fact]
    public void GetValidTargets_AllEnemiesDead_ReturnsEmptyWithoutThrowing()
    {
        // What: verifies an edge case — when every entity in the requested pool is dead (or
        //       otherwise excluded), the offered pool is empty rather than the flow throwing
        //       or omitting the TargetSelectionRequested event entirely.
        // How:  One ally and a single already-dead enemy. The ally's Choose command for
        //       ValidTarget.Enemies + LivingOrDead.Living should still raise
        //       TargetSelectionRequested, just with an empty valid-id list.
        var ally     = MakeEntity("ally", speed: 10);
        var deadOnly = MakeEntity("deadOnly", speed: 10);
        deadOnly.MarkDead();

        var validIds = CaptureChooseValidIds("ally", ValidTarget.Enemies, LivingOrDead.Living,
            allies: [ally], enemies: [deadOnly]);

        Assert.Empty(validIds);
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
        e3.MarkDead(); // pre-dead: must never appear in an "All" expansion

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
        //       test captures all of that. Critically, the ally's turn must NOT have ended yet
        //       at this point, since the action can't resolve until a target is chosen. The
        //       test then asserts the request matches expectations (actor "ally", type Choose,
        //       valid ids ["e1", "e2"], numAttacks 1) and that the ally's turn hasn't ended. Only
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

        bool allyTurnEnded = false;
        CombatEventBus.TurnEnded += (entityId, _) => allyTurnEnded |= entityId == "ally";

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
        Assert.False(allyTurnEnded, "The ally's turn must not end before targets are submitted.");

        string? damagedId = null;
        CombatEventBus.EntityDamaged += (targetId, _, _, _, _, _, _, _, _, _) => damagedId ??= targetId;

        engine.SubmitTargets(["e2"]);

        Assert.True(allyTurnEnded);
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
