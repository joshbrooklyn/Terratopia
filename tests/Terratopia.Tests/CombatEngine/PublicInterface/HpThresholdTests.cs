using CombatEngine;
using CombatEngine.CombatFunctions;
using CombatEngine.DataClasses;
using CombatEngine.Engine;
using CombatEngine.Enums;
using CombatEngine.Keywords;

namespace Terratopia.Tests.CombatEngine.PublicInterface;

// Unlike Teamwork/Growth (which get both an isolated keyword-class unit test in
// Internal/ and a full-combat test here), the HP-threshold keywords previously only had the
// isolated version (see Internal/HpThresholdKeywordTests.cs, which exhaustively probes each
// threshold boundary). This file adds the missing end-to-end half: proving each keyword's
// bonus actually flows through the real ApplyKeywordBonuses/CombatEventBus pipeline when driven
// by live actor/target HP, not just that GetBonus computes the right number in isolation.
[Collection("CombatEngineSerial")]
public class HpThresholdTests
{
    // Ally (power=10, level=1, defense=0) opens combat with a single BasicDamage hit
    // (PowerFactor=1.0) carrying the keyword under test, then — regardless of whether that hit
    // was lethal — follows up with a guaranteed-overkill hit so combat always ends right after
    // the one hit each test measures. The enemy only ever uses NoOp, so it never threatens the
    // ally and never itself carries a keyword.
    //
    // With no bonus: effectivePowerFactor=1.0 -> actionPower=10 -> baseDamage=10*2+1*5=25 ->
    // damage 25. With the keyword's +50% bonus applied (cap = min(1.0*2, 1.0+0.5) = 1.5,
    // comfortably above the 0.5 bonus, so it's never clipped here): effectivePowerFactor=1.5 ->
    // actionPower=15 -> baseDamage=15*2+5=35 -> damage 35. 25 / 35 are the "no bonus" /
    // "bonus applied" fingerprints asserted throughout this file.
    private static (CombatEngineClass engine, CombatEntity enemy) SetupOpeningHit(
        string keyword, int allyHp, int allyMaxHp, int enemyHp, int enemyMaxHp)
    {
        var engine = new CombatEngineClass(new Random(0));

        var ally = new CombatEntity(
            entityId: "ally", name: "Ally", level: 1,
            maxHp: allyMaxHp, hp: allyHp, maxTp: 0, tp: 0,
            power: 10, defense: 0, speed: 10,
            evasion: 0f, critChance: 0f, critModifier: 0f);

        var enemy = new CombatEntity(
            entityId: "enemy", name: "Enemy", level: 1,
            maxHp: enemyMaxHp, hp: enemyHp, maxTp: 0, tp: 0,
            power: 0, defense: 0, speed: 1,
            evasion: 0f, critChance: 0f, critModifier: 0f);

        engine.InitCombat(allies: [ally], enemies: [enemy]);

        bool openingUsed = false;
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

            if (!openingUsed)
            {
                openingUsed = true;
                engine.SubmitCommand(new CombatCommand
                {
                    ActorId        = entityId,
                    TargetingType  = TargetingType.Random,
                    ValidTargets   = ValidTarget.Enemies,
                    LivingOrDead   = LivingOrDead.Living,
                    Keywords       = [keyword],
                    CombatFunction = BasicDamageFunction.FunctionName,
                    Parameters     = new CombatFunctionParameters { CalcType = DamageOrHealCalcType.StandardFormula, PowerFactor = 1.0 },
                });
                return;
            }

            // Closing hit: deliberate overkill so combat always ends here, regardless of how
            // much HP the opening hit left the enemy at.
            engine.SubmitCommand(new CombatCommand
            {
                ActorId        = entityId,
                TargetingType  = TargetingType.Random,
                ValidTargets   = ValidTarget.Enemies,
                LivingOrDead   = LivingOrDead.Living,
                CombatFunction = BasicDamageFunction.FunctionName,
                Parameters     = new CombatFunctionParameters { CalcType = DamageOrHealCalcType.StandardFormula, PowerFactor = 100.0 },
            });
        };

