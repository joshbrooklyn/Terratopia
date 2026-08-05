using CombatEngine.CombatFunctions;
using CombatEngine.DataClasses;
using CombatEngine.Engine;
using CombatEngine.Enums;
using GameEngine;
using GameEngine.DataClasses;

namespace GameEngine.Engine;

public class GameEngineClass
{
    private static readonly Lazy<GameEngineClass> _instance = new(() => new GameEngineClass());
    public static GameEngineClass Instance => _instance.Value;

    private GameFlowMachine _gameFlowMachine;
    private readonly Random _rng = new();
    private List<Tech> _allTechs = new();
    private List<Item> _allItems = new();
    private List<Monster> _allMonsters = new();
    private List<MonsterAction> _allMonsterActions = new();
    private List<Dungeon> _allDungeons = new();
    private List<Adventurer> _allAdventurers = new();
    private Dictionary<string, Monster> _enemyMonsterMap = new();
    private readonly RunInventory _runInventory = new();

    public IReadOnlyList<Tech> AllTechs => _allTechs;
    public IReadOnlyList<Item> AllItems => _allItems;
    public IReadOnlyList<Monster> AllMonsters => _allMonsters;
    public IReadOnlyList<MonsterAction> AllMonsterActions => _allMonsterActions;
    public IReadOnlyList<Dungeon> AllDungeons => _allDungeons;
    public IReadOnlyList<Adventurer> AllAdventurers => _allAdventurers;

    public IReadOnlyList<(string Id, int Level)> SelectedAdventurerSlots { get; set; } = [];
    public IReadOnlyList<(string Id, int Level)> SelectedMonsterSlots    { get; set; } = [];

    private GameEngineClass()
    {
        _gameFlowMachine = new GameFlowMachine();
        _allTechs = ContentLoader.LoadTechs();
        _allItems = ContentLoader.LoadItems();
        _allMonsters = ContentLoader.LoadMonsters();
        _allMonsterActions = ContentLoader.LoadMonsterActions();
        _allDungeons = ContentLoader.LoadDungeons();
        _allAdventurers = ContentLoader.LoadAdventurers();

        CombatBalance.Configure(ToCombatBalance(ContentLoader.LoadGameSettings()));
    }

    // GameSettings (GameEngine.DataClasses) is the schema-validated JSON DTO; CombatBalance
    // (CombatEngine.Engine) is what the formulas actually read. CombatEngine can't reference
    // GameEngine/ContentLoader itself, so this is the one place the loaded values cross over.
    private static CombatBalance ToCombatBalance(GameSettings s) => new()
    {
        DamageFormula = new CombatBalance.DamageFormulaSettings
        {
            PowerMultiplier           = s.DamageFormula.PowerMultiplier,
            LevelMultiplier           = s.DamageFormula.LevelMultiplier,
            DefenseMitigationConstant = s.DamageFormula.DefenseMitigationConstant,
            DefenseFlatDivisor        = s.DamageFormula.DefenseFlatDivisor,
        },
        DefaultPowerFactor = s.DefaultPowerFactor,
        CritBaseMultiplier = s.CritBaseMultiplier,
        EvasionDecayAmount = s.EvasionDecayAmount,
        KeywordCap = new CombatBalance.KeywordCapSettings
        {
            Multiplier = s.KeywordCap.Multiplier,
            Additive   = s.KeywordCap.Additive,
        },
        TurnOrder = new CombatBalance.TurnOrderSettings
        {
            BaseMultiplier = s.TurnOrder.BaseMultiplier,
            JitterRange    = s.TurnOrder.JitterRange,
            JitterOffset   = s.TurnOrder.JitterOffset,
        },
        Keywords = new CombatBalance.KeywordBalanceSettings
        {
            Cruel               = new CombatBalance.ThresholdBonus { Threshold = s.Keywords.Cruel.Threshold, Bonus = s.Keywords.Cruel.Bonus },
            Empowered           = new CombatBalance.ThresholdBonus { Threshold = s.Keywords.Empowered.Threshold, Bonus = s.Keywords.Empowered.Bonus },
            Engage              = new CombatBalance.ThresholdBonus { Threshold = s.Keywords.Engage.Threshold, Bonus = s.Keywords.Engage.Bonus },
            Stoic               = new CombatBalance.ThresholdBonus { Threshold = s.Keywords.Stoic.Threshold, Bonus = s.Keywords.Stoic.Bonus },
            GrowthBonusPerUse   = s.Keywords.GrowthBonusPerUse,
            TeamworkBonusPerUse = s.Keywords.TeamworkBonusPerUse,
        },
        LivingDeadReviveHp = s.LivingDeadReviveHp,
        TimedBuffPct = s.TimedBuffPct,
    };

