namespace CombatEngine.Enums;

public enum CombatFlowState
{
    Idle,
    RoundStart,
    TurnStart,
    WaitingForPlayerAction,
    WaitingForTargetSelection,
    AIDeciding,
    ResolvingAction,
    TurnEnd,
    RoundEnd,
    CheckWinCondition,
    CombatOver,
}
