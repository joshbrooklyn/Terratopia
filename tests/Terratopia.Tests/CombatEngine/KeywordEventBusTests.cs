using CombatEngine;
using CombatEngine.CombatFunctions;
using CombatEngine.DataClasses;
using CombatEngine.Engine;
using CombatEngine.Enums;
using CombatEngine.Keywords;

namespace Terratopia.Tests.CombatEngine;

[Collection("CombatEngineSerial")]
public class KeywordEventBusTests
{
    [Fact]
    public void MetCondition_RaisesKeywordAppliedWithCorrectNameAndBonus()
    {
        // What: verifies that when an active keyword's condition is met, the engine raises
        //       CombatEventBus.KeywordApplied carrying the keyword's own name, the acting and
        //       target entity ids, and the exact bonus value the keyword computed - not just
        //       that the damage math silently reflects the bonus.
        // How:  Engage requires the target at >=75% of max HP; enemy starts and stays at full
        //       HP right up to the single killing hit (Engage's GetBonus reads HP before damage
        //       is subtracted), so the condition is met and the bonus is capped at
        //       min(basePowerFactor=1.0, 0.5)=0.5. The test subscribes to KeywordApplied and
        //       asserts it fires exactly once, with the tuple (EngageKeyword.KeywordName,
        //       "ally", "enemy", 0.5).
        var engine = new CombatEngineClass(new Random(0));

        var ally = new CombatEntity(
            entityId: "ally", name: "Ally", level: 1,
            maxHp: 1000, hp: 1000, maxTp: 0, tp: 0,
            power: 10, defense: 0, speed: 100,
            evasion: 0f, critChance: 0f, critModifier: 0f);

        var enemy = new CombatEntity(
            entityId: "enemy", name: "Enemy", level: 1,
            maxHp: 25, hp: 25, maxTp: 0, tp: 0,
            power: 0, defense: 0, speed: 1,
            evasion: 0f, critChance: 0f, critModifier: 0f);

        engine.InitCombat(allies: [ally], enemies: [enemy]);

        CombatEventBus.WaitingForTurn += (entityId, _, _, isAlly) =>
        {
            if (!isAlly) return; // enemy dies to the ally's hit before ever getting a turn.

            engine.SubmitCommand(new CombatCommand
            {
                ActorId       = entityId,
                TargetingType = TargetingType.Random,
                ValidTargets  = ValidTarget.Enemies,
                LivingOrDead  = LivingOrDead.Living,
                TPCost        = 0,
                Keywords      = [EngageKeyword.KeywordName],
                CombatFunction = BasicDamageFunction.FunctionName,
                Parameters = new CombatFunctionParameters { CalcType = DamageCalcType.StandardFormula, PowerFactor = 1.0 },
            });
        };

        var applied = new List<(string keyword, string actorId, string targetId, double bonus)>();
        CombatEventBus.KeywordApplied += (keywordName, actorId, _, targetId, _, bonus) =>
            applied.Add((keywordName, actorId, targetId, bonus));

        engine.BeginCombat();

        var single = Assert.Single(applied);
        Assert.Equal((EngageKeyword.KeywordName, "ally", "enemy", 0.5), single);
    }