    public void CompleteStartup()  => _gameFlowMachine.CompleteStartup();
    public void OpenDataBrowser()  => _gameFlowMachine.OpenDataBrowser();
    public void CloseDataBrowser() => _gameFlowMachine.CloseDataBrowser();

    public void StartSkirmish() => _gameFlowMachine.StartSkirmish();
    public void EndSkirmish()   => _gameFlowMachine.EndSkirmish();

    // Refills every selected adventurer's item uses. Called once the party is locked in, since
    // StartSkirmish fires from the main menu before SelectedAdventurerSlots is populated. Move
    // this to the real run-start boundary when runs are implemented.
    public void StartRun() =>
        _runInventory.StartRun(
            SelectedAdventurerSlots.Select(slot => AllAdventurers.Lookup(slot.Id)),
            _allItems.Lookup);

    public int GetRemainingItemUses(string adventurerId, string itemId) =>
        _runInventory.GetRemaining(adventurerId, itemId);

    public CombatStartData InitSkirmishCombat()
    {
        var allies = new List<CombatEntity>();
        var allySeeds = new List<CombatantSeed>();

        foreach (var slot in SelectedAdventurerSlots)
        {
            var a = AllAdventurers.Lookup(slot.Id);
            allies.Add(MakeCombatEntity(a, slot.Level));

            var techs = a.TechsIds
                .Select(id => _allTechs.Lookup(id))
                .Where(t => t != null)
                .Select(t => new CombatantInventoryEntry(t.TechId, t.Name, t.Description))
                .ToList();

            allySeeds.Add(new CombatantSeed(
                a.AdventurerId, a.Name,
                a.Hp, a.MaxHp, a.Tp, a.MaxTp,
                slot.Level, a.Power, a.Defense, a.Speed, a.Evasion, a.CritChance,
                techs, [], [], []));
        }

        _enemyMonsterMap = new Dictionary<string, Monster>();
        var enemies = new List<CombatEntity>();
        var enemySeeds = new List<CombatantSeed>();

        for (int i = 0; i < SelectedMonsterSlots.Count; i++)
        {
            var slot = SelectedMonsterSlots[i];
            var monster = AllMonsters.Lookup(slot.Id);
            var level = slot.Level;
            var entityId = $"{monster.MonsterId}_{i + 1}";
            var displayName = $"{monster.Name} {i + 1}";

            int hp    = monster.HpBase      + (level - 1) * monster.HpPerLevel;
            int power = monster.PowerBase   + (level - 1) * monster.PowerPerLevel;
            int def   = monster.DefenseBase + (level - 1) * monster.DefensePerLevel;
            int speed = monster.SpeedBase   + (level - 1) * monster.SpeedBasePerLevel;

            _enemyMonsterMap[entityId] = monster;

            enemies.Add(new CombatEntity(
                entityId, displayName, level,
                hp, hp, 0, 0,
                power, def, speed,
                0f, 0f, 0.5f, passives: monster.Passives));

            enemySeeds.Add(new CombatantSeed(
                entityId, displayName,
                hp, hp, 0, 0,
                level, power, def, speed, 0f, 0f,
                [], [], [], []));
        }

        CombatEngineClass.Instance.InitCombat(allies, enemies);
        return new CombatStartData(allySeeds, enemySeeds);
    }

    public void BeginSkirmishCombat() => CombatEngineClass.Instance.BeginCombat();

    public CombatEntity MakeCombatEntity(Adventurer a) => MakeCombatEntity(a, a.Level);

    public CombatEntity MakeCombatEntity(Adventurer a, int level) => new(
        a.AdventurerId, a.Name, level,
        a.MaxHp, a.Hp, a.MaxTp, a.Tp,
        a.Power, a.Defense, a.Speed,
        a.Evasion, a.CritChance, a.CritModifier);

