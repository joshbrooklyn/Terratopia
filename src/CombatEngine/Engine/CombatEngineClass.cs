using CombatEngine.DataClasses;
using CombatEngine.Enums;
using CombatEngine.Passives;

namespace CombatEngine.Engine;

public class CombatEngineClass
{
    private static readonly Lazy<CombatEngineClass> _instance = new(() => new CombatEngineClass());
    public static CombatEngineClass Instance => _instance.Value;
    private readonly Random _rng;
    private readonly TurnOrderManager _turnOrder;
    private CombatFlowMachine _combatFlowMachine = null!;
    private readonly List<CombatEntity> _allies  = new();
    private readonly List<CombatEntity> _enemies = new();
    private Dictionary<string, CombatEntity> _allEntities = new();

    private int _roundNumber;    

    private CombatEngineClass()
    {
        _rng = new();
        _turnOrder = new TurnOrderManager(_rng);
    }

    internal CombatEngineClass(Random rng)
    {
        _rng = rng;
        _turnOrder = new TurnOrderManager(rng);
    }

    public void InitCombat(
        IReadOnlyList<CombatEntity> allies,
        IReadOnlyList<CombatEntity> enemies,
        bool isBossFight = false)
    {
        CombatEventBus.Reset();
        this.Reset();

        _allies.AddRange(allies);
        _enemies.AddRange(enemies);
        _allEntities = _allies.Concat(_enemies).ToDictionary(e => e.EntityId);

        _combatFlowMachine = new CombatFlowMachine(
            isPlayerEntity:           IsPlayerEntity,
            resolveAction:            ResolveAction,
            buildRound:               BuildRound,
            doRoundEnd:               DoRoundEnd,
            isRoundOver:              IsRoundOver,
            evaluateWinCondition:     EvaluateWinCondition,
            getValidTargets:          GetValidTargets,
            expandAutoTargets:        ExpandAutoTargets,
            assignAiTarget:           AssignRandomAiTarget,
            nextTurn:                 NextTurn,
            resolvePickCount:         ResolveRequiredPickCount
        );
    }

    public void SubmitTargets(List<string> chosenTargetIds) =>
        _combatFlowMachine.SubmitTargets(chosenTargetIds);

    public void SubmitCommand(CombatCommand cmd) =>
        _combatFlowMachine.SubmitCommand(cmd);

    public void BeginCombat()
    {
        _combatFlowMachine.Start();
    }

    public void Reset()
    {
        CombatEventBus.Reset();
        _allies.Clear();
        _enemies.Clear();
        _allEntities.Clear();
        _roundNumber = 0;
        _combatFlowMachine = null!;
    }

    private void BuildRound()
    {
        _roundNumber++;
        _turnOrder.BuildRound(GetLivingEntities());
        CombatEventBus.RaiseRoundStarted(_roundNumber, _turnOrder.CurrentTurnOrderIds, _turnOrder.CurrentTurnOrderNames);
    }

    private void DoRoundEnd()
    {
        CombatEventBus.RaiseRoundEnded(_roundNumber);
    }
    private bool IsPlayerEntity(CombatEntity entity) => _allies.Contains(entity);
    public IReadOnlyList<CombatEntity> GetLivingEntities() =>
        _allEntities.Values.Where(e => !e.IsDead).ToList();           

    public IReadOnlyList<CombatEntity> GetLivingAllies() =>
        _allies.Where(e => !e.IsDead).ToList();

    public IReadOnlyList<CombatEntity> GetLivingEnemies() =>
        _enemies.Where(e => !e.IsDead).ToList();

    public IReadOnlyList<CombatEntity> GetValidTargets(CombatCommand cmd)
    {
        var actor = _allEntities[cmd.ActorId];
        bool actorIsPlayer = IsPlayerEntity(actor);

        IEnumerable<CombatEntity> pool = cmd.ValidTargets switch
        {
            ValidTarget.Allies  => actorIsPlayer ? _allies  : _enemies,
            ValidTarget.Enemies => actorIsPlayer ? _enemies : _allies,
            ValidTarget.Both    => _allEntities.Values,
            _ => throw new ArgumentOutOfRangeException(nameof(cmd)),
        };

        return cmd.LivingOrDead switch
        {
            LivingOrDead.Living => pool.Where(e => !e.IsDead).ToList(),
            LivingOrDead.Dead   => pool.Where(e => e.IsDead).ToList(),
            LivingOrDead.Both   => pool.ToList(),
            _ => throw new ArgumentOutOfRangeException(nameof(cmd)),
        };
    }


    private CombatEntity? NextTurn()
    {
        var entity = _turnOrder.NextTurn();
        return entity;
    }


