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
    public static event Action<string, string, TargetingType, IReadOnlyList<string>, IReadOnlyList<string>, int, bool>? TargetSelectionRequested; // actorId, actorName, targetingType, validTargetIds, validTargetNames, numAttacks, allowMultipleAttackOnSameTarget
    public static event Action<bool>? CombatOver; // playerWon

    // Action resolution
    public static event Action<CombatCommand, string, string>? ActionRejected; // command, actorName, reason
    public static event Action<CombatCommand, string, IReadOnlyList<string>>? ActionResolved; // command, actorName, targetNames

    // Effects. sourceId/sourceName identify the Tech/Item/MonsterAction that caused the effect
    // (CombatCommand.SourceId/SourceName), distinct from actorId/actorName, the entity performing it.
    public static event Action<string, string, int, string, string, string, string, bool, int, int>? EntityDamaged; // targetId, targetName, amount, actorId, actorName, sourceId, sourceName, isCriticalHit, oldHp, newHp
    public static event Action<string, string, int, string, string, string, string, int, int>? EntityHealed;     // targetId, targetName, amount, actorId, actorName, sourceId, sourceName, oldHp, newHp
    public static event Action<string, string, string, string, float, float, string, string>? AttackEvaded; // attackerId, attackerName, targetId, targetName, oldEvasion, newEvasion, sourceId, sourceName
    public static event Action<string, string, string, string, string, double, string, string>? KeywordApplied; // keywordName, actorId, actorName, targetId, targetName, bonus, sourceId, sourceName

    // Timed buffs/debuffs. Expired covers both natural expiry and an opposite-polarity application
    // cancelling the entry out - either way the buff is gone and the stat has moved back.
    public static event Action<string, string, BuffDebuffStat, bool, int, bool, int, int, string, string>? BuffDebuffApplied; // entityId, entityName, stat, isPositive, roundsRemaining, untilRemoved, oldValue, newValue, sourceId, sourceName
    public static event Action<string, string, BuffDebuffStat, bool, int, string, string>?           BuffDebuffTicked;  // entityId, entityName, stat, isPositive, roundsRemaining, sourceId, sourceName
    public static event Action<string, string, BuffDebuffStat, bool, int, int, string, string>?      BuffDebuffExpired; // entityId, entityName, stat, isPositive, oldValue, newValue, sourceId, sourceName

    // Timed regen/drain. Unlike a buff/debuff, applying an entry doesn't move a stat by itself - the
    // HP/TP delta only lands once per round, reported via the existing EntityDamaged/EntityHealed/
    // EntityTpChanged events - so there is no oldValue/newValue here.
    public static event Action<string, string, RegenDrainStat, bool, int, bool, string, string>? RegenDrainApplied; // entityId, entityName, stat, isPositive, roundsRemaining, untilRemoved, sourceId, sourceName
    public static event Action<string, string, RegenDrainStat, bool, int, string, string>?       RegenDrainTicked;  // entityId, entityName, stat, isPositive, roundsRemaining, sourceId, sourceName
    public static event Action<string, string, RegenDrainStat, bool, string, string>?            RegenDrainExpired; // entityId, entityName, stat, isPositive, sourceId, sourceName

    // Entity lifecycle
    public static event Action<string, string, int, int, string, string>? EntityTpChanged;     // entityId, entityName, oldTp, newTp, sourceId, sourceName
    public static event Action<string, string, int, int>? EntityMaxHpChanged;  // entityId, entityName, oldMaxHp, newMaxHp
    public static event Action<string, string, int, int>? EntityMaxTpChanged;  // entityId, entityName, oldMaxTp, newMaxTp
    public static event Action<string, string, string, string>? EntityDeath; // entityId, entityName, sourceId, sourceName
    public static event Action<string, string, int, int, string, string>? EntityRevived; // entityId, entityName, oldHp, newHp, sourceId, sourceName

    public static void RaiseRoundStarted(int round, IReadOnlyList<string> turnOrderIds, IReadOnlyList<string> turnOrderNames) => RoundStarted?.Invoke(round, turnOrderIds, turnOrderNames);
    public static void RaiseRoundEnded(int round)               => RoundEnded?.Invoke(round);
    public static void RaiseTurnStarted(string entityId, string entityName) => TurnStarted?.Invoke(entityId, entityName);
    public static void RaiseTurnEnded(string entityId, string entityName)   => TurnEnded?.Invoke(entityId, entityName);
    public static void RaiseWaitingForTurn(string entityId, string entityName, int currentTp, bool isAlly) => WaitingForTurn?.Invoke(entityId, entityName, currentTp, isAlly);
    public static void RaiseTargetSelectionRequested(string actorId, string actorName, TargetingType targetingType, IReadOnlyList<string> validTargetIds, IReadOnlyList<string> validTargetNames, int numAttacks, bool allowMultipleAttackOnSameTarget) => TargetSelectionRequested?.Invoke(actorId, actorName, targetingType, validTargetIds, validTargetNames, numAttacks, allowMultipleAttackOnSameTarget);
    public static void RaiseCombatOver(bool playerWon)           => CombatOver?.Invoke(playerWon);
    public static void RaiseActionRejected(CombatCommand c, string actorName, string reason) => ActionRejected?.Invoke(c, actorName, reason);
    public static void RaiseActionResolved(CombatCommand c, string actorName, IReadOnlyList<string> targetNames) => ActionResolved?.Invoke(c, actorName, targetNames);
    public static void RaiseEntityDamaged(string targetId, string targetName, int amount, string actorId, string actorName, string sourceId, string sourceName, bool isCriticalHit, int oldHp, int newHp) => EntityDamaged?.Invoke(targetId, targetName, amount, actorId, actorName, sourceId, sourceName, isCriticalHit, oldHp, newHp);
    public static void RaiseEntityHealed(string targetId, string targetName, int amount, string actorId, string actorName, string sourceId, string sourceName, int oldHp, int newHp) => EntityHealed?.Invoke(targetId, targetName, amount, actorId, actorName, sourceId, sourceName, oldHp, newHp);
    public static void RaiseAttackEvaded(string attackerId, string attackerName, string targetId, string targetName, float oldEvasion, float newEvasion, string sourceId, string sourceName) => AttackEvaded?.Invoke(attackerId, attackerName, targetId, targetName, oldEvasion, newEvasion, sourceId, sourceName);
    public static void RaiseKeywordApplied(string keywordName, string actorId, string actorName, string targetId, string targetName, double bonus, string sourceId, string sourceName) => KeywordApplied?.Invoke(keywordName, actorId, actorName, targetId, targetName, bonus, sourceId, sourceName);
    public static void RaiseBuffDebuffApplied(string entityId, string entityName, BuffDebuffStat stat, bool isPositive, int roundsRemaining, bool untilRemoved, int oldValue, int newValue, string sourceId, string sourceName) => BuffDebuffApplied?.Invoke(entityId, entityName, stat, isPositive, roundsRemaining, untilRemoved, oldValue, newValue, sourceId, sourceName);
    public static void RaiseBuffDebuffTicked(string entityId, string entityName, BuffDebuffStat stat, bool isPositive, int roundsRemaining, string sourceId, string sourceName) => BuffDebuffTicked?.Invoke(entityId, entityName, stat, isPositive, roundsRemaining, sourceId, sourceName);
    public static void RaiseBuffDebuffExpired(string entityId, string entityName, BuffDebuffStat stat, bool isPositive, int oldValue, int newValue, string sourceId, string sourceName) => BuffDebuffExpired?.Invoke(entityId, entityName, stat, isPositive, oldValue, newValue, sourceId, sourceName);
    public static void RaiseRegenDrainApplied(string entityId, string entityName, RegenDrainStat stat, bool isPositive, int roundsRemaining, bool untilRemoved, string sourceId, string sourceName) => RegenDrainApplied?.Invoke(entityId, entityName, stat, isPositive, roundsRemaining, untilRemoved, sourceId, sourceName);
    public static void RaiseRegenDrainTicked(string entityId, string entityName, RegenDrainStat stat, bool isPositive, int roundsRemaining, string sourceId, string sourceName) => RegenDrainTicked?.Invoke(entityId, entityName, stat, isPositive, roundsRemaining, sourceId, sourceName);
    public static void RaiseRegenDrainExpired(string entityId, string entityName, RegenDrainStat stat, bool isPositive, string sourceId, string sourceName) => RegenDrainExpired?.Invoke(entityId, entityName, stat, isPositive, sourceId, sourceName);
    public static void RaiseEntityTpChanged(string entityId, string entityName, int oldTp, int newTp, string sourceId, string sourceName) => EntityTpChanged?.Invoke(entityId, entityName, oldTp, newTp, sourceId, sourceName);
    public static void RaiseEntityMaxHpChanged(string entityId, string entityName, int oldMaxHp, int newMaxHp) => EntityMaxHpChanged?.Invoke(entityId, entityName, oldMaxHp, newMaxHp);
    public static void RaiseEntityMaxTpChanged(string entityId, string entityName, int oldMaxTp, int newMaxTp) => EntityMaxTpChanged?.Invoke(entityId, entityName, oldMaxTp, newMaxTp);
    public static void RaiseEntityDeath(string entityId, string entityName, string sourceId, string sourceName) => EntityDeath?.Invoke(entityId, entityName, sourceId, sourceName);
    public static void RaiseEntityRevived(string entityId, string entityName, int oldHp, int newHp, string sourceId, string sourceName) => EntityRevived?.Invoke(entityId, entityName, oldHp, newHp, sourceId, sourceName);

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
        KeywordApplied         = null;
        BuffDebuffApplied      = null;
        BuffDebuffTicked       = null;
        BuffDebuffExpired      = null;
        RegenDrainApplied      = null;
        RegenDrainTicked       = null;
        RegenDrainExpired      = null;
        EntityTpChanged        = null;
        EntityMaxHpChanged     = null;
        EntityMaxTpChanged     = null;
        EntityDeath            = null;
        EntityRevived          = null;
    }
}
