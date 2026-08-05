namespace GameEngine.DataClasses;

// The single GameData/GameSettings.json file - every tunable constant CombatEngine's formulas
// read. Unlike every other GameData type this isn't one of many files in a folder, so Id is a
// fixed constant rather than a JSON-backed field; see ContentLoader.LoadGameSettings.
public class GameSettings : IGameDataObject
{
    public static string SchemaResourceName => "GameEngine.Schemas.gamesettings.schema.json";
    public string Id => "GameSettings";
    public required int SchemaVersion { get; init; }

    public required DamageFormulaSettings DamageFormula { get; init; }
    public required double DefaultPowerFactor { get; init; }
    public required double CritBaseMultiplier { get; init; }
    public required double EvasionDecayAmount { get; init; }
    public required KeywordCapSettings KeywordCap { get; init; }
    public required TurnOrderSettings TurnOrder { get; init; }
    public required KeywordBalanceSettings Keywords { get; init; }
    public required int LivingDeadReviveHp { get; init; }
    public required double TimedBuffPct { get; init; }
    public required double RegenDrainHpPct { get; init; }
    public required double RegenDrainTpPct { get; init; }

    public class DamageFormulaSettings
    {
        public required double PowerMultiplier { get; init; }
        public required double LevelMultiplier { get; init; }
        public required double DefenseMitigationConstant { get; init; }
        public required double DefenseFlatDivisor { get; init; }
    }

    public class KeywordCapSettings
    {
        public required double Multiplier { get; init; }
        public required double Additive { get; init; }
    }

    public class TurnOrderSettings
    {
        public required double BaseMultiplier { get; init; }
        public required double JitterRange { get; init; }
        public required double JitterOffset { get; init; }
    }

    public class KeywordBalanceSettings
    {
        public required ThresholdBonus Cruel { get; init; }
        public required ThresholdBonus Empowered { get; init; }
        public required ThresholdBonus Engage { get; init; }
        public required ThresholdBonus Stoic { get; init; }
        public required double GrowthBonusPerUse { get; init; }
        public required double TeamworkBonusPerUse { get; init; }
    }

    public class ThresholdBonus
    {
        public required double Threshold { get; init; }
        public required double Bonus { get; init; }
    }
}
