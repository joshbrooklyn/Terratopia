namespace CombatEngine.Enums;

public enum CombatFlowState
{
    Idle,
    RoundStart,
    TurnStart,
    WaitingForTurn,
    WaitingForTargetSelection,
    ResolvingAction,
    TurnEnd,
    RoundEnd,
    CheckWinCondition,
    CombatOver,
}