    [Fact]
    public void UnmetCondition_DoesNotRaiseKeywordApplied()
    {
        // What: verifies the converse of MetCondition_RaisesKeywordAppliedWithCorrectNameAndBonus
        //       - an active keyword whose condition is never satisfied must never raise
        //       KeywordApplied, even though it's attached to every command submitted.
        // How:  Cruel requires the target at <=25% of max HP. The enemy starts and stays at
        //       full HP right up to the killing blow (GetBonus reads HP before damage is
        //       subtracted), so the condition is never met across the whole fight. The test
        //       counts every KeywordApplied firing and asserts the list stays empty, while also
        //       confirming the enemy still actually dies (i.e. combat ran and Cruel's absence
        //       wasn't just because nothing happened).
        var engine = new CombatEngineClass(new Random(0));

        var ally = new CombatEntity(
            entityId: "ally", name: "Ally", level: 1,
            maxHp: 1000, hp: 1000, maxTp: 0, tp: 0,
            power: 10, defense: 0, speed: 100,
            evasion: 0f, critChance: 0f, critModifier: 0f);

        var enemy = new CombatEntity(
            entityId: "enemy", name: "Enemy", level: 1,
            maxHp: 25, hp: 25, maxTp: 0, tp: 0,
            power: 0, defense: 0, speed: 1,
            evasion: 0f, critChance: 0f, critModifier: 0f);

        engine.InitCombat(allies: [ally], enemies: [enemy]);

        CombatEventBus.WaitingForTurn += (entityId, _, _, isAlly) =>
        {
            if (!isAlly) return;

            engine.SubmitCommand(new CombatCommand
            {
                ActorId       = entityId,
                TargetingType = TargetingType.Random,
                ValidTargets  = ValidTarget.Enemies,
                LivingOrDead  = LivingOrDead.Living,
                TPCost        = 0,
                Keywords      = [CruelKeyword.KeywordName],
                CombatFunction = BasicDamageFunction.FunctionName,
                Parameters = new CombatFunctionParameters { CalcType = DamageCalcType.StandardFormula, PowerFactor = 1.0 },
            });
        };

        var applied = new List<string>();
        CombatEventBus.KeywordApplied += (keywordName, _, _, _, _, _) => applied.Add(keywordName);

        engine.BeginCombat();

        Assert.Empty(applied);
        Assert.True(enemy.IsDead);
    }

    [Fact]
    public void TwoActiveKeywordsBothApply_RaisesEventOncePerKeyword()
    {
        // What: reproduces MultipleKeywordsTests' Engage+Stoic stacking scenario, but asserts
        //       on the event-bus side: two active keywords that both apply on the same hit
        //       should raise KeywordApplied twice - once per keyword - each carrying its own
        //       independently-capped bonus, rather than one combined event or one dropped event.
        // How:  Stoic requires the actor at <=25% of max HP (ally hp=200 of maxHp=1000 -> 20%,
        //       condition met); Engage requires the target at >=75% of max HP (enemy stays at
        //       full HP until its single lethal hit, condition met). Base PowerFactor=0.8, so
        //       each keyword's independent cap is min(0.8, 0.5)=0.5. Both conditions are met on
        //       the one and only hit, so the test asserts KeywordApplied fires exactly twice,
        //       once for (EngageKeyword.KeywordName, 0.5) and once for (StoicKeyword.KeywordName, 0.5).
        var engine = new CombatEngineClass(new Random(0));

        // Stoic requires the actor at <=25% of max HP.
        var ally = new CombatEntity(
            entityId: "ally", name: "Ally", level: 1,
            maxHp: 1000, hp: 200, maxTp: 0, tp: 0,
            power: 10, defense: 0, speed: 100,
            evasion: 0f, critChance: 0f, critModifier: 0f);

        // Engage requires the target at >=75% of max HP.
        var enemy = new CombatEntity(
            entityId: "enemy", name: "Enemy", level: 1,
            maxHp: 41, hp: 41, maxTp: 0, tp: 0,
            power: 0, defense: 0, speed: 1,
            evasion: 0f, critChance: 0f, critModifier: 0f);

        engine.InitCombat(allies: [ally], enemies: [enemy]);

        CombatEventBus.WaitingForTurn += (entityId, _, _, isAlly) =>
        {
            if (!isAlly) return;

            engine.SubmitCommand(new CombatCommand
            {
                ActorId       = entityId,
                TargetingType = TargetingType.Random,
                ValidTargets  = ValidTarget.Enemies,
                LivingOrDead  = LivingOrDead.Living,
                TPCost        = 0,
                Keywords      = [EngageKeyword.KeywordName, StoicKeyword.KeywordName],
                CombatFunction = BasicDamageFunction.FunctionName,
                Parameters = new CombatFunctionParameters { CalcType = DamageCalcType.StandardFormula, PowerFactor = 0.8 },
            });
        };

        var applied = new List<(string keyword, double bonus)>();
        CombatEventBus.KeywordApplied += (keywordName, _, _, _, _, bonus) => applied.Add((keywordName, bonus));

        engine.BeginCombat();

        Assert.Equal(2, applied.Count);
        Assert.Contains((EngageKeyword.KeywordName, 0.5), applied);
        Assert.Contains((StoicKeyword.KeywordName, 0.5), applied);
    }

