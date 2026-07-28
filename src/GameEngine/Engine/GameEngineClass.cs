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

    public IReadOnlyList<Tech> AllTechs => _allTechs;
    //public IReadOnlyList<Item> AllItems => _allItems;
    public IReadOnlyList<Monster> AllMonsters => _allMonsters;
    public IReadOnlyList<MonsterAction> AllMonsterActions => _allMonsterActions;
    public IReadOnlyList<Dungeon> AllDungeons => _allDungeons;
    public IReadOnlyList<Adventurer> AllAdventurers => _allAdventurers;

    private GameEngineClass()
    {
        _gameFlowMachine = new GameFlowMachine();
        _allTechs = ContentLoader.LoadTechs();
        //_allItems = ContentLoader.LoadItems();
        _allMonsters = ContentLoader.LoadMonsters();
        _allMonsterActions = ContentLoader.LoadMonsterActions();
        _allDungeons = ContentLoader.LoadDungeons();
        _allAdventurers = ContentLoader.LoadAdventurers();
    }

    public void CompleteStartup()  => _gameFlowMachine.CompleteStartup();
    public void OpenDataBrowser()  => _gameFlowMachine.OpenDataBrowser();
    public void CloseDataBrowser() => _gameFlowMachine.CloseDataBrowser();

    public void StartSkirmish() => _gameFlowMachine.StartSkirmish();
    public void EndSkirmish()   => _gameFlowMachine.EndSkirmish();

    public CombatStartData InitSkirmishCombat()
    {
        var allies = AllAdventurers.Select(MakeCombatEntity).ToList();
        var allySeeds = AllAdventurers
            .Select(a =>
            {
                var techs = a.TechsIds
                    .Select(id => _allTechs.Lookup(id))
                    .Where(t => t != null)
                    .Select(t => new CombatantInventoryEntry(t.TechId, t.Name, t.Description))
                    .ToList();
                return new CombatantSeed(
                    a.AdventurerId, a.Name,
                    a.Hp, a.MaxHp, a.Tp, a.MaxTp,
                    a.Level, a.Power, a.Defense, a.Speed, a.Evasion, a.CritChance,
                    techs, [], [], []);
            })
            .ToList();

        var skeleton = AllMonsters.Lookup("skeleton");
        _enemyMonsterMap = new Dictionary<string, Monster>();
        var enemies = Enumerable.Range(1, 4)
            .Select(i =>
            {
                int hp = skeleton.HpBase + skeleton.Level * skeleton.HpPerLevel;
                var entityId = $"skeleton_{i}";
                _enemyMonsterMap[entityId] = skeleton;
                return new CombatEntity(
                    entityId, $"Skeleton {i}", skeleton.Level,
                    hp, hp, 0, 0,
                    skeleton.PowerBase   + skeleton.Level * skeleton.PowerPerLevel,
                    skeleton.DefenseBase + skeleton.Level * skeleton.DefensePerLevel,
                    skeleton.SpeedBase   + skeleton.Level * skeleton.SpeedBasePerLevel,
                    0f, 0f, 0.5f, passives: skeleton.Passives);
            })
            .ToList();
        var enemySeeds = Enumerable.Range(1, 4)
            .Select(i =>
            {
                int hp    = skeleton.HpBase    + skeleton.Level * skeleton.HpPerLevel;
                int power = skeleton.PowerBase + skeleton.Level * skeleton.PowerPerLevel;
                int def   = skeleton.DefenseBase + skeleton.Level * skeleton.DefensePerLevel;
                int speed = skeleton.SpeedBase + skeleton.Level * skeleton.SpeedBasePerLevel;
                return new CombatantSeed(
                    $"skeleton_{i}", $"Skeleton {i}",
                    hp, hp, 0, 0,
                    skeleton.Level, power, def, speed, 0f, 0f,
                    [], [], [], []);
            })
            .ToList();

        CombatEngineClass.Instance.InitCombat(allies, enemies);
        return new CombatStartData(allySeeds, enemySeeds);
    }

    public void BeginSkirmishCombat() => CombatEngineClass.Instance.BeginCombat();

    public CombatEntity MakeCombatEntity(Adventurer a) => new(
        a.AdventurerId, a.Name, a.Level,
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

    public CombatCommand MakeCombatCommand(string actorId, string techId)
    {
        var tech = _allTechs.Lookup(techId);

        var directEffects = tech.DirectEffects.Select(e =>
        {
            if (e.EffectType == CombatDirectEffectType.Damage && e.Element is null)
                throw new ArgumentException($"Tech '{techId}': Damage effect requires an Element.");

            return new CombatDirectEffect
            {
                EffectType  = e.EffectType,
                Element     = e.Element,
                CalcType    = e.CalcType,
                PowerFactor = e.PowerFactor,
            };
        }).ToList();

        return new CombatCommand
        {
            ActorId       = actorId,
            TargetingType = tech.TargetingType,
            ValidTargets  = tech.ValidTargets,
            LivingOrDead  = tech.LivingOrDead,
            TPCost        = tech.TpCost,
            DirectEffects = directEffects,
        };
    }

    public CombatCommand MakeMonsterCombatCommand(string actorId, string monsterActionId)
    {
        var action = _allMonsterActions.Lookup(monsterActionId);

        var directEffects = action.DirectEffects.Select(e =>
        {
            if (e.EffectType == CombatDirectEffectType.Damage && e.Element is null)
                throw new ArgumentException($"MonsterAction '{monsterActionId}': Damage effect requires an Element.");

            return new CombatDirectEffect
            {
                EffectType  = e.EffectType,
                Element     = e.Element,
                CalcType    = e.CalcType,
                PowerFactor = e.PowerFactor,
            };
        }).ToList();

        return new CombatCommand
        {
            ActorId       = actorId,
            TargetingType = action.TargetingType,
            ValidTargets  = action.ValidTargets,
            LivingOrDead  = action.LivingOrDead,
            TPCost        = action.TpCost,
            DirectEffects = directEffects,
        };
    }

    public CombatCommand ChooseAiCommand(string actorId)
    {
        var monster = _enemyMonsterMap[actorId];

        var availableActions = new List<Func<CombatCommand>>();
        foreach (var actionId in monster.MonsterActionIds)
            availableActions.Add(() => MakeMonsterCombatCommand(actorId, actionId));
        if (monster.CanFight)
            availableActions.Add(() => MakeFightCommand(actorId));

        if (availableActions.Count == 0)
            throw new InvalidOperationException($"Monster '{monster.MonsterId}' has no available actions.");

        return availableActions[_rng.Next(availableActions.Count)]();
    }

    public CombatCommand MakeFightCommand(string actorId)
    {
        return new CombatCommand
        {
            ActorId       = actorId,
            TargetingType = TargetingType.Choose,
            ValidTargets  = ValidTarget.Enemies,
            LivingOrDead  = LivingOrDead.Living,
            TPCost        = 0,
            DirectEffects =
            [
                new CombatDirectEffect
                {
                    EffectType  = CombatDirectEffectType.Damage,
                    CalcType    = DamageCalcType.StandardFormula,
                    PowerFactor = 1.0,
                }
            ],
        };
    }
}