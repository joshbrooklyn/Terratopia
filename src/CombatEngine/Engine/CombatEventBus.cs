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

    // Effects. sourceId/sourceName identify the Tech/Item/MonsterAction that caused the effect
    // (CombatCommand.SourceId/SourceName), distinct from actorId/actorName, the entity performing it.
    public static event Action<string, string, int, string, string, string, string, bool, int, int>? EntityDamaged; // targetId, targetName, amount, actorId, actorName, sourceId, sourceName, isCriticalHit, oldHp, newHp
    public static event Action<string, string, int, string, string, string, string, int, int>? EntityHealed;     // targetId, targetName, amount, actorId, actorName, sourceId, sourceName, oldHp, newHp
    public static event Action<string, string, string, string, float, float, string, string>? AttackEvaded; // attackerId, attackerName, targetId, targetName, oldEvasion, newEvasion, sourceId, sourceName
    // keywordName, actorId, actorName, targetId, targetName, bonus, sourceId, sourceName, useCount.
    // useCount is the keyword's running counter total for its own scope (PowerKeyword.UsageKey),
    // 0 for the stateless HP-threshold keywords which keep no counter.
    public static event Action<string, string, string, string, string, double, string, string, int>? KeywordApplied;

    // Timed buffs/debuffs. Expired covers both natural expiry and an opposite-polarity application
    // cancelling the entry out - either way the buff is gone and the stat has moved back. sourceId/
    // sourceName always identify the entry that expired; counteredBySourceId/Name additionally
    // identify the opposing effect that cancelled it out, and are empty on a natural expiry.
    public static event Action<string, string, BuffDebuffStat, bool, int, bool, int, int, string, string>? BuffDebuffApplied; // entityId, entityName, stat, isPositive, roundsRemaining, untilRemoved, oldValue, newValue, sourceId, sourceName
    public static event Action<string, string, BuffDebuffStat, bool, int, string, string>?           BuffDebuffTicked;  // entityId, entityName, stat, isPositive, roundsRemaining, sourceId, sourceName
    public static event Action<string, string, BuffDebuffStat, bool, int, int, string, string, string, string>? BuffDebuffExpired; // entityId, entityName, stat, isPositive, oldValue, newValue, sourceId, sourceName, counteredBySourceId, counteredBySourceName

    // Timed regen/drain. Unlike a buff/debuff, applying an entry doesn't move a stat by itself - the
    // HP/TP delta only lands once per round, reported via the existing EntityDamaged/EntityHealed/
    // EntityTpChanged events - so there is no oldValue/newValue here. Expired's counteredBySourceId/Name
    // follow the same rule as BuffDebuffExpired above.
    public static event Action<string, string, RegenDrainStat, bool, int, bool, string, string>? RegenDrainApplied; // entityId, entityName, stat, isPositive, roundsRemaining, untilRemoved, sourceId, sourceName
    public static event Action<string, string, RegenDrainStat, bool, int, string, string>?       RegenDrainTicked;  // entityId, entityName, stat, isPositive, roundsRemaining, sourceId, sourceName
    public static event Action<string, string, RegenDrainStat, bool, string, string, string, string>? RegenDrainExpired; // entityId, entityName, stat, isPositive, sourceId, sourceName, counteredBySourceId, counteredBySourceName

    // A passive granted mid-combat by an action's passivesApplied rider. Only raised when the
    // entity did not already own it - PassiveTracker.Add is a no-op on a re-grant, and that no-op
    // is not reported here. Passives never tick or expire on their own, so unlike
    // BuffDebuffApplied/RegenDrainApplied there is no Ticked counterpart - only the Applied/Removed
    // pair below.
    public static event Action<string, string, string, string, string>? PassiveApplied; // entityId, entityName, passiveName, sourceId, sourceName
    // A passive stripping its own ownership (e.g. LivingDead consuming itself on death). Only
    // raised on a genuine removal - PassiveTracker.Remove is a no-op if the entity didn't own the
    // passive, and that no-op is not reported here. No sourceId/sourceName: removal is always the
    // passive acting on itself, so there is no separate causing Tech/Item/MonsterAction to name.
    public static event Action<string, string, string>? PassiveRemoved; // entityId, entityName, passiveName

    // Entity lifecycle
    public static event Action<string, string, int, int, string, string>? EntityTpChanged;     // entityId, entityName, oldTp, newTp, sourceId, sourceName
    public static event Action<string, string, string, string>? EntityDeath; // entityId, entityName, sourceId, sourceName
    public static event Action<string, string, int, int, string, string>? EntityRevived; // entityId, entityName, oldHp, newHp, sourceId, sourceName

    internal static void RaiseRoundStarted(int round, IReadOnlyList<string> turnOrderIds, IReadOnlyList<string> turnOrderNames) => RoundStarted?.Invoke(round, turnOrderIds, turnOrderNames);
    internal static void RaiseRoundEnded(int round)               => RoundEnded?.Invoke(round);
    internal static void RaiseTurnStarted(string entityId, string entityName) => TurnStarted?.Invoke(entityId, entityName);
    internal static void RaiseTurnEnded(string entityId, string entityName)   => TurnEnded?.Invoke(entityId, entityName);
    internal static void RaiseWaitingForTurn(string entityId, string entityName, int currentTp, bool isAlly) => WaitingForTurn?.Invoke(entityId, entityName, currentTp, isAlly);
    internal static void RaiseTargetSelectionRequested(string actorId, string actorName, TargetingType targetingType, IReadOnlyList<string> validTargetIds, IReadOnlyList<string> validTargetNames, int numAttacks, bool allowMultipleAttackOnSameTarget) => TargetSelectionRequested?.Invoke(actorId, actorName, targetingType, validTargetIds, validTargetNames, numAttacks, allowMultipleAttackOnSameTarget);
    internal static void RaiseCombatOver(bool playerWon)           => CombatOver?.Invoke(playerWon);
    internal static void RaiseEntityDamaged(string targetId, string targetName, int amount, string actorId, string actorName, string sourceId, string sourceName, bool isCriticalHit, int oldHp, int newHp) => EntityDamaged?.Invoke(targetId, targetName, amount, actorId, actorName, sourceId, sourceName, isCriticalHit, oldHp, newHp);
    internal static void RaiseEntityHealed(string targetId, string targetName, int amount, string actorId, string actorName, string sourceId, string sourceName, int oldHp, int newHp) => EntityHealed?.Invoke(targetId, targetName, amount, actorId, actorName, sourceId, sourceName, oldHp, newHp);
    internal static void RaiseAttackEvaded(string attackerId, string attackerName, string targetId, string targetName, float oldEvasion, float newEvasion, string sourceId, string sourceName) => AttackEvaded?.Invoke(attackerId, attackerName, targetId, targetName, oldEvasion, newEvasion, sourceId, sourceName);
    internal static void RaiseKeywordApplied(string keywordName, string actorId, string actorName, string targetId, string targetName, double bonus, string sourceId, string sourceName, int useCount) => KeywordApplied?.Invoke(keywordName, actorId, actorName, targetId, targetName, bonus, sourceId, sourceName, useCount);
    internal static void RaiseBuffDebuffApplied(string entityId, string entityName, BuffDebuffStat stat, bool isPositive, int roundsRemaining, bool untilRemoved, int oldValue, int newValue, string sourceId, string sourceName) => BuffDebuffApplied?.Invoke(entityId, entityName, stat, isPositive, roundsRemaining, untilRemoved, oldValue, newValue, sourceId, sourceName);
    internal static void RaiseBuffDebuffTicked(string entityId, string entityName, BuffDebuffStat stat, bool isPositive, int roundsRemaining, string sourceId, string sourceName) => BuffDebuffTicked?.Invoke(entityId, entityName, stat, isPositive, roundsRemaining, sourceId, sourceName);
    internal static void RaiseBuffDebuffExpired(string entityId, string entityName, BuffDebuffStat stat, bool isPositive, int oldValue, int newValue, string sourceId, string sourceName, string counteredBySourceId, string counteredBySourceName) => BuffDebuffExpired?.Invoke(entityId, entityName, stat, isPositive, oldValue, newValue, sourceId, sourceName, counteredBySourceId, counteredBySourceName);
    internal static void RaiseRegenDrainApplied(string entityId, string entityName, RegenDrainStat stat, bool isPositive, int roundsRemaining, bool untilRemoved, string sourceId, string sourceName) => RegenDrainApplied?.Invoke(entityId, entityName, stat, isPositive, roundsRemaining, untilRemoved, sourceId, sourceName);
    internal static void RaiseRegenDrainTicked(string entityId, string entityName, RegenDrainStat stat, bool isPositive, int roundsRemaining, string sourceId, string sourceName) => RegenDrainTicked?.Invoke(entityId, entityName, stat, isPositive, roundsRemaining, sourceId, sourceName);
    internal static void RaiseRegenDrainExpired(string entityId, string entityName, RegenDrainStat stat, bool isPositive, string sourceId, string sourceName, string counteredBySourceId, string counteredBySourceName) => RegenDrainExpired?.Invoke(entityId, entityName, stat, isPositive, sourceId, sourceName, counteredBySourceId, counteredBySourceName);
    internal static void RaisePassiveApplied(string entityId, string entityName, string passiveName, string sourceId, string sourceName) => PassiveApplied?.Invoke(entityId, entityName, passiveName, sourceId, sourceName);
    internal static void RaisePassiveRemoved(string entityId, string entityName, string passiveName) => PassiveRemoved?.Invoke(entityId, entityName, passiveName);
    internal static void RaiseEntityTpChanged(string entityId, string entityName, int oldTp, int newTp, string sourceId, string sourceName) => EntityTpChanged?.Invoke(entityId, entityName, oldTp, newTp, sourceId, sourceName);
    internal static void RaiseEntityDeath(string entityId, string entityName, string sourceId, string sourceName) => EntityDeath?.Invoke(entityId, entityName, sourceId, sourceName);
    internal static void RaiseEntityRevived(string entityId, string entityName, int oldHp, int newHp, string sourceId, string sourceName) => EntityRevived?.Invoke(entityId, entityName, oldHp, newHp, sourceId, sourceName);

    public static void Reset()
    {
        RoundStarted           = null;
        RoundEnded             = null;
        TurnStarted            = null;
        TurnEnded              = null;
        WaitingForTurn         = null;
        TargetSelectionRequested = null;
        CombatOver             = null;
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
        PassiveApplied         = null;
        PassiveRemoved         = null;
        EntityTpChanged        = null;
        EntityDeath            = null;
        EntityRevived          = null;
    }
}
