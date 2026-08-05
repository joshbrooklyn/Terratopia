using CombatEngine.CombatFunctions;
using CombatEngine.DataClasses;
using CombatEngine.Keywords;

namespace CombatEngine.Engine;

public class CombatEngineClass
{
    private static readonly Lazy<CombatEngineClass> _instance = new(() => new CombatEngineClass());
    public static CombatEngineClass Instance => _instance.Value;
    private readonly Random _rng;
    private readonly TurnOrderManager _turnOrder;
    private CombatFlowMachine _combatFlowMachine = null!;
    private CombatRoster      _roster            = null!;
    private KeywordResolver   _keywords          = null!;

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

        _roster   = new CombatRoster(allies, enemies, _rng);
        _keywords = new KeywordResolver();

        _combatFlowMachine = new CombatFlowMachine(
            isPlayerEntity:           _roster.IsPlayerEntity,
            resolveAction:            ResolveAction,
            buildRound:               BuildRound,
            doRoundEnd:               DoRoundEnd,
            isRoundOver:              IsRoundOver,
            evaluateWinCondition:     EvaluateWinCondition,
            getValidTargets:          _roster.GetValidTargets,
            expandAutoTargets:        _roster.ExpandAutoTargets,
            assignAiTarget:           _roster.AssignRandomAiTarget,
            nextTurn:                 NextTurn,
            resolvePickCount:         CombatRoster.ResolveRequiredPickCount
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

    // The roster and keyword counters are rebuilt outright by InitCombat rather than cleared, so
    // Reset only has to drop the previous encounter's collaborators.
    private void Reset()
    {
        CombatEventBus.Reset();
        _roundNumber       = 0;
        _roster            = null!;
        _keywords          = null!;
        _combatFlowMachine = null!;
    }

    internal IReadOnlyList<CombatEntity> GetValidTargets(CombatCommand cmd) =>
        _roster.GetValidTargets(cmd);

    private void BuildRound()
    {
        // Buff/debuff ticking must happen first: turn order is sorted by Speed, so a Speed buff
        // expiring this round must be gone before the order is decided. Regen/drain has no
        // bearing on turn order, so it's applied after RoundStarted fires - otherwise its HP/TP
        // delta (and combat-log entries) would land before the round-start announcement instead
        // of after it.
        TickBuffDebuffsOnEntities();

        _roundNumber++;
        _turnOrder.BuildRound(_roster.GetLivingEntities());
        CombatEventBus.RaiseRoundStarted(_roundNumber, _turnOrder.CurrentTurnOrderIds, _turnOrder.CurrentTurnOrderNames);

        ApplyRegensDrainsOnEntities();
    }

    private void DoRoundEnd()
    {
        CombatEventBus.RaiseRoundEnded(_roundNumber);
    }

    private CombatEntity? NextTurn()
    {
        var entity = _turnOrder.NextTurn();
        return entity;
    }


    private void ResolveAction(CombatCommand cmd)
    {
        if (!_roster.AllEntities.TryGetValue(cmd.ActorId, out var actor) || actor == null)
            throw new InvalidOperationException($"Actor with ID {cmd.ActorId} not found among combat entities.");

        var function = CombatFunctionRegistry.Resolve(cmd.CombatFunction);

        bool actorIsAlly    = _roster.IsPlayerEntity(actor);
        var  activeKeywords = PowerKeywordRegistry.Resolve(cmd.Keywords).ToList();
        _keywords.NotifyKeywordsUsed(activeKeywords, actor, actorIsAlly, cmd.SourceId);

        var targets = cmd.ChosenTargets.Select(_roster.GetEntity).ToList();

        CombatEventBus.RaiseActionResolved(cmd, actor.Name, targets.Select(t => t.Name).ToList());

        function.Execute(new CombatFunctionContext
        {
            Command               = cmd,
            Actor                 = actor,
            ActorIsAlly           = actorIsAlly,
            Parameters            = cmd.Parameters,
            Targets               = targets,
            AllEntities           = _roster.AllEntities,
            GetEntity             = _roster.GetEntity,
            Rng                   = _rng,
            ResolveTpCost         = ()                => cmd.TPCost,
            // Monsters don't spend TP on actions today; MonsterAction.TPCost is reserved for a future feature.
            DeductTp              = (entity, amount)  => { if (actorIsAlly) entity.SpendTp(amount, cmd.SourceId, cmd.SourceName); },
            TryEvade              = (a, t) => TryEvade(a, t, cmd.SourceId, cmd.SourceName),
            RollCrit              = RollCrit,
            ApplyCritModifier     = ApplyCritModifier,
            ApplyKeywordBonuses   = (basePower, a, t) => _keywords.ApplyKeywordBonuses(activeKeywords, basePower, a, t, actorIsAlly, cmd.SourceId, cmd.SourceName),
            CalculateDamageAmount = CombatMath.CalculateDamageAmount,
            CalculateHealAmount   = CombatMath.CalculateHealAmount,
            ApplyDamage           = (actor, target, damage, isCrit) => target.TakeDamage(actor, damage, cmd.SourceId, cmd.SourceName, isCrit),
            ApplyHeal             = (actor, target, amount)         => target.Heal(actor, amount, cmd.SourceId, cmd.SourceName),
            ApplyBuffDebuff       = (target, stat, isPositive, rounds, untilRemoved) => target.AddBuffDebuff(stat, isPositive, rounds, untilRemoved, cmd.SourceId, cmd.SourceName),
            ResolveBuffDebuffTargets = selector => _roster.ResolveBuffDebuffTargets(actor, selector, targets),
            ApplyRegenDrain       = (target, stat, isPositive, rounds, untilRemoved) => target.AddRegenDrain(stat, isPositive, rounds, untilRemoved, cmd.SourceId, cmd.SourceName),
        });
    }

    // True when the attack is evaded. Evasion decays 25% on each successful dodge.
    private bool TryEvade(CombatEntity actor, CombatEntity target, string sourceId, string sourceName)
    {
        float roll = _rng.NextSingle();
        if (roll >= target.Evasion)
        {
            Logger.Debug($"[combat] TryEvade: {target.Name} roll={roll:F3} vs evasion={target.Evasion:F3} -> not evaded");
            return false;
        }

        target.RegisterEvasion(actor, roll, sourceId, sourceName);
        return true;
    }

    private bool RollCrit(CombatEntity a)
    {
        float roll = _rng.NextSingle();
        bool isCrit = roll < a.CritChance;
        Logger.Debug($"[combat] RollCrit: {a.Name} roll={roll:F3} vs critChance={a.CritChance:F3} -> {(isCrit ? "crit" : "no crit")}");
        return isCrit;
    }

    private static int ApplyCritModifier(CombatEntity a, int damage)
    {
        int result = (int)(damage * (CombatBalance.Current.CritBaseMultiplier + a.CritModifier));
        Logger.Debug($"[combat] ApplyCritModifier: {a.Name} damage={damage} critModifier={a.CritModifier:F3} -> {result}");
        return result;
    }

    private bool EvaluateWinCondition()
    {
        var livingAllies  = _roster.GetLivingAllies();
        var livingEnemies = _roster.GetLivingEnemies();

        if (livingAllies.Count > 0 && livingEnemies.Count > 0)
            return false;

        bool playerWon = livingEnemies.Count == 0 && livingAllies.Count > 0;
        CombatEventBus.RaiseCombatOver(playerWon);
        return true;
    }

    // Runs before BuildRound: Speed buffs/debuffs must expire before turn order is sorted.
    internal void TickBuffDebuffsOnEntities()
    {
        foreach (var entity in _roster.GetLivingEntities())
        {
            entity.TickBuffDebuffs();
        }
    }

    // Runs after BuildRound/RoundStarted so the regen/drain HP/TP delta - and its combat-log
    // entries - land as part of the new round rather than the tail of the previous one.
    internal void ApplyRegensDrainsOnEntities()
    {
        foreach (var entity in _roster.GetLivingEntities())
        {
            entity.ProcessRegensDrains();
        }
    }

    private bool IsRoundOver() => _turnOrder.IsRoundOver;
}
