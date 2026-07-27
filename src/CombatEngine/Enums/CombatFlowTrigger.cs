namespace CombatEngine.Enums;

public enum CombatFlowTrigger
{
    CombatStarted,
    RoundBuilt,
    PlayerTurn,
    EnemyTurn,
    PlayerSubmittedCommand,
    TargetsSubmitted,
    AIResolved,
    ActionResolved,
    //ActionValidated,
    //ActionRejected,
    //EffectsReady,
    //EffectsApplied,
    //TriggerQueued,
    //TriggerQueueEmpty,
    TurnComplete,
    //RoundOver,
    RoundComplete,
    //CombatContinues,
    //CombatEnded,
    WinConditionChecked,
}
