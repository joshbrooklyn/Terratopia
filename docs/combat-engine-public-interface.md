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
    IReadOnlyList<CombatEntity> enemies,
    bool isBossFight = false)
```
Resets combat state and wires up a new `CombatFlowMachine` for the given roster. `isBossFight` is accepted but not currently read by the engine.

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
| `ActionRejected` | `Action<CombatCommand, string, string>` | command, actorName, reason |
| `ActionResolved` | `Action<CombatCommand, string, IReadOnlyList<string>>` | command, actorName, targetNames |
| `EntityDamaged` | `Action<string, string, int, string, string, bool, int, int>` | targetId, targetName, amount, sourceId, sourceName, isCriticalHit, oldHp, newHp |
| `EntityHealed` | `Action<string, string, int, string, string, int, int>` | targetId, targetName, amount, sourceId, sourceName, oldHp, newHp |
| `AttackEvaded` | `Action<string, string, string, string, float, float>` | attackerId, attackerName, targetId, targetName, oldEvasion, newEvasion |
| `KeywordApplied` | `Action<string, string, string, string, string, double>` | keywordName, actorId, actorName, targetId, targetName, bonus |
| `BuffDebuffApplied` | `Action<string, string, BuffDebuffStat, bool, int, bool, int, int>` | entityId, entityName, stat, isPositive, roundsRemaining, untilRemoved, oldValue, newValue |
| `BuffDebuffTicked` | `Action<string, string, BuffDebuffStat, bool, int>` | entityId, entityName, stat, isPositive, roundsRemaining |
| `BuffDebuffExpired` | `Action<string, string, BuffDebuffStat, bool, int, int>` | entityId, entityName, stat, isPositive, oldValue, newValue |
| `RegenDrainApplied` | `Action<string, string, RegenDrainStat, bool, int, bool>` | entityId, entityName, stat, isPositive, roundsRemaining, untilRemoved — no oldValue/newValue, since applying an entry doesn't move the resource by itself; see [`regen-and-drain.md`](regen-and-drain.md) |
| `RegenDrainTicked` | `Action<string, string, RegenDrainStat, bool, int>` | entityId, entityName, stat, isPositive, roundsRemaining |
| `RegenDrainExpired` | `Action<string, string, RegenDrainStat, bool>` | entityId, entityName, stat, isPositive |
| `EntityTpChanged` | `Action<string, string, int, int>` | entityId, entityName, oldTp, newTp — also raised by the per-round Hp/Tp regen/drain delta, alongside `EntityDamaged`/`EntityHealed` |
| `EntityMaxHpChanged` | `Action<string, string, int, int>` | entityId, entityName, oldMaxHp, newMaxHp |
| `EntityMaxTpChanged` | `Action<string, string, int, int>` | entityId, entityName, oldMaxTp, newMaxTp |
| `EntityDeath` | `Action<string, string>` | entityId, entityName |
| `EntityRevived` | `Action<string, string, int, int>` | entityId, entityName, oldHp, newHp |

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
- `List<CombatDirectEffect> DirectEffects` — effects applied to each chosen target.
- `List<string> ChosenTargets` — target entity IDs, one per attack instance; publicly gettable, set internally by the engine (via `SubmitTargets` or auto-target expansion).

### `CombatDirectEffect` (`CombatEngine.DataClasses`)

A single effect within a command: `CombatDirectEffectType EffectType`, `ElementType? Element`, `DamageCalcType CalcType`, `double PowerFactor`.

### Enums (`CombatEngine.Enums`)

- `ValidTarget`: `Allies`, `Enemies`, `Both`
- `LivingOrDead`: `Living`, `Dead`, `Both`
- `TargetingType`: `Choose`, `Random`, `All`, `Self`
- `CombatDirectEffectType`: `Damage`, `Heal`
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
