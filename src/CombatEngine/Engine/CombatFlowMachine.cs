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
        private readonly Func<CombatEntity, CombatCommand> _chooseAiCommand;
        private readonly Action<CombatCommand>     _assignAiTarget;
        private readonly Func<bool>                _isRoundOver;
        private readonly Func<bool>                _evaluateWinCondition;
        private readonly Func<CombatEntity?>       _nextTurn;

        public CombatFlowState CurrentState => _machine.State;

        internal void SubmitCommand(CombatCommand cmd)
        {
            _pendingCommand = cmd;
            _machine.Fire(CombatFlowTrigger.PlayerSubmittedCommand);
        }

        internal void SubmitTargets(List<string> chosenTargets)
        {
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
            Func<CombatEntity, CombatCommand> chooseAiCommand,
            Action<CombatCommand>     assignAiTarget,
            Func<CombatEntity?>       nextTurn
            )
        {
            _isPlayerEntity           = isPlayerEntity;
            _resolveAction            = resolveAction;
            _buildRound               = buildRound;
            _doRoundEnd               = doRoundEnd;
            _isRoundOver              = isRoundOver;
            _evaluateWinCondition     = evaluateWinCondition;
            _getValidTargets          = getValidTargets;
            _expandAutoTargets        = expandAutoTargets;
            _chooseAiCommand          = chooseAiCommand;
            _assignAiTarget           = assignAiTarget;
            _nextTurn                 = nextTurn;


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
                .Permit(CombatFlowTrigger.PlayerTurn, CombatFlowState.WaitingForPlayerAction)
                .Permit(CombatFlowTrigger.EnemyTurn,  CombatFlowState.AIDeciding)
                .OnEntry(() =>
                {
                    _currentEntity = _nextTurn();
                    CombatEventBus.RaiseTurnStarted(_currentEntity!.EntityId, _currentEntity!.Name);
                    _machine.Fire(_isPlayerEntity(_currentEntity!)
                        ? CombatFlowTrigger.PlayerTurn
                        : CombatFlowTrigger.EnemyTurn);
                });

            _machine.Configure(CombatFlowState.WaitingForPlayerAction)
                .PermitDynamic(CombatFlowTrigger.PlayerSubmittedCommand, () =>
                {
                    if (_pendingCommand!.TargetingType is TargetingType.Choose or TargetingType.SelectiveMulti)
                        return CombatFlowState.WaitingForTargetSelection;

                    _expandAutoTargets(_pendingCommand!);
                    return CombatFlowState.ResolvingAction;
                })
                .OnEntry(() =>
                {
                    CombatEventBus.RaiseWaitingForPlayerAction(_currentEntity!.EntityId, _currentEntity!.Name, _currentEntity!.Tp);
                });

            _machine.Configure(CombatFlowState.WaitingForTargetSelection)
                .Permit(CombatFlowTrigger.TargetsSubmitted, CombatFlowState.ResolvingAction)
                .OnEntry(() =>
                {
                    var validTargets = _getValidTargets(_pendingCommand!);
                    var validIds   = validTargets.Select(e => e.EntityId).ToList();
                    var validNames = validTargets.Select(e => e.Name).ToList();
                    CombatEventBus.RaiseTargetSelectionRequested(
                        _pendingCommand!.ActorId, _currentEntity!.Name, _pendingCommand!.TargetingType, validIds, validNames);
                });

            _machine.Configure(CombatFlowState.AIDeciding)
                .Permit(CombatFlowTrigger.AIResolved, CombatFlowState.ResolvingAction)
                .OnEntry(() =>
                {
                    _pendingCommand = _chooseAiCommand(_currentEntity!);
                    _assignAiTarget(_pendingCommand);
                    _machine.Fire(CombatFlowTrigger.AIResolved);
                });

            _machine.Configure(CombatFlowState.ResolvingAction)
                .Permit(CombatFlowTrigger.ActionResolved, CombatFlowState.TurnEnd)
                .OnEntry(() =>
                {
                    _resolveAction(_pendingCommand!);
                    CombatEventBus.RaiseActionResolved(_pendingCommand!, _currentEntity!.Name);
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
                //.OnEntry(() => CALL RESET HERE

            _machine.OnTransitioned(t =>
                Console.WriteLine($"[flow] {t.Source} → {t.Destination}"));
        }        
    }
}