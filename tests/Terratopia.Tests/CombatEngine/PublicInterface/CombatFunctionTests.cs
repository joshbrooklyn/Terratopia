using CombatEngine;
using CombatEngine.CombatFunctions;
using CombatEngine.DataClasses;
using CombatEngine.Engine;
using CombatEngine.Enums;

namespace Terratopia.Tests.CombatEngine.PublicInterface;

[Collection("CombatEngineSerial")]
public class CombatFunctionTests
{
    // ---------------------------------------------------------------
    // Execution: shared scenario
    // ---------------------------------------------------------------

    // One ally and one 1-HP enemy. The ally spends its FIRST turn on `openingMove` (the function
    // under test) and every later turn attacking, which kills the enemy and ends combat — without
    // that second phase the fight would run forever, since the enemy only ever passes.
    //
    // Ally is Power=10/Level=1 vs Defense=0, so BasicDamage deals exactly 25 and BasicHeal (the
    // same formula minus the defense divisor) restores exactly 25. Evasion and crit are 0, so
    // neither side's rolls change any number here.
    private static (CombatEngineClass engine, CombatEntity ally, CombatEntity enemy) SetupCombat(
        CombatCommand openingMove,
        int allyHp    = 100,
        int allyMaxHp = 100,
        int allyTp    = 50,
        IReadOnlyList<CombatEntity>? extraAllies = null,
        Action<CombatEngineClass>? onTargetSelectionRequested = null)
    {
        var engine = new CombatEngineClass(new Random(0));

        var ally = new CombatEntity(
            entityId: "ally", name: "Ally", level: 1,
            maxHp: allyMaxHp, hp: allyHp, maxTp: 50, tp: allyTp,
            power: 10, defense: 0, speed: 10,
            evasion: 0.0f, critChance: 0.0f, critModifier: 0.0f);

        var enemy = new CombatEntity(
            entityId: "enemy", name: "Enemy", level: 1,
            maxHp: 1, hp: 1, maxTp: 0, tp: 0,
            power: 0, defense: 0, speed: 5,
            evasion: 0.0f, critChance: 0.0f, critModifier: 0.0f);

        engine.InitCombat(
            allies:  [ally, .. extraAllies ?? []],
            enemies: [enemy]);

        // Wire AFTER InitCombat (which calls CombatEventBus.Reset()).
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

            if (entityId == "ally" && !openingMoveUsed)
            {
                openingMoveUsed = true;
                engine.SubmitCommand(openingMove);
                return;
            }

            // Closing move: kill the enemy so combat terminates.
            engine.SubmitCommand(new CombatCommand
            {
                ActorId        = entityId,
                TargetingType  = TargetingType.Random,
                ValidTargets   = ValidTarget.Enemies,
                LivingOrDead   = LivingOrDead.Living,
                CombatFunction = BasicDamageFunction.FunctionName,
                Parameters     = new CombatFunctionParameters { PowerFactor = 1.0 },
            });
        };

        if (onTargetSelectionRequested is not null)
        {
            CombatEventBus.TargetSelectionRequested += (_, _, _, _, _, _, _) => onTargetSelectionRequested(engine);
        }

