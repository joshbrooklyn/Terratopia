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

```csharp
public void Reset()
```
Clears all rosters and combat state and resets `CombatEventBus`. Leaves the instance ready for a fresh `InitCombat` call.

### Submitting a turn's command

```csharp
public void SubmitCommand(CombatCommand cmd)
```
Gives the flow machine the command chosen for the current turn's actor, ally or enemy alike.

```csharp
public void SubmitTargets(List<string> chosenTargetIds)
```
Supplies target entity IDs once the flow machine has requested target selection for the pending command (ally turns only — enemy turns always get a single random target assigned automatically).

### Queries

```csharp
public IReadOnlyList<CombatEntity> GetLivingEntities()
public IReadOnlyList<CombatEntity> GetLivingAllies()
public IReadOnlyList<CombatEntity> GetLivingEnemies()
```
Snapshots (new lists) of entities with `IsDead == false`, filtered to all combatants, allies only, or enemies only.

```csharp
public IReadOnlyList<CombatEntity> GetValidTargets(CombatCommand cmd)
```
Resolves the legal target pool for a command: side (`cmd.ValidTargets`, relative to whether the actor is a player-side entity) crossed with living/dead state (`cmd.LivingOrDead`).

### Consuming engine output

The engine does not return results from actions — it reports everything through `CombatEventBus` (see below). Callers should subscribe to the relevant events after `InitCombat` and before `BeginCombat`.

## Supporting public types

### `CombatEventBus` (`CombatEngine`)

Static event bus; the engine's only channel for reporting what happened. All events use IDs/names/primitives, never live `CombatEntity` references. `CombatEventBus.Reset()` clears all subscribers and is called automatically by `InitCombat` and `Reset`.

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
| `ActionResolved` | `Action<CombatCommand, string>` | command, actorName |
| `EntityDamaged` | `Action<string, string, int, string, string, bool>` | targetId, targetName, amount, sourceId, sourceName, isCriticalHit |
| `EntityHealed` | `Action<string, string, int, string, string>` | targetId, targetName, amount, sourceId, sourceName |
| `AttackEvaded` | `Action<string, string, string, string>` | attackerId, attackerName, targetId, targetName |
| `EntityHpChanged` | `Action<string, string, int, int>` | entityId, entityName, oldHp, newHp |
| `EntityTpChanged` | `Action<string, string, int, int>` | entityId, entityName, oldTp, newTp |
| `EntityMaxHpChanged` | `Action<string, string, int, int>` | entityId, entityName, oldMaxHp, newMaxHp |
| `EntityMaxTpChanged` | `Action<string, string, int, int>` | entityId, entityName, oldMaxTp, newMaxTp |
| `EntityDeath` | `Action<string, string>` | entityId, entityName |

### `CombatEntity` (`CombatEngine.DataClasses`)

Mutable combat participant passed into `InitCombat`. Public constructor takes the full stat block (`entityId, name, level, maxHp, hp, maxTp, tp, power, defense, speed, evasion, critChance, critModifier`). All properties are publicly readable but only internally settable — callers construct instances and read state, the engine mutates them during combat.

Key properties: `EntityId`, `Name`, `Level`, `MaxHp`, `Hp`, `MaxTp`/`Tp` (0 for enemies), `Power`, `Defense`, `Speed`, `Evasion`, `CritChance`, `CritModifier`, `IsDead`.

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
- `DamageCalcType`: `StandardFormula`
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
