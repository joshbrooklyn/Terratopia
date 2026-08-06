# Regen and Drain

Reference for the timed regen/drain feature: `CombatFunctionParameters.RegensDrains`, the
`RegenDrainSpec`/`RegenDrainType`/`RegenDrainStat` types, and the `CombatEntity` mechanics
underneath them. This mirrors [`buffs-and-debuffs.md`](buffs-and-debuffs.md) closely enough that
it's worth reading that doc first — the two features share almost every structural decision.

## Overview

A combat action can carry zero or more timed regen/drain entries alongside its other effects. Each
entry names a resource (`Hp` or `Tp`), whether it heals/restores or damages/spends that resource,
who it lands on, and how many rounds it lasts. Unlike a buff/debuff, a regen/drain entry doesn't
change anything the moment it's applied — its effect is a fixed percentage of the resource's max,
dealt or restored **at the start of every round** the entry is active, until it expires or an
opposite-polarity application cancels it. The percentage is a global constant
(`GameSettings.RegenDrainHpPct`/`RegenDrainTpPct`, both `0.10` by default) with no elemental
component — it bypasses `CombatMath` entirely, so Defense never mitigates it.

This is a rider on `BasicDamage`/`BasicHeal`, not a combat function of its own — an action authors
`parameters.regensDrains` alongside its `element`/`calcType`/`powerFactor`/`buffsDebuffs`.

## Core types

`src/CombatEngine/Enums/RegenDrainStat.cs`:

```csharp
public enum RegenDrainStat
{
    Hp,
    Tp
}
```

`src/CombatEngine/Enums/RegenDrainType.cs`:

```csharp
public enum RegenDrainType
{
    Positive,
    Negative
}
```

