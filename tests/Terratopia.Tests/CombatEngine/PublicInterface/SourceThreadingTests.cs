using CombatEngine;
using CombatEngine.CombatFunctions;
using CombatEngine.DataClasses;
using CombatEngine.Engine;
using CombatEngine.Enums;

namespace Terratopia.Tests.CombatEngine.PublicInterface;

// Exercises CombatCommand.SourceId/SourceName end-to-end: that the tech/item/monster-action
// identity set on a command reaches every effect event CombatEventBus raises as a result of it,
// including regen/drain ticks that fire in later rounds with no CombatCommand in scope at all -
// CombatEntity persists the originating source on the timed entry itself (see
// CombatEntity.AddRegenDrain/TickRegensDrains).
[Collection("CombatEngineSerial")]
public class SourceThreadingTests
{
    // ally acts on every one of its turns via `allyTurn(turnNumber)`; the enemy always passes.
    // Durable enemy (1000 HP) so the fight only ends once `allyTurn` submits an overkill hit.
    private static (CombatEngineClass engine, CombatEntity ally, CombatEntity enemy) SetupCombat(
        Func<int, CombatCommand> allyTurn)
    {
        var engine = new CombatEngineClass(new Random(0));

        var ally = new CombatEntity(
            entityId: "ally", name: "Ally", level: 1,
            maxHp: 100, hp: 50, maxTp: 50, tp: 50,
            power: 20, defense: 0, speed: 10,
            evasion: 0.0f, critChance: 0.0f, critModifier: 0.0f);

        var enemy = new CombatEntity(
            entityId: "enemy", name: "Enemy", level: 1,
            maxHp: 1000, hp: 1000, maxTp: 0, tp: 0,
            power: 0, defense: 0, speed: 1,
            evasion: 0.0f, critChance: 0.0f, critModifier: 0.0f);

        engine.InitCombat(allies: [ally], enemies: [enemy]);

        int allyTurnCount = 0;
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

            allyTurnCount++;
            engine.SubmitCommand(allyTurn(allyTurnCount));
        };

