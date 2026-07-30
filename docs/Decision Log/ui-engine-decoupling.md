# UI / Engine Decoupling Refactor - June 14, 2026


## Problem

`Battle.cs` and its child nodes (`CombatantCard`, `HpStatDisplay`, `TpStatDisplay`) were receiving and reading from `CombatEntity` — the CombatEngine's internal mutable class. This created tight coupling between the UI and engine internals, and left no clean place to put display-specific data (sprites, sounds) separately from combat data.

## Design

**Entity IDs are the runtime contract.** The UI never holds a `CombatEntity` reference. After combat starts, the only thing shared between the two layers is a `string entityId`.

**`GameEngine` is the orchestrator.** It constructs both the `CombatEntity` list (for `CombatEngine`) and the `CombatantSeed` list (for the UI) from the same JSON source data, in a single call.

**Asset paths live in JSON.** `monsters.json` and `adventurers.json` now include `spritePath` and `hitSoundPath` fields so nothing is hardcoded in C#.

## New Types

### `GameEngine/DataClasses/CombatantSeed.cs`

```csharp
public record CombatantSeed(
    string EntityId, string Name,
    string SpritePath, string HitSoundPath,
    int Hp, int MaxHp, int Tp, int MaxTp,
    int Level, int Power, int Defense, int Speed,
    float Evasion, float CritChance);

public record CombatStartData(
    IReadOnlyList<CombatantSeed> Allies,
    IReadOnlyList<CombatantSeed> Enemies);
```

`CombatantSeed` is a one-time handoff record — used to build UI cards at combat start, then discarded. All subsequent stat updates come through events.

## Changed Files

### `GameEngine/DataClasses/Adventurer.cs` and `Monster.cs`
Added `string SpritePath` and `string HitSoundPath` properties.

### `GameData/Adventurers/adventurers.json` and `GameData/Monsters/monsters.json`
Added `spritePath` and `hitSoundPath` fields (empty strings for now, to be filled when assets exist).

### `GameEngine/Engine/GameEngineClass.cs`
`StartSkirmishCombat()` now returns `CombatStartData` instead of `void`. It builds parallel `CombatantSeed` lists alongside the `CombatEntity` lists from the same source data, calls `CombatEngineClass.StartCombat()` as before, then returns the seeds.

### `CombatEngine/Engine/CombatEventBus.cs`
All events that previously passed `CombatEntity` now pass IDs and primitives:

| Event | Old signature | New signature |
|---|---|---|
| `TurnStarted` | `Action<CombatEntity>` | `Action<string>` — entityId |
| `TurnEnded` | `Action<CombatEntity>` | `Action<string>` — entityId |
| `WaitingForPlayerAction` | `Action<CombatEntity>` | `Action<string, int>` — entityId, currentTp |
| `EntityHpChanged` | `Action<CombatEntity, int, int>` | `Action<string, int, int, int>` — entityId, oldHp, newHp, maxHp |
| `EntityTpChanged` | `Action<CombatEntity, int, int>` | `Action<string, int, int, int>` — entityId, oldTp, newTp, maxTp |
| `EntityHealed` | `Action<CombatEntity, int, CombatEntity>` | `Action<string, int, string>` — targetId, amount, sourceId |

### `CombatEngine/Engine/CombatFlowMachine.cs`
Two `Raise*` calls updated to pass `_currentEntity.EntityId` and `_currentEntity.Tp` instead of the entity object.

### `Game/Scenes/MainMenu.cs`
`GoToBattle()` no longer calls `StartSkirmishCombat()` — it just changes scene. `Battle._Ready()` now owns combat startup.

### `Game/Scenes/Battle.cs`
- `_Ready()` calls `GameEngineClass.Instance.StartSkirmishCombat()` and passes the result to `BuildCombatantCards()`
- All event handlers updated to match new signatures
- `PopulateAndShowModal` takes `(string entityId, int currentTp)` — no engine query needed for TP

### `Game/Scenes/CombatantCard.cs`
`Initialize(CombatEntity, bool)` → `Initialize(CombatantSeed, bool)`

### `Game/Scenes/HpStatDisplay.cs` and `TpStatDisplay.cs`
Event handler signatures updated to `(string entityId, int oldVal, int newVal, int maxVal)`. No longer import `CombatEngine.DataClasses`.
