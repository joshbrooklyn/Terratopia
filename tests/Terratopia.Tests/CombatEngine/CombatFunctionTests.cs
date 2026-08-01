using System.Reflection;
using System.Text.Json;
using CombatEngine;
using CombatEngine.CombatFunctions;
using CombatEngine.DataClasses;
using CombatEngine.Engine;
using CombatEngine.Enums;
using GameEngine.DataClasses;

namespace Terratopia.Tests.CombatEngine;

[Collection("CombatEngineSerial")]
public class CombatFunctionTests
{
    // The three action schemas that carry combatFunction/parameters. Each is a hand-maintained
    // mirror of the C# side, so every one of them gets checked for drift.
    private static readonly string[] ActionSchemaResources =
    [
        Tech.SchemaResourceName,
        Item.SchemaResourceName,
        MonsterAction.SchemaResourceName,
    ];

    private static JsonElement LoadSchema(string resourceName)
    {
        using var stream = Assembly.GetAssembly(typeof(Tech))!.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded schema '{resourceName}' not found.");
        return JsonDocument.Parse(stream).RootElement.Clone();
    }

    // Mirrors JsonNamingPolicy.CamelCase, which is how ContentLoader maps these property names.
    private static string CamelCase(string name) => char.ToLowerInvariant(name[0]) + name[1..];

    // ---------------------------------------------------------------
    // Registry resolution
    // ---------------------------------------------------------------

    [Fact]
    public void CombatFunctionRegistry_ResolvesRegisteredFunctionsByName()
    {
        // What: verifies the registry maps each registered FunctionName string — the exact value
        //       that appears in GameData JSON — back to its implementing class.
        // How:  Resolve is called with each of the three shipped names and the concrete runtime
        //       type of the returned instance is asserted. This is the whole point of the
        //       refactor: a name in data selects the class that resolves the action, so if a
        //       FunctionName const and its registry entry ever disagree, this fails.
        Assert.IsType<BasicDamageFunction>(CombatFunctionRegistry.Resolve(BasicDamageFunction.FunctionName));
        Assert.IsType<BasicHealFunction>(CombatFunctionRegistry.Resolve(BasicHealFunction.FunctionName));
        Assert.IsType<NoOpFunction>(CombatFunctionRegistry.Resolve(NoOpFunction.FunctionName));
    }

    [Fact]
    public void CombatFunctionRegistry_ThrowsOnUnknownName()
    {
        // What: verifies an unregistered name is a hard failure rather than a silent no-op.
        // How:  Resolve is called with a name that is deliberately not in the registry. This is
        //       the explicit contrast with PowerKeywordRegistry, which drops unknown keyword
        //       names silently — that is tolerable for a keyword (an action just loses a bonus)
        //       but not for a CombatFunction, which IS the action: dropping it would turn a Tech
        //       into a do-nothing instead of surfacing the typo. The message is asserted to name
        //       the offending value so the failure points at the bad data file.
        var ex = Assert.Throws<InvalidOperationException>(() => CombatFunctionRegistry.Resolve("NotAFunction"));
        Assert.Contains("NotAFunction", ex.Message);
    }

    // ---------------------------------------------------------------
    // Hand-synced schema surfaces — drift guards
    // ---------------------------------------------------------------

    [Fact]
    public void CombatFunctionRegistry_MatchesSchemaEnum()
    {
        // What: verifies the combatFunction enum in every action schema lists exactly the
        //       functions the registry actually has registered.
        // How:  The schemas are hand-maintained against CombatFunctionRegistry (the same
        //       arrangement the keywords enum has), so they can silently drift: a new function
        //       that never reaches the schema is unauthorable, and a schema entry with no
        //       registered class passes load-time validation only to throw mid-combat. This
        //       reads each embedded schema resource, pulls properties.combatFunction.enum, and
        //       asserts it is set-equal to CombatFunctionRegistry.RegisteredNames.
        var registered = CombatFunctionRegistry.RegisteredNames.OrderBy(n => n).ToArray();

        foreach (var resourceName in ActionSchemaResources)
        {
            var schemaEnum = LoadSchema(resourceName)
                .GetProperty("properties").GetProperty("combatFunction").GetProperty("enum")
                .EnumerateArray().Select(e => e.GetString()!).OrderBy(n => n).ToArray();

            Assert.Equal(registered, schemaEnum);
        }
    }

    [Fact]
    public void CombatFunctionParameters_MatchesSchemaSuperset()
    {
        // What: verifies the parameters block in every action schema declares exactly the fields
        //       CombatFunctionParameters exposes — no more, no less.
        // How:  parameters is a closed (additionalProperties: false) hand-maintained superset, so
        //       a field present in C# but missing from the schema is unauthorable, and a field in
        //       the schema with no C# property is silently discarded on deserialization. This
        //       reflects over CombatFunctionParameters, camelCases each property name the way
        //       ContentLoader's JsonNamingPolicy.CamelCase does, and asserts set equality against
        //       properties.parameters.properties in each schema.
        var expected = typeof(CombatFunctionParameters)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => CamelCase(p.Name))
            .OrderBy(n => n)
            .ToArray();

        foreach (var resourceName in ActionSchemaResources)
        {
            var schemaFields = LoadSchema(resourceName)
                .GetProperty("properties").GetProperty("parameters").GetProperty("properties")
                .EnumerateObject().Select(p => p.Name).OrderBy(n => n).ToArray();

            Assert.Equal(expected, schemaFields);
        }
    }

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
        CombatEventBus.EntityTpChanged += (entityId, _, oldTp, newTp) =>
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
        CombatEventBus.EntityHpChanged += (entityId, _, _, _) =>
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
        CombatEventBus.EntityHealed += (entityId, _, amount, _, _) =>
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
        CombatEventBus.EntityHealed += (entityId, _, amount, _, _) =>
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
        corpse.IsDead = true;

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
        CombatEventBus.EntityHealed  += (entityId, _, _, _, _) => { if (entityId == "corpse") corpseHealed  = true; };
        CombatEventBus.EntityRevived += (entityId, _)          => { if (entityId == "corpse") corpseRevived = true; };

        engine.BeginCombat();

        Assert.False(corpseHealed,  "Healing a dead target should raise no EntityHealed.");
        Assert.False(corpseRevived, "BasicHeal should never revive — that is a separate function.");
        Assert.Equal(0, corpse.Hp);
        Assert.True(corpse.IsDead);
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
