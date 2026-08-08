# CombatEngine Public Interface

Reference for the public API surface of the `CombatEngine` assembly, centered on `CombatEngineClass` (`CombatEngine.Engine`).

## `CombatEngineClass`

Singleton that owns and drives a single combat encounter.

```csharp
public static CombatEngineClass Instance { get; }
```
Access point for the singleton. There is no public constructor.

### Lifecycle

```csharp
public void InitCombat(
    IReadOnlyList<CombatEntity> allies,
    IReadOnlyList<CombatEntity> enemies)
```
Resets combat state and wires up a new `CombatFlowMachine` for the given roster.

The engine does not decide AI actions itself. Callers subscribe to `CombatEventBus.WaitingForTurn` and, when `isAlly` is `false`, decide how to pick that entity's command themselves (in this project, via `GameEngine.GameEngineClass.Instance.ChooseAiCommand(entityId)`) before calling `SubmitCommand`. The engine still applies its own default single-random-target assignment to whatever command a non-player actor submits, regardless of the command's `TargetingType`.

```csharp
public void BeginCombat()
```
Starts the combat flow (first round, first turn) after `InitCombat` has been called.

### Submitting a turn's command

```csharp
public void SubmitCommand(CombatCommand cmd)
```
Gives the flow machine the command chosen for the current turn's actor, ally or enemy alike.

```csharp
public void SubmitTargets(List<string> chosenTargetIds)
```
Supplies target entity IDs once the flow machine has requested target selection for the pending command (ally turns only — enemy turns always get a single random target assigned automatically).

### Consuming engine output

The engine does not return results from actions — it reports everything through `CombatEventBus` (see below). Callers should subscribe to the relevant events after `InitCombat` and before `BeginCombat`.

## Supporting public types

### `CombatEventBus` (`CombatEngine`)

Static event bus; the engine's only channel for reporting what happened. All events use IDs/names/primitives, never live `CombatEntity` references. `CombatEventBus.Reset()` clears all subscribers and is called automatically by `InitCombat`.

| Event | Signature | Payload |
|---|---|---|
| `RoundStarted` | `Action<int, IReadOnlyList<string>, IReadOnlyList<string>>` | round, turnOrderIds, turnOrderNames |
| `RoundEnded` | `Action<int>` | round |
| `TurnStarted` | `Action<string, string>` | entityId, entityName |
| `TurnEnded` | `Action<string, string>` | entityId, entityName |
| `WaitingForTurn` | `Action<string, string, int, bool>` | entityId, entityName, currentTp, isAlly — `isAlly` tells the caller which side must act |
| `TargetSelectionRequested` | `Action<string, string, TargetingType, IReadOnlyList<string>, IReadOnlyList<string>, int, bool>` | actorId, actorName, targetingType, validTargetIds, validTargetNames, numAttacks, allowMultipleAttackOnSameTarget — `numAttacks` is already capped to the valid-target pool size when repeats aren't allowed |
| `CombatOver` | `Action<bool>` | playerWon |
| `EntityDamaged` | `Action<string, string, int, string, string, string, string, bool, int, int>` | targetId, targetName, amount, actorId, actorName, sourceId, sourceName, isCriticalHit, oldHp, newHp — sourceId/sourceName identify the Tech/Item/MonsterAction (`CombatCommand.SourceId`/`SourceName`) that caused the damage, distinct from actorId/actorName, the entity that dealt it |
| `EntityHealed` | `Action<string, string, int, string, string, string, string, int, int>` | targetId, targetName, amount, actorId, actorName, sourceId, sourceName, oldHp, newHp |
| `AttackEvaded` | `Action<string, string, string, string, float, float, string, string>` | attackerId, attackerName, targetId, targetName, oldEvasion, newEvasion, sourceId, sourceName |
| `KeywordApplied` | `Action<string, string, string, string, string, double, string, string, int>` | keywordName, actorId, actorName, targetId, targetName, bonus, sourceId, sourceName, useCount |
| `BuffDebuffApplied` | `Action<string, string, BuffDebuffStat, bool, int, bool, int, int, string, string>` | entityId, entityName, stat, isPositive, roundsRemaining, untilRemoved, oldValue, newValue, sourceId, sourceName |
| `BuffDebuffTicked` | `Action<string, string, BuffDebuffStat, bool, int, string, string>` | entityId, entityName, stat, isPositive, roundsRemaining, sourceId, sourceName |
| `BuffDebuffExpired` | `Action<string, string, BuffDebuffStat, bool, int, int, string, string, string, string>` | entityId, entityName, stat, isPositive, oldValue, newValue, sourceId, sourceName, counteredBySourceId, counteredBySourceName — sourceId/sourceName always identify the entry that expired; counteredBySourceId/Name additionally identify the opposing effect that cancelled it, and are empty on a natural expiry |
| `RegenDrainApplied` | `Action<string, string, RegenDrainStat, bool, int, bool, string, string>` | entityId, entityName, stat, isPositive, roundsRemaining, untilRemoved, sourceId, sourceName — no oldValue/newValue, since applying an entry doesn't move the resource by itself; see [`regen-and-drain.md`](regen-and-drain.md) |
| `RegenDrainTicked` | `Action<string, string, RegenDrainStat, bool, int, string, string>` | entityId, entityName, stat, isPositive, roundsRemaining, sourceId, sourceName |
| `RegenDrainExpired` | `Action<string, string, RegenDrainStat, bool, string, string, string, string>` | entityId, entityName, stat, isPositive, sourceId, sourceName, counteredBySourceId, counteredBySourceName — same counteredBy rule as `BuffDebuffExpired` |
| `TriggeredEffectApplied` | `Action<string, string, string, string, string>` | entityId, entityName, triggeredEffectName, sourceId, sourceName — raised when a `triggeredEffectsApplied` rider actually grants a triggered effect the entity didn't already own |
| `TriggeredEffectRemoved` | `Action<string, string, string>` | entityId, entityName, triggeredEffectName — raised when a triggered effect strips its own ownership (e.g. `LivingDead` consuming itself); no sourceId/sourceName, since removal is always the triggered effect acting on itself |
| `EntityTpChanged` | `Action<string, string, int, int, string, string>` | entityId, entityName, oldTp, newTp, sourceId, sourceName — also raised by the per-round Hp/Tp regen/drain delta, alongside `EntityDamaged`/`EntityHealed` |
| `EntityDeath` | `Action<string, string, string, string>` | entityId, entityName, sourceId, sourceName |
| `EntityRevived` | `Action<string, string, int, int, string, string>` | entityId, entityName, oldHp, newHp, sourceId, sourceName |