Targeting reuses `BuffDebuffTarget` (`src/CombatEngine/Enums/BuffDebuffTarget.cs`) rather than a
parallel enum — see the target selector catalog in
[`buffs-and-debuffs.md`](buffs-and-debuffs.md#target-selector-catalog), which applies here
unchanged. `CombatRoster.ResolveBuffDebuffTargets` resolves both features' targets through the same
code path.

`src/CombatEngine/DataClasses/RegenDrainSpec.cs` — one authored entry:

```csharp
public class RegenDrainSpec
{
    public required RegenDrainStat   Stat         { get; init; }
    public required RegenDrainType   Type         { get; init; }
    public required BuffDebuffTarget Target       { get; init; }
    public required int              Rounds       { get; init; }
    public required bool             UntilRemoved { get; init; }
    public required bool             CancelOnEntityDeath  { get; init; }
    public required bool             CancelOnApplierDeath { get; init; }
}
```

And on `CombatFunctionParameters`:

```csharp
public IReadOnlyList<RegenDrainSpec>? RegensDrains { get; init; }
```

Every field on `RegenDrainSpec` is `required`, for the same reason `BuffDebuffSpec`'s fields are:
the schema marks `stat`/`type`/`target`/`rounds`/`untilRemoved`/`cancelOnEntityDeath`/
`cancelOnApplierDeath` all required within each array entry, so there's no partially-authored entry
to distinguish from a complete one. Only `RegensDrains` itself stays nullable, so "no regen/drain
authored" is distinguishable from "an empty list."

## No round-based expiration: `UntilRemoved`

Identical semantics to buffs/debuffs: an entry authored with `untilRemoved: true` never expires from
the round clock and stays active — still applying its heal/damage every round — until an
opposite-polarity application on the same resource cancels it.

- **Same-polarity merges are sticky** — a timed entry refreshed by an `UntilRemoved` one of the same
  polarity ends up `UntilRemoved`, and vice versa.
- **`CombatEntity.TickRegensDrains` skips these entries** — no decrement, no `RegenDrainTicked`/
  `RegenDrainExpired` from the round clock. They keep applying every round regardless.

`CombatEventBus.RegenDrainApplied` carries `untilRemoved` for the same reason
`BuffDebuffApplied` does.

## Cancellation on death: `CancelOnEntityDeath` and `CancelOnApplierDeath`

Identical semantics to buffs/debuffs (see
[`buffs-and-debuffs.md`](buffs-and-debuffs.md#cancellation-on-death-cancelonentitydeath-and-cancelonapplierdeath)):
`CancelOnEntityDeath` (default `true`) removes the entry the instant the entity holding it dies,
before `EntityDeath` is raised. `CancelOnApplierDeath` (default `false`) removes the entry the
instant the entity that applied it dies, even when the holder is someone else and stays alive —
handled by `CombatRoster` subscribing to `CombatEventBus.EntityDeath` and calling
`CombatEntity.CancelEffectsAppliedBy` on every other living entity. Both removals raise the existing
`RegenDrainExpired` event with empty `counteredBySourceId`/`counteredBySourceName`, the same shape a
natural tick-expiry already uses. Same-polarity refresh carries the newest values of both flags
forward, exactly like `SourceId`/`SourceName` already do. Note `ApplyRegensDrains` no-ops entirely on
a dead entity, but `TickRegensDrains` does not — an un-cleared (`CancelOnEntityDeath: false`) entry
on a dead entity is inert (nothing ever calls `ApplyRegensDrains` on it again while it stays dead)
but still technically present and tickable, which is what distinguishes it from a cleared one.

## Timing: apply-then-tick at round start

Both families tick from the same hook, `CombatEntity.OnRoundStart()`, called once per living entity
by `CombatEngineClass.FireRoundStartEventsOnEntities` during `CombatFlowState.RoundStart`, before
the round's turn order is built:

```csharp
internal void OnRoundStart()
{
    ApplyRegensDrains();   // heal/damage lands first
    TickRegensDrains();    // then remaining rounds are counted down
    TickBuffDebuffs();
}
```

The heal/damage is applied **before** the duration is decremented. This ordering is deliberate: a
`rounds: 1` Drain must deal its damage exactly once before it expires, not tick to zero and vanish
without ever firing. (This differs from the design note in `Obsidian/status-effects.md`, which
originally described regen/drain — there called "Regen/Poison" — as an end-of-round effect; the
implementation applies at the start of the round, matching where buff/debuff durations already
tick, so both families share one hook and one point in the flow.)

Each round, `ApplyRegensDrains` computes the flat amount directly rather than through `CombatMath`:

```csharp
Hp: amount = (int)Math.Round(MaxHp * CombatBalance.Current.RegenDrainHpPct);
Tp: amount = (int)Math.Round(MaxTp * CombatBalance.Current.RegenDrainTpPct);
```

and routes it through the entity's ordinary mutators — `Heal`/`TakeDamage` for `Hp`,
`RestoreTp`/`SpendTp` for `Tp` — so clamping, death handling, and the usual events all behave
exactly as they would for any other source (see "Reuses existing events" below). The entity passes
itself as the "actor," since the affliction's source, for logging/event purposes, is the afflicted
entity itself. `ApplyRegensDrains` no-ops entirely on a dead entity, and a Drain that kills the
entity partway through its own entries (an entity can hold at most one `Hp` and one `Tp` entry)
stops applying further entries that same round.

An `Hp` Drain can be the killing blow: it routes through `TakeDamage`, so `HandleDefeat` and
`OnDeath` passives (e.g. Living Dead) fire exactly as they would for damage from any other action.

`Tp` regen/drain on a monster (`MaxTp` is always `0` for enemies) computes a zero delta and is a
silent no-op — no `EntityTpChanged` fires.

### `SpendTp`/`RestoreTp`

Adding a `Tp` Drain surfaced a pre-existing gap: `CombatEntity.SpendTp` didn't clamp at 0, and there
was no restore-direction mutator at all. Alongside this feature:

- `SpendTp` now clamps: `Tp = Math.Max(0, Tp - amount)`.
- `RestoreTp(int amount)` is new, mirroring `Heal`: no-ops on a non-positive amount, clamps at
  `MaxTp`, raises `EntityTpChanged`.

## Reuses existing events for the per-round change

Unlike `BuffDebuffApplied`, `RegenDrainApplied` carries no `oldValue`/`newValue` — applying an entry
doesn't move the resource by itself, only `ApplyRegensDrains` does, once per round. The per-round
HP/TP change is reported through the engine's existing `EntityDamaged`/`EntityHealed`/
`EntityTpChanged` events (the same ones any other damage/heal/TP-spend source raises), not a new
dedicated payload — so health bars and the combat log need no new wiring to react to it. Three new
events cover only the status's own lifecycle:

```csharp
public static event Action<string, string, RegenDrainStat, bool, int, bool, string, string>? RegenDrainApplied; // entityId, entityName, stat, isPositive, roundsRemaining, untilRemoved, sourceId, sourceName
public static event Action<string, string, RegenDrainStat, bool, int, string, string>?       RegenDrainTicked;  // entityId, entityName, stat, isPositive, roundsRemaining, sourceId, sourceName
public static event Action<string, string, RegenDrainStat, bool, string, string, string, string>? RegenDrainExpired; // entityId, entityName, stat, isPositive, sourceId, sourceName, counteredBySourceId, counteredBySourceName
```

`RegenDrainExpired` covers both natural expiry and an opposite-polarity cancellation, exactly like
`BuffDebuffExpired`. `sourceId`/`sourceName` always identify the entry that expired; on a countering
cancellation, `counteredBySourceId`/`counteredBySourceName` additionally identify the opposing entry
that cancelled it (empty on a natural expiry) — this is what lets the combat log render "X countered
by Y" instead of a generic "wore off".

## Duplicate and collision handling

Identical two-layer guard to buffs/debuffs:

- **Schema-level** — `parameters.regensDrains` carries `uniqueBy: ["stat", "target"]`, catching two
  entries that authored the exact same `(stat, target)` pair.
- **Runtime** — `CombatFunction.ApplyRegensDrains` tracks every `(resolved entity, stat)` pair across
  an action's entries and throws `InvalidOperationException` naming `ctx.Command.ActionId` the
  moment a second entry would land on a pair it's already touched — catching two *different*
  authored targets that resolve to the same entity (e.g. `Self` and `AllAllies` on a solo actor).

## How it's wired end-to-end

1. **JSON data** — `tech.schema.json`, `item.schema.json`, and `monsteraction.schema.json` each
   expose `parameters.regensDrains` as an array of objects, each requiring `stat`/`type`/`target`/
   `rounds`/`untilRemoved`/`cancelOnEntityDeath`/`cancelOnApplierDeath`.
2. **Data class** — `CombatFunctionParameters.RegensDrains` (`IReadOnlyList<RegenDrainSpec>?`) loads
   straight from that JSON array.
3. **Combat functions** — `BasicDamageFunction` and `BasicHealFunction` resolve their damage/healing
   loop via the shared `CalculateAndApplyDamage(ctx)`/`CalculateAndApplyHealing(ctx)` helpers, then
   call `ApplyBuffsDebuffs(ctx); ApplyRegensDrains(ctx);` once, after.
4. **`CombatFunction.ApplyRegensDrains`** — no-ops if `RegensDrains` is null or empty; otherwise, for
   each entry, resolves its `Target` via `ctx.Roster.ResolveBuffDebuffTargets(ctx.Actor,
   entry.Target, ctx.Targets)`, checks for a duplicate `(entity, stat)` pair, and calls
   `entity.AddRegenDrain(entry.Stat, entry.Type == RegenDrainType.Positive, entry.Rounds,
   entry.UntilRemoved, ctx.Command.SourceId, ctx.Command.SourceName, ctx.Actor.EntityId,
   entry.CancelOnEntityDeath, entry.CancelOnApplierDeath)`.
5. **`CombatEntity.AddRegenDrain`** — `applierId` is `Actor.EntityId`, the same way
   `AddBuffDebuff` closes over it: a resource holds at most one regen/drain; re-applying the same
   polarity extends the duration, the opposite polarity cancels the existing entry outright.
6. **`CombatEntity.HandleDefeat`** — sweeps the entity's own regens/drains for `CancelOnEntityDeath`
   entries before `MarkDead()`/`RaiseEntityDeath`, mirroring the buffs/debuffs sweep.
7. **`CombatRoster`** — subscribed to `CombatEventBus.EntityDeath`, calls
   `CombatEntity.CancelEffectsAppliedBy(deadEntityId)` on every other living entity, removing any
   regen/drain whose `ApplierId` matches and `CancelOnApplierDeath` is set.
8. **`CombatEntity.ProcessRegensDrains`** — `ApplyRegensDrains()` then `TickRegensDrains()`,
   described above.
9. **`CombatEventBus`** — `AddRegenDrain`/`TickRegensDrains`/the two death-cancellation sweeps raise
   `RegenDrainApplied`, `RegenDrainTicked`, `RegenDrainExpired` (death cancellation reuses
   `RegenDrainExpired`'s existing shape with empty `counteredBySourceId`/`Name`).

## Authoring in game data

```json
"parameters": {
  "powerFactor": 1.0,
  "regensDrains": [
    { "stat": "Hp", "type": "Negative", "target": "SelectedTargets", "rounds": 3, "untilRemoved": false, "cancelOnEntityDeath": true, "cancelOnApplierDeath": false }
  ]
}
```

A tech authored this way deals no direct damage of its own (if riding on a `powerFactor: 0`
`BasicDamage`, the "buff-only action" idiom already used by `Howl.json`) but leaves its target
losing 10% of its MaxHp at the start of each of the next 3 rounds.

Like `buffsDebuffs`, the GameData Editor needs no bespoke UI for `regensDrains` — it renders through
the same generic `renderObjectListField`.

Current schema versions: tech 11, item 12, monsteraction 12. `GameSettings.json` is at
schemaVersion 3.

## Test coverage

### `tests/Terratopia.Tests/CombatEngine/PublicInterface/RegenDrainTests.cs`

Mirrors `BuffDebuffTests.cs`'s structure. Covers: the per-round Hp/Tp delta (`MaxHp`/`MaxTp` times
the configured percentage, rounded); a `Tp` regen/drain on a zero-`MaxTp` entity being a silent
no-op; `RestoreTp` clamping at `MaxTp` and the now-clamped `SpendTp` flooring at 0; `AddRegenDrain`'s
non-positive-rounds no-op and its `UntilRemoved` override; same-polarity re-stacking (duration adds,
but the resource still only takes one hit per round); opposite-polarity cancellation before either
side ever applies; `UntilRemoved` merging sticky in both directions and continuing to apply every
round without ticking; cancellation on death (`CancelOnEntityDeath` clearing the entry the moment
its holder dies, proven via `TickRegensDrains` - which unlike `ApplyRegensDrains` never gates on
`IsDead` - still finding nothing left to tick; `CancelOnApplierDeath` clearing an entry on a
*separate* holder once the applier dies via a directly-constructed `CombatRoster`; and same-polarity
refresh carrying the newest flags forward — mirroring `BuffDebuffTests`' equivalent cases); the
apply-before-tick ordering pinned down via a `rounds: 1` Drain that fires exactly once; the normal
tick-then-expire sequence for a multi-round entry; an `Hp` Drain reducing an entity to 0 and raising
`EntityDeath`; `OnRoundStart` no-oping on an already-dead entity; and the end-to-end path from an
authored `regensDrains` entry through both `BasicDamage` and `BasicHeal`, plus the same
collision-throws-naming-the-action guarantee `BuffDebuffTests` has.

### `tests/Terratopia.Tests/CombatEngine/Internal/CombatFunctionRegistryTests.cs`

- **`RegenDrainSpec_MatchesSchemaSuperset`** — the same drift guard as
  `BuffDebuffSpec_MatchesSchemaSuperset`, one level deeper: reflects over `RegenDrainSpec`'s public
  properties and asserts the set matches `parameters.regensDrains.items.properties` in all three
  action schemas.

## See also

- [`buffs-and-debuffs.md`](buffs-and-debuffs.md) — the sibling feature this one mirrors; the target
  selector catalog, the collision-guard rationale, and the `UntilRemoved` rules are shared verbatim.
- [`combat-functions.md`](combat-functions.md) — the `CombatFunctionParameters` reference table
  documents `RegensDrains` inline as well; keep the two in sync.
- [`combat-engine-public-interface.md`](combat-engine-public-interface.md) — the
  `RegenDrainApplied`/`RegenDrainTicked`/`RegenDrainExpired` event signatures.