        return (engine, ally, enemy);
    }

    private static CombatCommand OverkillCommand(string sourceId = "", string sourceName = "") => new()
    {
        ActorId        = "ally",
        TargetingType  = TargetingType.Random,
        ValidTargets   = ValidTarget.Enemies,
        LivingOrDead   = LivingOrDead.Living,
        CombatFunction = BasicDamageFunction.FunctionName,
        Parameters     = new CombatFunctionParameters { CalcType = DamageOrHealCalcType.FixedAmount, PowerFactor = 100_000 },
        SourceId       = sourceId,
        SourceName     = sourceName,
    };

    [Fact]
    public void EntityDamaged_ReportsCommandsSourceName()
    {
        var opening = OverkillCommand("fireblast", "Fireblast");
        var (engine, _, _) = SetupCombat(_ => opening);

        string? sourceName = null;
        CombatEventBus.EntityDamaged += (targetId, _, _, _, _, _, name, _, _, _) =>
        {
            if (targetId == "enemy") sourceName ??= name;
        };

        engine.BeginCombat();

        Assert.Equal("Fireblast", sourceName);
    }

    [Fact]
    public void EntityHealed_ReportsCommandsSourceName()
    {
        var (engine, _, _) = SetupCombat(turn => turn == 1
            ? new CombatCommand
            {
                ActorId        = "ally",
                TargetingType  = TargetingType.Self,
                ValidTargets   = ValidTarget.Allies,
                LivingOrDead   = LivingOrDead.Living,
                CombatFunction = BasicHealFunction.FunctionName,
                Parameters     = new CombatFunctionParameters { CalcType = DamageOrHealCalcType.StandardFormula, PowerFactor = 1.0 },
                SourceId       = "cure",
                SourceName     = "Cure",
            }
            : OverkillCommand());

        string? sourceName = null;
        CombatEventBus.EntityHealed += (targetId, _, _, _, _, _, name, _, _) =>
        {
            if (targetId == "ally") sourceName ??= name;
        };

        engine.BeginCombat();

        Assert.Equal("Cure", sourceName);
    }

    [Fact]
    public void RegenDrainTicked_InLaterRounds_StillReportsTheOriginatingActionsName()
    {
        // What: verifies the source recorded when a regen/drain is APPLIED survives onto its
        //       later ticks, even though those ticks fire from CombatEntity.ProcessRegensDrains
        //       with no CombatCommand in scope - CombatEntity must have persisted it on the timed entry
        //       itself (see AddRegenDrain storing SourceId/SourceName on the RegenDrain struct).
        // How:  ally applies a 5-round Hp regen (source "Renew") on turn 1, passes on turn 2, then
        //       finishes the enemy off from turn 3 onward - guaranteeing at least two round-start
        //       ticks (round 2's and round 3's) fire before combat ends.
        var applyRenew = new CombatCommand
        {
            ActorId        = "ally",
            TargetingType  = TargetingType.Self,
            ValidTargets   = ValidTarget.Allies,
            LivingOrDead   = LivingOrDead.Living,
            CombatFunction = BasicHealFunction.FunctionName,
            Parameters     = new CombatFunctionParameters
            {
                CalcType     = DamageOrHealCalcType.StandardFormula,
                PowerFactor  = 0.0,
                RegensDrains = [new RegenDrainSpec { Stat = RegenDrainStat.Hp, Type = RegenDrainType.Positive, Target = BuffDebuffTarget.SelectedTargets, Rounds = 5, UntilRemoved = false }],
            },
            SourceId       = "renew",
            SourceName     = "Renew",
        };
        var pass = new CombatCommand
        {
            ActorId        = "ally",
            TargetingType  = TargetingType.Self,
            ValidTargets   = ValidTarget.Allies,
            LivingOrDead   = LivingOrDead.Living,
            CombatFunction = NoOpFunction.FunctionName,
        };

        var (engine, _, _) = SetupCombat(turn => turn switch
        {
            1 => applyRenew,
            2 => pass,
            _ => OverkillCommand(),
        });

        var tickedSourceNames = new List<string>();
        CombatEventBus.RegenDrainTicked += (entityId, _, _, _, _, _, sourceName) =>
        {
            if (entityId == "ally") tickedSourceNames.Add(sourceName);
        };

        engine.BeginCombat();

        Assert.True(tickedSourceNames.Count >= 2, $"Expected at least 2 ticks, got {tickedSourceNames.Count}.");
        Assert.All(tickedSourceNames, name => Assert.Equal("Renew", name));
    }

    [Fact]
    public void BuffDebuffExpired_OnOppositePolarityCancellation_ReportsBothTheOriginalAndCounteringSourceNames()
    {
        // What: verifies a countering cancellation's BuffDebuffExpired reports the ORIGINAL effect's
        //       source (the one that wore off) as sourceName, and the NEW opposing effect's source as
        //       counteredBySourceName - the UI needs both names at once to render "X countered by Y".
        CombatEventBus.Reset();

        (string SourceName, string CounteredBySourceName)? expired = null;
        CombatEventBus.BuffDebuffExpired += (_, _, _, _, _, _, _, sourceName, _, counteredBySourceName) =>
            expired = (sourceName, counteredBySourceName);

        var entity = new CombatEntity(
            entityId: "ally", name: "Ally", level: 1,
            maxHp: 100, hp: 100, maxTp: 50, tp: 50,
            power: 20, defense: 0, speed: 10,
            evasion: 0.0f, critChance: 0.0f, critModifier: 0.0f);

        entity.AddBuffDebuff(BuffDebuffStat.Power, isPositive: false, roundsRemaining: 5, untilRemoved: false, "weaken", "Weaken");
        entity.AddBuffDebuff(BuffDebuffStat.Power, isPositive: true, roundsRemaining: 3, untilRemoved: false, "powersurge", "Power Surge");

        Assert.Equal(("Weaken", "Power Surge"), expired);
    }

    [Fact]
    public void RegenDrainExpired_OnOppositePolarityCancellation_ReportsBothTheOriginalAndCounteringSourceNames()
    {
        // What: same guarantee as the buff/debuff case above, but for regen/drain cancellation.
        CombatEventBus.Reset();

        (string SourceName, string CounteredBySourceName)? expired = null;
        CombatEventBus.RegenDrainExpired += (_, _, _, _, _, sourceName, _, counteredBySourceName) =>
            expired = (sourceName, counteredBySourceName);

        var entity = new CombatEntity(
            entityId: "ally", name: "Ally", level: 1,
            maxHp: 100, hp: 100, maxTp: 50, tp: 50,
            power: 20, defense: 0, speed: 10,
            evasion: 0.0f, critChance: 0.0f, critModifier: 0.0f);

        entity.AddRegenDrain(RegenDrainStat.Hp, isPositive: false, roundsRemaining: 5, untilRemoved: false, "toxiccloud", "Toxic Cloud");
        entity.AddRegenDrain(RegenDrainStat.Hp, isPositive: true, roundsRemaining: 3, untilRemoved: false, "healingaura", "Healing Aura");

        Assert.Equal(("Toxic Cloud", "Healing Aura"), expired);
    }
}
