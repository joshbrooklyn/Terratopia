using CombatEngine.CombatFunctions;
using CombatEngine.DataClasses;
using CombatEngine.Enums;
using CombatEngine.Keywords;
using CombatEngine.Passives;

namespace CombatEngine.Engine;

public class CombatEngineClass : IKeywordUsageStore
{
    private static readonly Lazy<CombatEngineClass> _instance = new(() => new CombatEngineClass());
    public static CombatEngineClass Instance => _instance.Value;
    private readonly Random _rng;
    private readonly TurnOrderManager _turnOrder;
    private CombatFlowMachine _combatFlowMachine = null!;
    private readonly List<CombatEntity> _allies  = new();
    private readonly List<CombatEntity> _enemies = new();
    private Dictionary<string, CombatEntity> _allEntities = new();
    private readonly Dictionary<string, int> _keywordUsageCounts = new();

    private int _roundNumber;

    int IKeywordUsageStore.Increment(string key) =>
        _keywordUsageCounts[key] = _keywordUsageCounts.GetValueOrDefault(key) + 1;

    int IKeywordUsageStore.GetCount(string key) =>
        _keywordUsageCounts.GetValueOrDefault(key);

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

    private void Reset()
    {
        CombatEventBus.Reset();
        _allies.Clear();
        _enemies.Clear();
        _allEntities.Clear();
        _keywordUsageCounts.Clear();
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
    internal IReadOnlyList<CombatEntity> GetLivingEntities() =>
        _allEntities.Values.Where(e => !e.IsDead).ToList();

    private IReadOnlyList<CombatEntity> GetLivingAllies() =>
        _allies.Where(e => !e.IsDead).ToList();

    private IReadOnlyList<CombatEntity> GetLivingEnemies() =>
        _enemies.Where(e => !e.IsDead).ToList();

    internal IReadOnlyList<CombatEntity> GetValidTargets(CombatCommand cmd)
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

        var function = CombatFunctionRegistry.Resolve(cmd.CombatFunction);

        bool actorIsAlly    = IsPlayerEntity(actor);
        var  activeKeywords = PowerKeywordRegistry.Resolve(cmd.Keywords).ToList();
        NotifyKeywordsUsed(activeKeywords, actor, actorIsAlly, cmd.ActionId);

        var targets = cmd.ChosenTargets.Select(GetEntity).ToList();

        function.Execute(new CombatFunctionContext
        {
            Command             = cmd,
            Actor               = actor,
            ActorIsAlly         = actorIsAlly,
            Parameters          = cmd.Parameters,
            Targets             = targets,
            AllEntities         = _allEntities,
            GetEntity           = GetEntity,
            Rng                 = _rng,
            ResolveTpCost       = ()                => cmd.TPCost,
            DeductTp            = DeductTp,
            TryEvade            = TryEvade,
            RollCrit            = a                 => _rng.NextSingle() < a.CritChance,
            ApplyCritModifier   = (a, damage)       => (int)(damage * (1.0f + a.CritModifier)),
            ApplyKeywordBonuses = (basePower, a, t) => ApplyKeywordBonuses(activeKeywords, basePower, a, t, actorIsAlly, cmd.ActionId),
            CalculateDamage     = CalculateDamage,
            CalculateHealAmount = CalculateHealAmount,
            ApplyDamage         = ApplyDamage,
            ApplyHeal           = ApplyHeal,
        });

        CombatEventBus.RaiseActionResolved(cmd, actor.Name, targets.Select(t => t.Name).ToList());
    }

    // Fires once per active keyword, per command - before any target is touched. This is the
    // hook stacking keywords (Growth, Teamwork) use to record that this action was used.
    private void NotifyKeywordsUsed(List<PowerKeyword> activeKeywords, CombatEntity actor, bool actorIsAlly, string actionId)
    {
        foreach (var keyword in activeKeywords)
            keyword.OnUsed(actor, actorIsAlly, actionId, this);
    }

