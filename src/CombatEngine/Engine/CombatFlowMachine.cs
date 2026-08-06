using Stateless;
using CombatEngine.Enums;
using CombatEngine.DataClasses;

namespace CombatEngine.Engine
{

    internal class CombatFlowMachine
    {
        private readonly StateMachine<CombatFlowState, CombatFlowTrigger> _machine;
        private readonly CombatEngineClass _engine;
        private readonly CombatRoster      _roster;
        private readonly TurnOrderManager  _turnOrder;

        private CombatCommand? _pendingCommand;
        private CombatEntity?  _currentEntity;
        private bool _winConditionMet;
        private bool _roundIsOver;

        internal void SubmitCommand(CombatCommand cmd)
        {
            _pendingCommand = cmd;
            _machine.Fire(CombatFlowTrigger.CommandSubmitted);
        }

        internal void SubmitTargets(List<string> chosenTargets)
        {
            var validIds = _roster.GetValidTargets(_pendingCommand!).Select(e => e.EntityId).ToHashSet();
            var invalid  = chosenTargets.Where(id => !validIds.Contains(id)).ToList();
            if (invalid.Count > 0)
                throw new InvalidOperationException(
                    $"Chosen target(s) not in valid pool: {string.Join(", ", invalid)}");

            _pendingCommand!.ChosenTargets = chosenTargets;
            _machine.Fire(CombatFlowTrigger.TargetsSubmitted);
        }

        public CombatFlowMachine(CombatEngineClass engine, CombatRoster roster, TurnOrderManager turnOrder)
        {
            _engine    = engine;
            _roster    = roster;
            _turnOrder = turnOrder;

            _machine = new StateMachine<CombatFlowState, CombatFlowTrigger>(CombatFlowState.Idle);
            ConfigureMachine();
        }

        public void Start() => _machine.Fire(CombatFlowTrigger.CombatStarted);

        private void ConfigureMachine()
        {
            _machine.Configure(CombatFlowState.Idle)
                .Permit(CombatFlowTrigger.CombatStarted, CombatFlowState.RoundStart);

            _machine.Configure(CombatFlowState.RoundStart)
                .Permit(CombatFlowTrigger.RoundBuilt, CombatFlowState.TurnStart)
                .OnEntry(() =>
                {
                    _engine.BuildRound();
                    _machine.Fire(CombatFlowTrigger.RoundBuilt);
                });

            _machine.Configure(CombatFlowState.TurnStart)
                .PermitDynamic(CombatFlowTrigger.TurnReady,
                    () => _currentEntity!.IsDead ? CombatFlowState.CheckWinCondition : CombatFlowState.WaitingForTurn)
                .OnEntry(() =>
                {
                    _currentEntity = _turnOrder.NextTurn();

                    // Dying doesn't vacate a queue slot built for the round - an entity killed
                    // earlier this round can still be dequeued for "its" turn. Skip it silently:
                    // no TurnStarted/TurnEnded, no chance to submit a command.
                    if (_currentEntity!.IsDead)
                    {
                        _machine.Fire(CombatFlowTrigger.TurnReady);
                        return;
                    }

                    CombatEventBus.RaiseTurnStarted(_currentEntity!.EntityId, _currentEntity!.Name);
                    _machine.Fire(CombatFlowTrigger.TurnReady);
                });

            _machine.Configure(CombatFlowState.WaitingForTurn)
                .PermitDynamic(CombatFlowTrigger.CommandSubmitted, () =>
                {
                    if (!_roster.IsPlayerEntity(_currentEntity!))
                    {
                        _roster.AssignRandomAiTarget(_pendingCommand!);
                        return CombatFlowState.ResolvingAction;
                    }

                    if (_pendingCommand!.TargetingType is TargetingType.Choose)
                        return CombatFlowState.WaitingForTargetSelection;

                    _roster.ExpandAutoTargets(_pendingCommand!);
                    return CombatFlowState.ResolvingAction;
                })
                .OnEntry(() =>
                {
                    CombatEventBus.RaiseWaitingForTurn(
                        _currentEntity!.EntityId, _currentEntity!.Name, _currentEntity!.Tp,
                        _roster.IsPlayerEntity(_currentEntity!));
                });

            _machine.Configure(CombatFlowState.WaitingForTargetSelection)
                .Permit(CombatFlowTrigger.TargetsSubmitted, CombatFlowState.ResolvingAction)
                .OnEntry(() =>
                {
                    var validTargets = _roster.GetValidTargets(_pendingCommand!);
                    var validIds   = validTargets.Select(e => e.EntityId).ToList();
                    var validNames = validTargets.Select(e => e.Name).ToList();
                    int numAttacks = CombatRoster.ResolveRequiredPickCount(
                        _pendingCommand!.NumAttacks, _pendingCommand!.AllowMultipleAttackOnSameTarget, validTargets.Count);
                    CombatEventBus.RaiseTargetSelectionRequested(
                        _pendingCommand!.ActorId, _currentEntity!.Name, _pendingCommand!.TargetingType,
                        validIds, validNames, numAttacks, _pendingCommand!.AllowMultipleAttackOnSameTarget);
                });

            _machine.Configure(CombatFlowState.ResolvingAction)
                .Permit(CombatFlowTrigger.ActionResolved, CombatFlowState.TurnEnd)
                .OnEntry(() =>
                {
                    _engine.ResolveAction(_pendingCommand!);
                    _machine.Fire(CombatFlowTrigger.ActionResolved);
                });

            _machine.Configure(CombatFlowState.TurnEnd)
                .Permit(CombatFlowTrigger.TurnComplete, CombatFlowState.CheckWinCondition)
                .OnEntry(() =>
                {
                    CombatEventBus.RaiseTurnEnded(_currentEntity!.EntityId, _currentEntity!.Name);
                    _pendingCommand = null;
                    _machine.Fire(CombatFlowTrigger.TurnComplete);
                });

            _machine.Configure(CombatFlowState.RoundEnd)
                .Permit(CombatFlowTrigger.RoundComplete, CombatFlowState.RoundStart)
                .OnEntry(() =>
                {
                    _engine.DoRoundEnd();
                    _machine.Fire(CombatFlowTrigger.RoundComplete);
                });

            _machine.Configure(CombatFlowState.CheckWinCondition)
                .PermitIf(CombatFlowTrigger.WinConditionChecked,
                        CombatFlowState.CombatOver,
                        () => _winConditionMet)
                .PermitIf(CombatFlowTrigger.WinConditionChecked,
                        CombatFlowState.RoundEnd,
                        () => !_winConditionMet && _roundIsOver)
                .PermitIf(CombatFlowTrigger.WinConditionChecked,
                        CombatFlowState.TurnStart,
                        () => !_winConditionMet && !_roundIsOver)
                .OnEntry(() =>
                {
                    _winConditionMet = _engine.EvaluateWinCondition();
                    _roundIsOver     = _turnOrder.IsRoundOver;
                    _machine.Fire(CombatFlowTrigger.WinConditionChecked);
                });

            _machine.Configure(CombatFlowState.CombatOver);

            _machine.OnTransitioned(t =>
                Console.WriteLine($"[flow] {t.Source} → {t.Destination}"));
        }
    }
}