    private void ResolveAction(CombatCommand cmd)
    {
        if (!_allEntities.TryGetValue(cmd.ActorId, out var actor) || actor == null) 
            throw new InvalidOperationException($"Actor with ID {cmd.ActorId} not found among combat entities.");


        // Deduct TP
        if (cmd.TPCost > 0)
        {
            int oldTp = actor.Tp;
            actor.Tp -= cmd.TPCost;
            CombatEventBus.RaiseEntityTpChanged(actor.EntityId, actor.Name, oldTp, actor.Tp);
        }

        foreach (CombatDirectEffect CDE in cmd.DirectEffects)
        {
            foreach (var targetId in cmd.ChosenTargets)
            {
                if (!_allEntities.TryGetValue(targetId, out var target) || target == null) 
                    throw new InvalidOperationException($"Target with ID {targetId} not found among combat entities.");

                if (EvasionCheck(actor, target, cmd))
                {
                    //Evasion reduces 25% on each successful attempt
                    target.Evasion = Math.Max(0f, target.Evasion - 0.25f);

                    CombatEventBus.RaiseAttackEvaded(actor.EntityId, actor.Name, target.EntityId, target.Name);
                    continue;
                }               

                int damage = CalculateDamage(CDE, actor, target);

                bool isCrit = _rng.NextSingle() < actor.CritChance;
                if (isCrit)
                {
                    damage = (int)(damage * (1.0f + actor.CritModifier));
                }

                int oldHp = target.Hp;
                target.Hp = (int)Math.Max(0f, target.Hp - damage);
                CombatEventBus.RaiseEntityDamaged(target.EntityId, target.Name, damage, actor.EntityId, actor.Name, isCrit);
                CombatEventBus.RaiseEntityHpChanged(target.EntityId, target.Name, oldHp, target.Hp);
                if (target.Hp == 0 && !target.IsDead)
                    HandleEntityDefeated(target);
            }
        }
    }

    private void HandleEntityDefeated(CombatEntity target)
    {
        foreach (var passive in PassiveRegistry.GetForTrigger<DeathPassive>(target.Passives, PassiveTrigger.OnDeath))
        {
            if (passive.TryPreventDeath(target))
                return;
        }

        target.IsDead = true;
        CombatEventBus.RaiseEntityDeath(target.EntityId, target.Name);
    }

    private void AssignRandomAiTarget(CombatCommand cmd)
    {
        var pool = GetValidTargets(cmd);
        cmd.ChosenTargets = new List<string> { pool[_rng.Next(pool.Count)].EntityId };
    }

    private void ExpandAutoTargets(CombatCommand cmd)
    {
        switch (cmd.TargetingType)
        {
            case TargetingType.All:
            {
                bool actorIsPlayer = IsPlayerEntity(_allEntities[cmd.ActorId]);
                IEnumerable<CombatEntity> allPool = cmd.ValidTargets switch
                {
                    ValidTarget.Allies  => actorIsPlayer ? GetLivingAllies()  : GetLivingEnemies(),
                    ValidTarget.Enemies => actorIsPlayer ? GetLivingEnemies() : GetLivingAllies(),
                    ValidTarget.Both    => GetLivingEntities(),
                    _ => throw new ArgumentOutOfRangeException(nameof(cmd)),
                };
                cmd.ChosenTargets = allPool.Select(e => e.EntityId).ToList();
                break;
            }
            case TargetingType.Self:
                cmd.ChosenTargets = new List<string> { cmd.ActorId };
                break;
            case TargetingType.Random:
            {
                var pool = IsPlayerEntity(_allEntities[cmd.ActorId])
                    ? GetLivingEnemies()
                    : GetLivingAllies();
                int picks = ResolveRequiredPickCount(cmd.NumAttacks, cmd.AllowMultipleAttackOnSameTarget, pool.Count);
                cmd.ChosenTargets = cmd.AllowMultipleAttackOnSameTarget
                    ? PickWithReplacement(pool, picks, _rng)
                    : PickDistinctWithoutReplacement(pool, picks, _rng);
                break;
            }
        }
    }

    private static int ResolveRequiredPickCount(int numAttacks, bool allowMultipleAttackOnSameTarget, int poolSize) =>
        allowMultipleAttackOnSameTarget ? numAttacks : Math.Min(numAttacks, poolSize);

    private static List<string> PickWithReplacement(IReadOnlyList<CombatEntity> pool, int count, Random rng)
    {
        var result = new List<string>(count);
        for (int i = 0; i < count; i++)
            result.Add(pool[rng.Next(pool.Count)].EntityId);
        return result;
    }

    private static List<string> PickDistinctWithoutReplacement(IReadOnlyList<CombatEntity> pool, int count, Random rng)
    {
        var remaining = pool.ToList();
        var result = new List<string>(count);
        for (int i = 0; i < count; i++)
        {
            int idx = rng.Next(remaining.Count);
            result.Add(remaining[idx].EntityId);
            remaining.RemoveAt(idx);
        }
        return result;
    }

    private bool EvasionCheck(CombatEntity actor, CombatEntity target, CombatCommand cmd)
    {
        return _rng.NextSingle() < target.Evasion;
    }

    private int CalculateDamage(
        CombatDirectEffect cde,
        CombatEntity actor,
        CombatEntity target)
    {
        double actionPower = actor.Power * cde.PowerFactor;
        double baseDamage = (actionPower * 2f) + (actor.Level * 5f);

        double rawDamage;
        rawDamage = (baseDamage / ((target.Defense + 128f) / 128f)) - (target.Defense / 2f);

        int damage = (int)Math.Max(0f, rawDamage);

        return damage;
    }    

    private bool EvaluateWinCondition()
    {
        var livingAllies  = GetLivingAllies();
        var livingEnemies = GetLivingEnemies();

        if (livingAllies.Count > 0 && livingEnemies.Count > 0)
            return false;

        bool playerWon = livingEnemies.Count == 0 && livingAllies.Count > 0;
        CombatEventBus.RaiseCombatOver(playerWon);
        return true;
    }

    private bool IsRoundOver() => _turnOrder.IsRoundOver;
}