    // Each active keyword's bonus is capped independently against this action's own base power
    // factor, then the capped contributions are summed (not: sum first, then cap once) - see
    // docs/keywords.md "Capping rules". Also raises KeywordApplied for each nonzero contribution.
    private double ApplyKeywordBonuses(List<PowerKeyword> activeKeywords, double basePowerFactor, CombatEntity actor, CombatEntity target, bool actorIsAlly, string actionId)
    {
        if (activeKeywords.Count == 0)
            return 0.0;

        // <= 0 when base PowerFactor is 0, which naturally yields no bonus ("no effect if the
        // base power modifier of the action is 0%").
        double cap = Math.Min(basePowerFactor * 2, basePowerFactor + 0.5);

        double totalBonus = 0.0;
        foreach (var keyword in activeKeywords)
        {
            double raw = keyword.GetBonus(actor, target, actorIsAlly, actionId, this);
            double applied = Math.Min(raw, cap);
            if (applied > 0)
                CombatEventBus.RaiseKeywordApplied(keyword.Name, actor.EntityId, actor.Name, target.EntityId, target.Name, applied);
            totalBonus += applied;
        }
        return totalBonus;
    }

    private static void HandleEntityDefeated(CombatEntity target)
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

    private CombatEntity GetEntity(string entityId) =>
        _allEntities.TryGetValue(entityId, out var entity) && entity != null
            ? entity
            : throw new InvalidOperationException($"Target with ID {entityId} not found among combat entities.");

    private static void DeductTp(CombatEntity actor, int amount)
    {
        if (amount <= 0)
            return;

        int oldTp = actor.Tp;
        actor.Tp -= amount;
        CombatEventBus.RaiseEntityTpChanged(actor.EntityId, actor.Name, oldTp, actor.Tp);
    }

    // True when the attack is evaded. Evasion decays 25% on each successful dodge.
    private bool TryEvade(CombatEntity actor, CombatEntity target)
    {
        if (_rng.NextSingle() >= target.Evasion)
            return false;

        target.Evasion = Math.Max(0f, target.Evasion - 0.25f);
        CombatEventBus.RaiseAttackEvaded(actor.EntityId, actor.Name, target.EntityId, target.Name);
        return true;
    }

    private static void ApplyDamage(CombatEntity actor, CombatEntity target, int damage, bool isCrit)
    {
        int oldHp = target.Hp;
        target.Hp = (int)Math.Max(0f, target.Hp - damage);
        CombatEventBus.RaiseEntityDamaged(target.EntityId, target.Name, damage, actor.EntityId, actor.Name, isCrit);
        CombatEventBus.RaiseEntityHpChanged(target.EntityId, target.Name, oldHp, target.Hp);
        if (target.Hp == 0 && !target.IsDead)
            HandleEntityDefeated(target);
    }

    // Healing never revives - a dead target is skipped outright, and Hp is capped at MaxHp.
    private static void ApplyHeal(CombatEntity actor, CombatEntity target, int amount)
    {
        if (target.IsDead || amount <= 0)
            return;

        int oldHp = target.Hp;
        target.Hp = Math.Min(target.MaxHp, target.Hp + amount);
        CombatEventBus.RaiseEntityHealed(target.EntityId, target.Name, target.Hp - oldHp, actor.EntityId, actor.Name);
        CombatEventBus.RaiseEntityHpChanged(target.EntityId, target.Name, oldHp, target.Hp);
    }

    // The shared half of the formula: action power scaled by calc type, doubled, plus level bump.
    private static double CalculateBaseAmount(CombatEntity actor, double effectivePowerFactor, DamageCalcType calcType)
    {
        double actionPower = calcType == DamageCalcType.FixedPower
            ? effectivePowerFactor
            : actor.Power * effectivePowerFactor;
        return (actionPower * 2f) + (actor.Level * 5f);
    }

    private static int CalculateDamage(
        CombatEntity   actor,
        CombatEntity   target,
        double         effectivePowerFactor,
        DamageCalcType calcType)
    {
        double baseDamage = CalculateBaseAmount(actor, effectivePowerFactor, calcType);

        double rawDamage;
        rawDamage = (baseDamage / ((target.Defense + 128f) / 128f)) - (target.Defense / 2f);

        int damage = (int)Math.Max(0f, rawDamage);

        return damage;
    }

    // Same formula as damage, minus the target's Defense divisor - healing ignores defense.
    private static int CalculateHealAmount(CombatEntity actor, double effectivePowerFactor, DamageCalcType calcType) =>
        (int)Math.Max(0f, CalculateBaseAmount(actor, effectivePowerFactor, calcType));

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