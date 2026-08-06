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
        IReadOnlyList<CombatEntity> enemies)
    {
        CombatEventBus.Reset();
        _roundNumber = 0;

        _roster   = new CombatRoster(allies, enemies, _rng);
        _keywords = new KeywordResolver();

        _combatFlowMachine = new CombatFlowMachine(this, _roster, _turnOrder);
    }

    public void SubmitTargets(List<string> chosenTargetIds) =>
        _combatFlowMachine.SubmitTargets(chosenTargetIds);

    public void SubmitCommand(CombatCommand cmd) =>
        _combatFlowMachine.SubmitCommand(cmd);

    public void BeginCombat()
    {
        _combatFlowMachine.Start();
    }

    internal IReadOnlyList<CombatEntity> GetValidTargets(CombatCommand cmd) =>
        _roster.GetValidTargets(cmd);

    internal void BuildRound()
    {
        // Buff/debuff ticking must happen first: turn order is sorted by Speed, so a Speed buff
        // expiring this round must be gone before the order is decided. Regen/drain has no
        // bearing on turn order, so it's applied after RoundStarted fires - otherwise its HP/TP
        // delta (and combat-log entries) would land before the round-start announcement instead
        // of after it.
        foreach (var entity in _roster.GetLivingEntities())
        {
            entity.TickBuffDebuffs();
        }

        _roundNumber++;
        _turnOrder.BuildRound(_roster.GetLivingEntities());
        CombatEventBus.RaiseRoundStarted(_roundNumber, _turnOrder.CurrentTurnOrderIds, _turnOrder.CurrentTurnOrderNames);

        // Runs after RoundStarted fires so the regen/drain HP/TP delta - and its combat-log
        // entries - land as part of the new round rather than the tail of the previous one.
        foreach (var entity in _roster.GetLivingEntities())
        {
            entity.ProcessRegensDrains();
        }
    }

    internal void DoRoundEnd()
    {
        CombatEventBus.RaiseRoundEnded(_roundNumber);
    }

    internal void ResolveAction(CombatCommand cmd)
    {
        if (!_roster.AllEntities.TryGetValue(cmd.ActorId, out var actor) || actor == null)
            throw new InvalidOperationException($"Actor with ID {cmd.ActorId} not found among combat entities.");

        var function = CombatFunctionRegistry.Resolve(cmd.CombatFunction);

        bool actorIsAlly    = _roster.IsPlayerEntity(actor);
        var  activeKeywords = PowerKeywordRegistry.Resolve(cmd.Keywords).ToList();
        _keywords.NotifyKeywordsUsed(activeKeywords, actor, actorIsAlly, cmd.SourceId);

        var targets = cmd.ChosenTargets.Select(_roster.GetEntity).ToList();

        function.Execute(new CombatFunctionContext(_roster, _keywords, activeKeywords)
        {
            Command     = cmd,
            Actor       = actor,
            ActorIsAlly = actorIsAlly,
            Targets     = targets,
            Rng         = _rng,
        });
    }

    internal bool EvaluateWinCondition()
    {
        var livingAllies  = _roster.GetLivingAllies();
        var livingEnemies = _roster.GetLivingEnemies();

        if (livingAllies.Count > 0 && livingEnemies.Count > 0)
            return false;

        bool playerWon = livingEnemies.Count == 0 && livingAllies.Count > 0;
        CombatEventBus.RaiseCombatOver(playerWon);
        return true;
    }
}
