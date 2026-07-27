using CombatEngine.DataClasses;
using CombatEngine.Enums;

namespace CombatEngine;

public static class CombatEventBus
{
    // Turn flow
    public static event Action<int, IReadOnlyList<string>, IReadOnlyList<string>>? RoundStarted; // round, turnOrderIds, turnOrderNames
    public static event Action<int>? RoundEnded;
    public static event Action<string, string>? TurnStarted; // entityId, entityName
    public static event Action<string, string>? TurnEnded;   // entityId, entityName
    public static event Action<string, string, int, bool>? WaitingForTurn;   // entityId, entityName, currentTp, isAlly
    public static event Action<string, string, TargetingType, IReadOnlyList<string>, IReadOnlyList<string>>? TargetSelectionRequested; // actorId, actorName, targetingType, validTargetIds, validTargetNames
    public static event Action<bool>? CombatOver; // playerWon

    // Action resolution
    public static event Action<CombatCommand, string, string>? ActionRejected; // command, actorName, reason
    public static event Action<CombatCommand, string>? ActionResolved;         // command, actorName

    // Effects
    public static event Action<string, string, int, string, string, bool>? EntityDamaged; // targetId, targetName, amount, sourceId, sourceName, isCriticalHit
    public static event Action<string, string, int, string, string>? EntityHealed;     // targetId, targetName, amount, sourceId, sourceName
    public static event Action<string, string, string, string>? AttackEvaded;          // attackerId, attackerName, targetId, targetName

    // Entity lifecycle
    public static event Action<string, string, int, int>? EntityHpChanged;     // entityId, entityName, oldHp, newHp
    public static event Action<string, string, int, int>? EntityTpChanged;     // entityId, entityName, oldTp, newTp
    public static event Action<string, string, int, int>? EntityMaxHpChanged;  // entityId, entityName, oldMaxHp, newMaxHp
    public static event Action<string, string, int, int>? EntityMaxTpChanged;  // entityId, entityName, oldMaxTp, newMaxTp
    public static event Action<string, string>? EntityDeath; // entityId, entityName

    public static void RaiseRoundStarted(int round, IReadOnlyList<string> turnOrderIds, IReadOnlyList<string> turnOrderNames) => RoundStarted?.Invoke(round, turnOrderIds, turnOrderNames);
    public static void RaiseRoundEnded(int round)               => RoundEnded?.Invoke(round);
    public static void RaiseTurnStarted(string entityId, string entityName) => TurnStarted?.Invoke(entityId, entityName);
    public static void RaiseTurnEnded(string entityId, string entityName)   => TurnEnded?.Invoke(entityId, entityName);
    public static void RaiseWaitingForTurn(string entityId, string entityName, int currentTp, bool isAlly) => WaitingForTurn?.Invoke(entityId, entityName, currentTp, isAlly);
    public static void RaiseTargetSelectionRequested(string actorId, string actorName, TargetingType targetingType, IReadOnlyList<string> validTargetIds, IReadOnlyList<string> validTargetNames) => TargetSelectionRequested?.Invoke(actorId, actorName, targetingType, validTargetIds, validTargetNames);
    public static void RaiseCombatOver(bool playerWon)           => CombatOver?.Invoke(playerWon);
    public static void RaiseActionRejected(CombatCommand c, string actorName, string reason) => ActionRejected?.Invoke(c, actorName, reason);
    public static void RaiseActionResolved(CombatCommand c, string actorName) => ActionResolved?.Invoke(c, actorName);
    public static void RaiseEntityDamaged(string targetId, string targetName, int amount, string sourceId, string sourceName, bool isCriticalHit) => EntityDamaged?.Invoke(targetId, targetName, amount, sourceId, sourceName, isCriticalHit);
    public static void RaiseEntityHealed(string targetId, string targetName, int amount, string sourceId, string sourceName) => EntityHealed?.Invoke(targetId, targetName, amount, sourceId, sourceName);
    public static void RaiseAttackEvaded(string attackerId, string attackerName, string targetId, string targetName) => AttackEvaded?.Invoke(attackerId, attackerName, targetId, targetName);
    public static void RaiseEntityHpChanged(string entityId, string entityName, int oldHp, int newHp) => EntityHpChanged?.Invoke(entityId, entityName, oldHp, newHp);
    public static void RaiseEntityTpChanged(string entityId, string entityName, int oldTp, int newTp) => EntityTpChanged?.Invoke(entityId, entityName, oldTp, newTp);
    public static void RaiseEntityMaxHpChanged(string entityId, string entityName, int oldMaxHp, int newMaxHp) => EntityMaxHpChanged?.Invoke(entityId, entityName, oldMaxHp, newMaxHp);
    public static void RaiseEntityMaxTpChanged(string entityId, string entityName, int oldMaxTp, int newMaxTp) => EntityMaxTpChanged?.Invoke(entityId, entityName, oldMaxTp, newMaxTp);
    public static void RaiseEntityDeath(string entityId, string entityName) => EntityDeath?.Invoke(entityId, entityName);

    public static void Reset()
    {
        RoundStarted           = null;
        RoundEnded             = null;
        TurnStarted            = null;
        TurnEnded              = null;
        WaitingForTurn         = null;
        TargetSelectionRequested = null;
        CombatOver             = null;
        ActionRejected         = null;
        ActionResolved         = null;
        EntityDamaged          = null;
        EntityHealed           = null;
        AttackEvaded           = null;
        EntityHpChanged        = null;
        EntityTpChanged        = null;
        EntityMaxHpChanged     = null;
        EntityMaxTpChanged     = null;
        EntityDeath            = null;
    }
}