    public CombatEntity MakeCombatEntity(Monster m)
    {
        int hp = m.HpBase + m.Level * m.HpPerLevel;
        return new(
            m.MonsterId, m.Name, m.Level,
            hp, hp, 0, 0,
            m.PowerBase   + m.Level * m.PowerPerLevel,
            m.DefenseBase + m.Level * m.DefensePerLevel,
            m.SpeedBase   + m.Level * m.SpeedBasePerLevel,
            0f, 0f, 0.5f, passives: m.Passives);
    }

    public CombatCommand MakeTechCommand(string actorId, string techId)
    {
        var tech = _allTechs.Lookup(techId);

        return new CombatCommand
        {
            ActorId        = actorId,
            TargetingType  = tech.TargetingType,
            ValidTargets   = tech.ValidTargets,
            LivingOrDead   = tech.LivingOrDead,
            TPCost         = tech.TpCost,
            CombatFunction = tech.CombatFunction,
            Parameters     = tech.Parameters,
            Keywords       = tech.Keywords,
            ActionId       = tech.TechId,
            NumAttacks     = tech.NumAttacks,
            AllowMultipleAttackOnSameTarget = tech.AllowMultipleAttackOnSameTarget ?? false,
        };
    }

    // Consumes one use and returns the command. Named UseItem rather than MakeItemCommand because
    // it mutates run state - there is no cancel path in CombatFlowMachine, so a command that gets
    // built always resolves.
    public CombatCommand UseItem(string actorId, string itemId)
    {
        if (!_runInventory.TryConsume(actorId, itemId))
            throw new InvalidOperationException($"Item '{itemId}' has no uses remaining for '{actorId}'.");

        var item = _allItems.Lookup(itemId);

        return new CombatCommand
        {
            ActorId        = actorId,
            TargetingType  = item.TargetingType,
            ValidTargets   = item.ValidTargets,
            LivingOrDead   = item.LivingOrDead,
            TPCost         = 0,
            CombatFunction = item.CombatFunction,
            Parameters     = item.Parameters,
            Keywords       = item.Keywords,
            ActionId       = item.ItemId,
            NumAttacks     = item.NumAttacks,
            AllowMultipleAttackOnSameTarget = item.AllowMultipleAttackOnSameTarget ?? false,
        };
    }

    public CombatCommand MakeMonsterCombatCommand(string actorId, string monsterActionId)
    {
        var action = _allMonsterActions.Lookup(monsterActionId);

        return new CombatCommand
        {
            ActorId        = actorId,
            TargetingType  = action.TargetingType,
            ValidTargets   = action.ValidTargets,
            LivingOrDead   = action.LivingOrDead,
            TPCost         = action.TpCost,
            CombatFunction = action.CombatFunction,
            Parameters     = action.Parameters,
            Keywords       = action.Keywords,
            ActionId       = action.MonsterActionId,
        };
    }

    public CombatCommand ChooseAiCommand(string actorId)
    {
        var monster = _enemyMonsterMap[actorId];

        var availableActions = new List<Func<CombatCommand>>();
        foreach (var actionId in monster.MonsterActionIds)
            availableActions.Add(() => MakeMonsterCombatCommand(actorId, actionId));
        if (monster.CanUseFightAction)
            availableActions.Add(() => MakeFightCommand(actorId));

        if (availableActions.Count == 0)
            throw new InvalidOperationException($"Monster '{monster.MonsterId}' has no available actions.");

        return availableActions[_rng.Next(availableActions.Count)]();
    }

    public CombatCommand MakeFightCommand(string actorId)
    {
        // No element - the basic attack is non-elemental (physical).
        return new CombatCommand
        {
            ActorId        = actorId,
            TargetingType  = TargetingType.Choose,
            ValidTargets   = ValidTarget.Enemies,
            LivingOrDead   = LivingOrDead.Living,
            TPCost         = 0,
            CombatFunction = BasicDamageFunction.FunctionName,
            Parameters     = new CombatFunctionParameters
            {
                CalcType    = DamageOrHealCalcType.StandardFormula,
                PowerFactor = 1.0,
            },
        };
    }
}