    [Fact]
    public void GrowthStacking_FirstUseDoesNotRaise_SubsequentUsesRaiseWithGrowingBonus()
    {
        // What: reproduces GrowthTests' repeated-use stacking scenario, but asserts on the
        //       event-bus side - since a bonus of exactly 0.0 is still a "met condition" as far
        //       as the engine's dispatch logic is concerned, this pins down whether
        //       KeywordApplied is gated on the bonus being non-zero or fires unconditionally
        //       whenever the keyword is active.
        // How:  Growth's bonus formula is 0.10*max(0,count-1), keyed by actor+actionId="tech1".
        //       Use 1: count=1 -> bonus=0 -> KeywordApplied should NOT fire (a zero bonus is
        //       treated as "nothing to report"). Use 2: count=2 -> bonus=0.10 -> fires. Use 3:
        //       count=3 -> bonus=0.20 -> fires. enemyHp is set to the exact running damage total
        //       (25+27+29) so combat ends right after the third hit. The test collects every
        //       bonus value from KeywordApplied and asserts the sequence is exactly [0.10, 0.20]
        //       - two events, not three, confirming the first (zero-bonus) use stayed silent.
        var engine = new CombatEngineClass(new Random(0));

        var ally = new CombatEntity(
            entityId: "ally", name: "Ally", level: 1,
            maxHp: 1000, hp: 1000, maxTp: 0, tp: 0,
            power: 10, defense: 0, speed: 100,
            evasion: 0f, critChance: 0f, critModifier: 0f);

        var enemy = new CombatEntity(
            entityId: "enemy", name: "Enemy", level: 1,
            maxHp: 25 + 27 + 29, hp: 25 + 27 + 29, maxTp: 0, tp: 0,
            power: 0, defense: 0, speed: 1,
            evasion: 0f, critChance: 0f, critModifier: 0f);

        engine.InitCombat(allies: [ally], enemies: [enemy]);

        CombatEventBus.WaitingForTurn += (entityId, _, _, isAlly) =>
        {
            if (isAlly)
            {
                engine.SubmitCommand(new CombatCommand
                {
                    ActorId       = entityId,
                    TargetingType = TargetingType.Random,
                    ValidTargets  = ValidTarget.Enemies,
                    LivingOrDead  = LivingOrDead.Living,
                    TPCost        = 0,
                    ActionId      = "tech1",
                    Keywords      = [GrowthKeyword.KeywordName],
                    CombatFunction = BasicDamageFunction.FunctionName,
                    Parameters = new CombatFunctionParameters { CalcType = DamageCalcType.StandardFormula, PowerFactor = 1.0 },
                });
            }
            else
            {
                engine.SubmitCommand(new CombatCommand
                {
                    ActorId       = entityId,
                    TargetingType = TargetingType.Random,
                    ValidTargets  = ValidTarget.Enemies,
                    LivingOrDead  = LivingOrDead.Living,
                    TPCost        = 0,
                    CombatFunction = BasicDamageFunction.FunctionName,
                    Parameters = new CombatFunctionParameters { CalcType = DamageCalcType.StandardFormula, PowerFactor = 1.0 },
                });
            }
        };

        var bonuses = new List<double>();
        CombatEventBus.KeywordApplied += (_, _, _, _, _, bonus) => bonuses.Add(bonus);

        engine.BeginCombat();

        Assert.Equal([0.10, 0.20], bonuses);
        Assert.True(enemy.IsDead);
    }
}
