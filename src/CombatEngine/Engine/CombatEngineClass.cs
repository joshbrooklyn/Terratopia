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
        _roundNumber++;
        _turnOrder.BuildRound(_roster.GetLivingEntities());
        CombatEventBus.RaiseRoundStarted(_roundNumber, _turnOrder.CurrentTurnOrderIds, _turnOrder.CurrentTurnOrderNames);
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
        _keywords.NotifyKeywordsUsed(activeKeywords, actor, actorIsAlly, cmd.ActionId);

        var targets = cmd.ChosenTargets.Select(_roster.GetEntity).ToList();

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
            DeductTp              = (entity, amount)  => entity.SpendTp(amount),
            TryEvade              = TryEvade,
            RollCrit              = RollCrit,
            ApplyCritModifier     = ApplyCritModifier,
            ApplyKeywordBonuses   = (basePower, a, t) => _keywords.ApplyKeywordBonuses(activeKeywords, basePower, a, t, actorIsAlly, cmd.ActionId),
            CalculateDamageAmount = CombatMath.CalculateDamageAmount,
            CalculateHealAmount   = CombatMath.CalculateHealAmount,
            ApplyDamage           = (actor, target, damage, isCrit) => target.TakeDamage(actor, damage, isCrit),
            ApplyHeal             = (actor, target, amount)         => target.Heal(actor, amount),
        });

        CombatEventBus.RaiseActionResolved(cmd, actor.Name, targets.Select(t => t.Name).ToList());
    }

    // True when the attack is evaded. Evasion decays 25% on each successful dodge.
    private bool TryEvade(CombatEntity actor, CombatEntity target)
    {
        float roll = _rng.NextSingle();
        if (roll >= target.Evasion)
        {
            Logger.Debug($"[combat] TryEvade: {target.Name} roll={roll:F3} vs evasion={target.Evasion:F3} -> not evaded");
            return false;
        }

        target.RegisterEvasion(actor, roll);
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

    private bool IsRoundOver() => _turnOrder.IsRoundOver;
}