### `CombatEntity` (`CombatEngine.DataClasses`)

Mutable combat participant passed into `InitCombat`. Public constructor takes the full stat block (`entityId, name, level, maxHp, hp, maxTp, tp, power, defense, speed, evasion, critChance, critModifier`). All properties are `internal` — callers construct instances to hand to `InitCombat` but cannot read state back off them afterward; the engine owns and mutates them during combat and reports everything through `CombatEventBus` instead.

### `CombatCommand` (`CombatEngine.DataClasses`)

Describes an action being taken. Constructed by callers (player UI or the caller's own AI decision logic, e.g. `GameEngine.ChooseAiCommand`) and passed to `SubmitCommand`.

- `string ActorId` — entity performing the action.
- `TargetingType TargetingType` — how targets are determined (see enum below).
- `ValidTarget ValidTargets` *(required)* — which side(s) may be targeted.
- `LivingOrDead LivingOrDead` *(required)* — living/dead filter on targets.
- `int TPCost` — TP deducted from the actor on resolution.
- `int NumAttacks` — number of separate attack instances this command performs (default 1).
- `bool AllowMultipleAttackOnSameTarget` — whether the same target may be chosen/picked more than once across the `NumAttacks` attacks (default false; when false and the valid-target pool is smaller than `NumAttacks`, the required picks are capped to the pool size rather than forcing repeats).
- `string CombatFunction` *(required)* — name of the `CombatFunction` this command resolves through, looked up via `CombatFunctionRegistry`.
- `CombatFunctionParameters Parameters` — the flat parameter bag the resolved `CombatFunction` reads its inputs from (element, calc type, power factor, buffsDebuffs, regensDrains).
- `List<string> Keywords` — power keyword names active on this command, resolved via `PowerKeywordRegistry`; see [keywords.md](keywords.md).
- `string SourceId` / `string SourceName` — the Tech/Item/MonsterAction ID and display name this command came from (empty for the basic Fight action's synthetic `"fight"`/`"Fight"`). Used both for stacking-keyword bookkeeping (e.g. Growth telling "used this action again" from "used a different action") and echoed onto every effect event `CombatEventBus` raises as a result of this command, so callers can report what caused an effect without a separate lookup.
- `List<string> ChosenTargets` — target entity IDs, one per attack instance; publicly gettable, set internally by the engine (via `SubmitTargets` or auto-target expansion).

### `CombatFunctionParameters` (`CombatEngine.DataClasses`)

The parameter bag a `CombatFunction` reads its inputs from: `ElementType? Element`, `DamageOrHealCalcType? CalcType`, `double? PowerFactor`, `IReadOnlyList<BuffDebuffSpec>? BuffsDebuffs`, `IReadOnlyList<RegenDrainSpec>? RegensDrains`. Every field is optional; each `CombatFunction` decides which ones it requires. See [combat-functions.md](combat-functions.md).

### Enums (`CombatEngine.Enums`)

- `ValidTarget`: `Allies`, `Enemies`, `Both`
- `LivingOrDead`: `Living`, `Dead`, `Both`
- `TargetingType`: `Choose`, `Random`, `All`, `Self`
- `DamageOrHealCalcType`: `StandardFormula`, `FixedPower`, `FixedAmount`, `PercentOfMax` — see [damage-or-heal-calc-type.md](damage-or-heal-calc-type.md)
- `ElementType`: `Fire`, `Ice`, `Lightning`, `Void`

## Typical usage

```csharp
CombatEngineClass.Instance.InitCombat(allies, enemies);
// subscribe to CombatEventBus events here, including WaitingForTurn
CombatEngineClass.Instance.BeginCombat();

// later, when WaitingForTurn fires:
if (isAlly)
{
    // show UI, then once the player picks an action:
    CombatEngineClass.Instance.SubmitCommand(cmd);
}
else
{
    // decide the enemy's action yourself (e.g. GameEngine.ChooseAiCommand)
    CombatEngineClass.Instance.SubmitCommand(enemyCmd);
}
// if TargetSelectionRequested fires (ally turns only): collect `numAttacks` picks
// (the event's payload), then call SubmitTargets once with the full list.
CombatEngineClass.Instance.SubmitTargets(targetIds);
```