        return (engine, ally, enemy);
    }

    private static CombatCommand SelfCommand(string combatFunction, int tpCost = 0, double? powerFactor = null) => new()
    {
        ActorId        = "ally",
        TargetingType  = TargetingType.Self,
        ValidTargets   = ValidTarget.Allies,
        LivingOrDead   = LivingOrDead.Living,
        TPCost         = tpCost,
        CombatFunction = combatFunction,
        Parameters     = new CombatFunctionParameters { PowerFactor = powerFactor },
    };

    // ---------------------------------------------------------------
    // TP is now the function's responsibility, not the engine's
    // ---------------------------------------------------------------

    [Fact]
    public void BasicDamage_DeductsTpCost_AndRaisesEntityTpChanged()
    {
        // What: verifies BasicDamage still pays the command's TP cost now that TP deduction has
        //       moved out of ResolveAction and behind an injected delegate the function calls.
        // How:  ResolveAction used to deduct TP itself, so every action paid whether or not it
        //       wanted to; under the CombatFunction design each function calls ctx.DeductTpCost().
        //       The ally starts with 50 TP and opens with a 10-TP BasicDamage, so the first
        //       EntityTpChanged for "ally" should report 50 → 40. The test captures that first
        //       transition and asserts both endpoints, proving the delegate is wired and invoked.
        var opening = SelfCommand(BasicDamageFunction.FunctionName, tpCost: 10, powerFactor: 0.0);
        var (engine, ally, _) = SetupCombat(opening);

        (int oldTp, int newTp)? firstChange = null;
        CombatEventBus.EntityTpChanged += (entityId, _, oldTp, newTp, _, _) =>
        {
            if (entityId == "ally") firstChange ??= (oldTp, newTp);
        };

        engine.BeginCombat();

        Assert.Equal((50, 40), firstChange);
        Assert.Equal(40, ally.Tp);
    }

    [Fact]
    public void NoOp_DealsNoDamage_ButStillPaysTpCost()
    {
        // What: verifies NoOp is a real, payable action — it burns the turn and the TP without
        //       touching any target.
        // How:  NoOp is the named replacement for the old `DirectEffects = []` idiom, which used
        //       to mean "resolve to nothing" implicitly. Now that combatFunction is required, that
        //       idiom needs a name, and the name must still behave like a turn that was spent.
        //       The ally opens with a 10-TP NoOp aimed at itself: the test asserts its HP is
        //       untouched (no damage, no healing) while its TP dropped by exactly the cost.
        var opening = SelfCommand(NoOpFunction.FunctionName, tpCost: 10);
        var (engine, ally, _) = SetupCombat(opening);

        bool allyHpChanged = false;
        CombatEventBus.EntityDamaged += (entityId, _, _, _, _, _, _, _, _, _) =>
        {
            if (entityId == "ally") allyHpChanged = true;
        };
        CombatEventBus.EntityHealed += (entityId, _, _, _, _, _, _, _, _) =>
        {
            if (entityId == "ally") allyHpChanged = true;
        };

        engine.BeginCombat();

        Assert.False(allyHpChanged, "NoOp should never change any entity's HP.");
        Assert.Equal(100, ally.Hp);
        Assert.Equal(40, ally.Tp);
    }

    // ---------------------------------------------------------------
    // BasicHeal
    // ---------------------------------------------------------------

    [Fact]
    public void BasicHeal_RestoresHp_UsingFormulaWithoutDefenseDivisor()
    {
        // What: verifies BasicHeal restores the standard formula's base amount — the damage
        //       formula with the target's Defense divisor skipped, since healing ignores defense.
        // How:  The ally is Power=10 at Level=1 with powerFactor 1.0, so the shared base amount is
        //       (10 × 1.0 × 2) + (1 × 5) = 25. Starting at 10 of 100 HP, a self-cast BasicHeal
        //       should take it to exactly 35 and report an EntityHealed amount of 25. The test
        //       asserts both the event payload and the resulting HP, pinning the arithmetic.
        var opening = SelfCommand(BasicHealFunction.FunctionName, powerFactor: 1.0);
        var (engine, ally, _) = SetupCombat(opening, allyHp: 10);

        int? healedAmount = null;
        CombatEventBus.EntityHealed += (entityId, _, amount, _, _, _, _, _, _) =>
        {
            if (entityId == "ally") healedAmount ??= amount;
        };

        engine.BeginCombat();

        Assert.Equal(25, healedAmount);
        Assert.Equal(35, ally.Hp);
    }

    [Fact]
    public void BasicHeal_CapsAtMaxHp_AndReportsOnlyTheAppliedAmount()
    {
        // What: verifies healing cannot push an entity past MaxHp, and that EntityHealed reports
        //       the HP actually restored rather than the raw computed amount.
        // How:  The ally starts at 90 of 100 HP and heals for a computed 25 (see the test above).
        //       ApplyHeal clamps the result with Math.Min(MaxHp, Hp + amount), so HP should land
        //       on exactly 100, not 115. The event is raised with `target.Hp - oldHp`, so the
        //       reported amount should be the applied 10 rather than the computed 25 — otherwise
        //       a UI floating-number would overstate what the heal did.
        var opening = SelfCommand(BasicHealFunction.FunctionName, powerFactor: 1.0);
        var (engine, ally, _) = SetupCombat(opening, allyHp: 90);

        int? healedAmount = null;
        CombatEventBus.EntityHealed += (entityId, _, amount, _, _, _, _, _, _) =>
        {
            if (entityId == "ally") healedAmount ??= amount;
        };

        engine.BeginCombat();

        Assert.Equal(10, healedAmount);
        Assert.Equal(100, ally.Hp);
    }

    [Fact]
    public void BasicHeal_OnDeadTarget_IsNoOp()
    {
        // What: verifies healing a dead entity does nothing — revival is deliberately NOT a side
        //       effect of healing, but a future dedicated CombatFunction.
        // How:  A dead target is normally unreachable (ExpandAutoTargets draws from living pools
        //       only), so this reaches it the one way data can: TargetingType.Choose with
        //       LivingOrDead.Dead over Allies, which makes GetValidTargets return the corpse, then
        //       answering TargetSelectionRequested by submitting the corpse's id. BasicHeal skips
        //       IsDead targets and ApplyHeal guards again, so the corpse should stay dead at 0 HP
        //       with no EntityHealed and no EntityRevived raised for it.
        var corpse = new CombatEntity(
            entityId: "corpse", name: "Corpse", level: 1,
            maxHp: 100, hp: 0, maxTp: 0, tp: 0,
            power: 0, defense: 0, speed: 1,
            evasion: 0.0f, critChance: 0.0f, critModifier: 0.0f);
        corpse.MarkDead();

        var opening = new CombatCommand
        {
            ActorId        = "ally",
            TargetingType  = TargetingType.Choose,
            ValidTargets   = ValidTarget.Allies,
            LivingOrDead   = LivingOrDead.Dead,
            CombatFunction = BasicHealFunction.FunctionName,
            Parameters     = new CombatFunctionParameters { PowerFactor = 1.0 },
        };

        var (engine, _, _) = SetupCombat(
            opening,
            extraAllies: [corpse],
            onTargetSelectionRequested: e => e.SubmitTargets(["corpse"]));

        bool corpseHealed  = false;
        bool corpseRevived = false;
        CombatEventBus.EntityHealed  += (entityId, _, _, _, _, _, _, _, _) => { if (entityId == "corpse") corpseHealed  = true; };
        CombatEventBus.EntityRevived += (entityId, _, _, _, _, _)          => { if (entityId == "corpse") corpseRevived = true; };

        engine.BeginCombat();

        Assert.False(corpseHealed,  "Healing a dead target should raise no EntityHealed.");
        Assert.False(corpseRevived, "BasicHeal should never revive — that is a separate function.");
        Assert.Equal(0, corpse.Hp);
        Assert.True(corpse.IsDead);
    }

    [Fact]
    public void BasicHeal_PercentOfMax_UsesTargetsMaxHp_NotActors()
    {
        // What: verifies DamageOrHealCalcType.PercentOfMax bases the healed amount on the HEALED
        //       target's MaxHp, not the caster's — CalculateHealAmount takes a target parameter
        //       specifically so this works even though the actor is the one whose Power/Level
        //       would normally matter.
        // How:  "ally" (MaxHp=100, the caster) heals "friend" (MaxHp=500) for powerFactor=0.1
        //       (10%). If PercentOfMax used the caster's own MaxHp the amount would be 10; using
        //       the target's MaxHp it is 50. friend starts at 100/500 HP so the heal doesn't cap.
        var friend = new CombatEntity(
            entityId: "friend", name: "Friend", level: 1,
            maxHp: 500, hp: 100, maxTp: 0, tp: 0,
            power: 0, defense: 0, speed: 1,
            evasion: 0.0f, critChance: 0.0f, critModifier: 0.0f);

        var opening = new CombatCommand
        {
            ActorId        = "ally",
            TargetingType  = TargetingType.Choose,
            ValidTargets   = ValidTarget.Allies,
            LivingOrDead   = LivingOrDead.Living,
            CombatFunction = BasicHealFunction.FunctionName,
            Parameters     = new CombatFunctionParameters { PowerFactor = 0.1, CalcType = DamageOrHealCalcType.PercentOfMax },
        };

        var (engine, _, _) = SetupCombat(
            opening,
            extraAllies: [friend],
            onTargetSelectionRequested: e => e.SubmitTargets(["friend"]));

        int? healedAmount = null;
        CombatEventBus.EntityHealed += (entityId, _, amount, _, _, _, _, _, _) =>
        {
            if (entityId == "friend") healedAmount ??= amount;
        };

        engine.BeginCombat();

        Assert.Equal(50, healedAmount);
        Assert.Equal(150, friend.Hp);
    }

    [Fact]
    public void BasicHeal_ConsumesNoRandomness()
    {
        // What: verifies BasicHeal draws nothing from the engine's RNG, so introducing a heal into
        //       a fight cannot shift any downstream roll.
        // How:  BasicHeal deliberately skips both TryEvade and RollCrit (a heal on an ally is
        //       neither dodgeable nor critical), which is what keeps every other seeded test in
        //       this suite stable. The engine's Random is shared with TurnOrderManager, so an
        //       extra draw would perturb the turn order of the NEXT round. The test runs the same
        //       seeded scenario twice — once opening with BasicHeal, once with NoOp (which is
        //       known to consume nothing) — and asserts the recorded turn orders are identical.
        //       A stray NextSingle() inside BasicHeal would desynchronise round 2 and fail here.
        static List<string> RunAndRecordTurnOrders(string combatFunction)
        {
            var opening = SelfCommand(combatFunction, powerFactor: 1.0);
            var (engine, _, _) = SetupCombat(opening, allyHp: 10);

            var turnOrders = new List<string>();
            CombatEventBus.RoundStarted += (round, ids, _) => turnOrders.Add($"{round}:{string.Join(",", ids)}");

            engine.BeginCombat();
            return turnOrders;
        }

        Assert.Equal(RunAndRecordTurnOrders(NoOpFunction.FunctionName),
                     RunAndRecordTurnOrders(BasicHealFunction.FunctionName));
    }
}