        return (engine, enemy);
    }

    private static int? RunAndCaptureOpeningDamage(CombatEngineClass engine, CombatEntity enemy)
    {
        int? firstDamage = null;
        CombatEventBus.EntityDamaged += (targetId, _, dmg, _, _, _, _, _, _, _) =>
        {
            if (targetId == "enemy") firstDamage ??= dmg;
        };

        engine.BeginCombat();

        Assert.True(enemy.IsDead, "The overkill closing hit should always finish the enemy off.");
        return firstDamage;
    }

    [Fact]
    public void Engage_AppliesBonus_WhenTargetIsHealthy()
    {
        // What: verifies EngageKeyword's +50% bonus actually flows through real combat (not
        //       just GetBonus in isolation) when the target is at or above 75% of max HP.
        // How:  Enemy is at 100/100 HP (100% >= 75%), so the opening hit should land for 35 and
        //       raise KeywordApplied for "Engage" with bonus 0.5.
        var (engine, enemy) = SetupOpeningHit(EngageKeyword.KeywordName,
            allyHp: 1000, allyMaxHp: 1000, enemyHp: 100, enemyMaxHp: 100);

        double? appliedBonus = null;
        CombatEventBus.KeywordApplied += (name, _, _, _, _, bonus, _, _, _) => { if (name == EngageKeyword.KeywordName) appliedBonus ??= bonus; };

        var damage = RunAndCaptureOpeningDamage(engine, enemy);

        Assert.Equal(0.5, appliedBonus);
        Assert.Equal(35, damage);
    }

    [Fact]
    public void StatelessKeyword_ReportsZeroUseCount()
    {
        // What: verifies KeywordApplied's useCount is 0 for a stateless HP-threshold keyword,
        //       which keeps no counter (PowerKeyword.UsageKey defaults to null for these).
        // How:  Same Engage setup as Engage_AppliesBonus_WhenTargetIsHealthy - Engage has no
        //       UsageKey override, so KeywordResolver.ApplyKeywordBonuses must fall back to 0
        //       rather than throwing or reading some unrelated counter.
        var (engine, enemy) = SetupOpeningHit(EngageKeyword.KeywordName,
            allyHp: 1000, allyMaxHp: 1000, enemyHp: 100, enemyMaxHp: 100);

        int? useCount = null;
        CombatEventBus.KeywordApplied += (name, _, _, _, _, _, _, _, count) => { if (name == EngageKeyword.KeywordName) useCount ??= count; };

        RunAndCaptureOpeningDamage(engine, enemy);

        Assert.Equal(0, useCount);
    }

    [Fact]
    public void Engage_GrantsNoBonus_WhenTargetIsNotHealthy()
    {
        // What: verifies Engage's bonus is withheld once the target drops below the 75%
        //       threshold.
        // How:  Enemy is at 50/100 HP (50% < 75%), so the opening hit should land for the
        //       unmodified 25 damage and no "Engage" KeywordApplied should fire at all.
        var (engine, enemy) = SetupOpeningHit(EngageKeyword.KeywordName,
            allyHp: 1000, allyMaxHp: 1000, enemyHp: 50, enemyMaxHp: 100);

        bool engageApplied = false;
        CombatEventBus.KeywordApplied += (name, _, _, _, _, _, _, _, _) => { if (name == EngageKeyword.KeywordName) engageApplied = true; };

        var damage = RunAndCaptureOpeningDamage(engine, enemy);

        Assert.False(engageApplied, "Engage should not apply below the 75% target-HP threshold.");
        Assert.Equal(25, damage);
    }

    [Fact]
    public void Cruel_AppliesBonus_WhenTargetIsWounded()
    {
        // What: verifies CruelKeyword's +50% bonus flows through real combat when the target is
        //       at or below 25% of max HP.
        // How:  Enemy is at 25/100 HP (25% <= 25%), so the opening hit should land for 35 and
        //       raise KeywordApplied for "Cruel" with bonus 0.5.
        var (engine, enemy) = SetupOpeningHit(CruelKeyword.KeywordName,
            allyHp: 1000, allyMaxHp: 1000, enemyHp: 25, enemyMaxHp: 100);

        double? appliedBonus = null;
        CombatEventBus.KeywordApplied += (name, _, _, _, _, bonus, _, _, _) => { if (name == CruelKeyword.KeywordName) appliedBonus ??= bonus; };

        var damage = RunAndCaptureOpeningDamage(engine, enemy);

        Assert.Equal(0.5, appliedBonus);
        Assert.Equal(35, damage);
    }

    [Fact]
    public void Cruel_GrantsNoBonus_WhenTargetIsNotWounded()
    {
        // What: verifies Cruel's bonus is withheld once the target is above the 25% threshold.
        // How:  Enemy is at 50/100 HP (50% > 25%), so the opening hit should land for the
        //       unmodified 25 damage and no "Cruel" KeywordApplied should fire at all.
        var (engine, enemy) = SetupOpeningHit(CruelKeyword.KeywordName,
            allyHp: 1000, allyMaxHp: 1000, enemyHp: 50, enemyMaxHp: 100);

        bool cruelApplied = false;
        CombatEventBus.KeywordApplied += (name, _, _, _, _, _, _, _, _) => { if (name == CruelKeyword.KeywordName) cruelApplied = true; };

        var damage = RunAndCaptureOpeningDamage(engine, enemy);

        Assert.False(cruelApplied, "Cruel should not apply above the 25% target-HP threshold.");
        Assert.Equal(25, damage);
    }

    [Fact]
    public void Empowered_AppliesBonus_WhenActorIsHealthy()
    {
        // What: verifies EmpoweredKeyword's +50% bonus flows through real combat when the
        //       acting ally itself is at or above 75% of its own max HP.
        // How:  Ally is at 1000/1000 HP (100% >= 75%), so the opening hit should land for 35
        //       and raise KeywordApplied for "Empowered" with bonus 0.5.
        var (engine, enemy) = SetupOpeningHit(EmpoweredKeyword.KeywordName,
            allyHp: 1000, allyMaxHp: 1000, enemyHp: 100, enemyMaxHp: 100);

        double? appliedBonus = null;
        CombatEventBus.KeywordApplied += (name, _, _, _, _, bonus, _, _, _) => { if (name == EmpoweredKeyword.KeywordName) appliedBonus ??= bonus; };

        var damage = RunAndCaptureOpeningDamage(engine, enemy);

        Assert.Equal(0.5, appliedBonus);
        Assert.Equal(35, damage);
    }

    [Fact]
    public void Empowered_GrantsNoBonus_WhenActorIsNotHealthy()
    {
        // What: verifies Empowered's bonus is withheld once the acting ally drops below the
        //       75% threshold.
        // How:  Ally is at 500/1000 HP (50% < 75%), so the opening hit should land for the
        //       unmodified 25 damage and no "Empowered" KeywordApplied should fire at all.
        var (engine, enemy) = SetupOpeningHit(EmpoweredKeyword.KeywordName,
            allyHp: 500, allyMaxHp: 1000, enemyHp: 100, enemyMaxHp: 100);

        bool empoweredApplied = false;
        CombatEventBus.KeywordApplied += (name, _, _, _, _, _, _, _, _) => { if (name == EmpoweredKeyword.KeywordName) empoweredApplied = true; };

        var damage = RunAndCaptureOpeningDamage(engine, enemy);

        Assert.False(empoweredApplied, "Empowered should not apply below the 75% actor-HP threshold.");
        Assert.Equal(25, damage);
    }

    [Fact]
    public void Stoic_AppliesBonus_WhenActorIsWounded()
    {
        // What: verifies StoicKeyword's +50% bonus flows through real combat when the acting
        //       ally itself is at or below 25% of its own max HP.
        // How:  Ally is at 250/1000 HP (25% <= 25%), so the opening hit should land for 35 and
        //       raise KeywordApplied for "Stoic" with bonus 0.5.
        var (engine, enemy) = SetupOpeningHit(StoicKeyword.KeywordName,
            allyHp: 250, allyMaxHp: 1000, enemyHp: 100, enemyMaxHp: 100);

        double? appliedBonus = null;
        CombatEventBus.KeywordApplied += (name, _, _, _, _, bonus, _, _, _) => { if (name == StoicKeyword.KeywordName) appliedBonus ??= bonus; };

        var damage = RunAndCaptureOpeningDamage(engine, enemy);

        Assert.Equal(0.5, appliedBonus);
        Assert.Equal(35, damage);
    }

    [Fact]
    public void Stoic_GrantsNoBonus_WhenActorIsNotWounded()
    {
        // What: verifies Stoic's bonus is withheld once the acting ally is above the 25%
        //       threshold.
        // How:  Ally is at 500/1000 HP (50% > 25%), so the opening hit should land for the
        //       unmodified 25 damage and no "Stoic" KeywordApplied should fire at all.
        var (engine, enemy) = SetupOpeningHit(StoicKeyword.KeywordName,
            allyHp: 500, allyMaxHp: 1000, enemyHp: 100, enemyMaxHp: 100);

        bool stoicApplied = false;
        CombatEventBus.KeywordApplied += (name, _, _, _, _, _, _, _, _) => { if (name == StoicKeyword.KeywordName) stoicApplied = true; };

        var damage = RunAndCaptureOpeningDamage(engine, enemy);

        Assert.False(stoicApplied, "Stoic should not apply above the 25% actor-HP threshold.");
        Assert.Equal(25, damage);
    }
}
