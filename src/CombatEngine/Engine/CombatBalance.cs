namespace CombatEngine.Engine;

// Every tunable number the combat formulas read, sourced from GameSettings.json at startup.
// CombatEngine can't reference GameEngine (ContentLoader lives there and depends on CombatEngine,
// not the other way around), so GameEngineClass loads the JSON and calls Configure() once at
// startup - the same static-singleton shape as Logger/CombatEventBus. Every property defaults to
// today's hardcoded value, so Current is always valid even if Configure is never called (e.g. in
// tests, which exercise the defaults directly).
public sealed class CombatBalance
{
    public static CombatBalance Current { get; private set; } = new();

    public static void Configure(CombatBalance balance) => Current = balance;

    public DamageFormulaSettings DamageFormula { get; init; } = new();
    public double DefaultPowerFactor { get; init; } = 1.0;
    public double CritBaseMultiplier { get; init; } = 1.0;
    public double EvasionDecayAmount { get; init; } = 0.25;
    public KeywordCapSettings KeywordCap { get; init; } = new();
    public TurnOrderSettings TurnOrder { get; init; } = new();
    public KeywordBalanceSettings Keywords { get; init; } = new();
    public int LivingDeadReviveHp { get; init; } = 1;

    public sealed class DamageFormulaSettings
    {
        public double PowerMultiplier { get; init; } = 2.0;
        public double LevelMultiplier { get; init; } = 5.0;
        public double DefenseMitigationConstant { get; init; } = 128.0;
        public double DefenseFlatDivisor { get; init; } = 2.0;
    }

    public sealed class KeywordCapSettings
    {
        public double Multiplier { get; init; } = 2.0;
        public double Additive { get; init; } = 0.5;
    }

    public sealed class TurnOrderSettings
    {
        public double BaseMultiplier { get; init; } = 1.0;
        public double JitterRange { get; init; } = 0.5;
        public double JitterOffset { get; init; } = 0.25;
    }

    public sealed class KeywordBalanceSettings
    {
        public ThresholdBonus Cruel { get; init; } = new() { Threshold = 0.25, Bonus = 0.5 };
        public ThresholdBonus Empowered { get; init; } = new() { Threshold = 0.75, Bonus = 0.5 };
        public ThresholdBonus Engage { get; init; } = new() { Threshold = 0.75, Bonus = 0.5 };
        public ThresholdBonus Stoic { get; init; } = new() { Threshold = 0.25, Bonus = 0.5 };
        public double GrowthBonusPerUse { get; init; } = 0.10;
        public double TeamworkBonusPerUse { get; init; } = 0.05;
    }

    public sealed class ThresholdBonus
    {
        public double Threshold { get; init; }
        public double Bonus { get; init; }
    }
}
