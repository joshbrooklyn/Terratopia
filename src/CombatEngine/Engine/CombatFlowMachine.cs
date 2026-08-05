using Stateless;
using CombatEngine.Enums;
using CombatEngine.DataClasses;

namespace CombatEngine.Engine
{
    
    internal class CombatFlowMachine
    {
        private readonly StateMachine<CombatFlowState, CombatFlowTrigger> _machine;
        private CombatCommand? _pendingCommand;
        private CombatEntity?  _currentEntity;
        private bool _winConditionMet;
        private bool _roundIsOver;

        private readonly Func<CombatEntity, bool>  _isPlayerEntity;
        private readonly Action<CombatCommand>     _resolveAction;
        private readonly Action                    _buildRound;
        private readonly Action                    _doRoundEnd;
        private readonly Func<CombatCommand, IReadOnlyList<CombatEntity>> _getValidTargets;
        private readonly Action<CombatCommand>     _expandAutoTargets;
        private readonly Action<CombatCommand>     _assignAiTarget;
        private readonly Func<bool>                _isRoundOver;
        private readonly Func<bool>                _evaluateWinCondition;
        private readonly Func<CombatEntity?>       _nextTurn;
        private readonly Func<int, bool, int, int> _resolvePickCount;

        public CombatFlowState CurrentState => _machine.State;

        internal void SubmitCommand(CombatCommand cmd)
        {
            _pendingCommand = cmd;
            _machine.Fire(CombatFlowTrigger.CommandSubmitted);
        }

        internal void SubmitTargets(List<string> chosenTargets)
        {
            var validIds = _getValidTargets(_pendingCommand!).Select(e => e.EntityId).ToHashSet();
            var invalid  = chosenTargets.Where(id => !validIds.Contains(id)).ToList();
            if (invalid.Count > 0)
                throw new InvalidOperationException(
                    $"Chosen target(s) not in valid pool: {string.Join(", ", invalid)}");

            _pendingCommand!.ChosenTargets = chosenTargets;
            _machine.Fire(CombatFlowTrigger.TargetsSubmitted);
        }

        public CombatFlowMachine(
            Func<CombatEntity, bool>  isPlayerEntity,
            Action<CombatCommand>     resolveAction,
            Action                    buildRound,
            Action                    doRoundEnd,
            Func<bool>                isRoundOver,
            Func<bool>                evaluateWinCondition,
            Func<CombatCommand, IReadOnlyList<CombatEntity>> getValidTargets,
            Action<CombatCommand>     expandAutoTargets,
            Action<CombatCommand>     assignAiTarget,
            Func<CombatEntity?>       nextTurn,
            Func<int, bool, int, int> resolvePickCount)
        {
            _isPlayerEntity           = isPlayerEntity;
            _resolveAction            = resolveAction;
            _buildRound               = buildRound;
            _doRoundEnd               = doRoundEnd;
            _isRoundOver              = isRoundOver;
            _evaluateWinCondition     = evaluateWinCondition;
            _getValidTargets          = getValidTargets;
            _expandAutoTargets        = expandAutoTargets;
            _assignAiTarget           = assignAiTarget;
            _nextTurn                 = nextTurn;
            _resolvePickCount         = resolvePickCount;

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
                    _buildRound();
                    _machine.Fire(CombatFlowTrigger.RoundBuilt);
                });

            _machine.Configure(CombatFlowState.TurnStart)
                .PermitDynamic(CombatFlowTrigger.TurnReady,
                    () => _currentEntity!.IsDead ? CombatFlowState.CheckWinCondition : CombatFlowState.WaitingForTurn)
                .OnEntry(() =>
                {
                    _currentEntity = _nextTurn();

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
                    if (!_isPlayerEntity(_currentEntity!))
                    {
                        _assignAiTarget(_pendingCommand!);
                        return CombatFlowState.ResolvingAction;
                    }

                    if (_pendingCommand!.TargetingType is TargetingType.Choose)
                        return CombatFlowState.WaitingForTargetSelection;

                    _expandAutoTargets(_pendingCommand!);
                    return CombatFlowState.ResolvingAction;
                })
                .OnEntry(() =>
                {
                    CombatEventBus.RaiseWaitingForTurn(
                        _currentEntity!.EntityId, _currentEntity!.Name, _currentEntity!.Tp,
                        _isPlayerEntity(_currentEntity!));
                });

            _machine.Configure(CombatFlowState.WaitingForTargetSelection)
                .Permit(CombatFlowTrigger.TargetsSubmitted, CombatFlowState.ResolvingAction)
                .OnEntry(() =>
                {
                    var validTargets = _getValidTargets(_pendingCommand!);
                    var validIds   = validTargets.Select(e => e.EntityId).ToList();
                    var validNames = validTargets.Select(e => e.Name).ToList();
                    int numAttacks = _resolvePickCount(
                        _pendingCommand!.NumAttacks, _pendingCommand!.AllowMultipleAttackOnSameTarget, validTargets.Count);
                    CombatEventBus.RaiseTargetSelectionRequested(
                        _pendingCommand!.ActorId, _currentEntity!.Name, _pendingCommand!.TargetingType,
                        validIds, validNames, numAttacks, _pendingCommand!.AllowMultipleAttackOnSameTarget);
                });

            _machine.Configure(CombatFlowState.ResolvingAction)
                .Permit(CombatFlowTrigger.ActionResolved, CombatFlowState.TurnEnd)
                .OnEntry(() =>
                {
                    _resolveAction(_pendingCommand!);
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
                    _doRoundEnd();
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
                    _winConditionMet = _evaluateWinCondition();
                    _roundIsOver     = _isRoundOver();
                    _machine.Fire(CombatFlowTrigger.WinConditionChecked);
                });

            _machine.Configure(CombatFlowState.CombatOver);                

            _machine.OnTransitioned(t =>
                Console.WriteLine($"[flow] {t.Source} → {t.Destination}"));
        }        
    }
}