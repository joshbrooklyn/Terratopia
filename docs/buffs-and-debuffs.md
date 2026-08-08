# Buffs and Debuffs

Reference for the timed stat buff/debuff feature: `CombatFunctionParameters.BuffsDebuffs`, the
`BuffDebuffSpec`/`BuffDebuffType`/`BuffDebuffTarget` types, and the `CombatEntity` stat-modifier
mechanics underneath them.

## Overview

A combat action can carry zero or more timed stat buffs/debuffs alongside
its other effects. Each entry is independent: it names a stat, whether it raises or lowers that
stat, who it lands on, and how many rounds it lasts. Entries are resolved and applied once, after
the action has fully finished its own damage/healing — not folded into the per-hit loop — and every
entry lands regardless of whether the action's own attacks connected or missed.

This is a rider on `BasicDamage`/`BasicHeal`, not a combat function of its own — an action authors
`parameters.buffsDebuffs` alongside its `element`/`calcType`/`powerFactor`, the same way it always
has.

## Core types

`src/CombatEngine/Enums/BuffDebuffStat.cs` 

```csharp
public enum BuffDebuffStat
{
    Power,
    Defense,
    Speed
}
```

`src/CombatEngine/Enums/BuffDebuffType.cs`:

```csharp
public enum BuffDebuffType
{
    Positive,
    Negative
}
```

`src/CombatEngine/Enums/BuffDebuffTarget.cs` — who an entry lands on (see the catalog below):

```csharp
public enum BuffDebuffTarget
{
    SelectedTargets,
    Self,
    RandomAlly,
    RandomEnemy,
    AllAllies,
    AllEnemies
}
```

`src/CombatEngine/DataClasses/BuffDebuffSpec.cs` — one authored entry:

```csharp
public class BuffDebuffSpec
{
    public required BuffDebuffStat   Stat         { get; init; }
    public required BuffDebuffType   Type         { get; init; }
    public required BuffDebuffTarget Target       { get; init; }
    public required int              Rounds       { get; init; }
    public required bool             UntilRemoved { get; init; }
    public required bool             CancelOnEntityDeath  { get; init; }
    public required bool             CancelOnApplierDeath { get; init; }
}
```

And on `CombatFunctionParameters`:

```csharp
public IReadOnlyList<BuffDebuffSpec>? BuffsDebuffs { get; init; }
```

Every field on `BuffDebuffSpec` is a mandatory C# `required` property, unlike the rest of
`CombatFunctionParameters`, which makes everything nullable so "omitted" can be told apart from
"authored as the default." That distinction doesn't apply inside a `BuffDebuffSpec`: the JSON
Schema marks `stat`/`type`/`target`/`rounds`/`untilRemoved`/`cancelOnEntityDeath`/`cancelOnApplierDeath`
all `required` within each array entry, so there's no partially-authored entry to distinguish from a
complete one, and no cross-field pairing to validate at combat time. Only `BuffsDebuffs` itself — the
list — stays nullable, so "this action carries no buffs/debuffs at all" is still distinguishable from
"an empty list."

## No round-based expiration: `UntilRemoved`

An entry authored with `untilRemoved: true` never expires from the round clock — `rounds` is still
present (schema-required) but ignored at runtime. It stays active until an opposite-polarity
application lands on the same stat and cancels it (see "Duplicate and collision handling" below);
nothing else removes it.

Two rules govern how it interacts with the rest of the mechanic:

- **Same-polarity merges are sticky.** When a new application refreshes an existing entry of the
  same polarity, the merged result is `UntilRemoved` if *either side* was — a timed buff refreshing
  an indefinite one doesn't make it finite, and an indefinite buff refreshing a timed one doesn't
  shorten it back down. Only when both sides are timed does the usual "durations add, magnitude
  doesn't" rule (see "Re-stacking" below) apply.
- **`CombatEntity.TickBuffDebuffs` skips these entries entirely** — no decrement, and neither
  `BuffDebuffTicked` nor `BuffDebuffExpired` fires for them from the round clock. Cancellation is
  the only path that removes one, and it already raises `BuffDebuffExpired` exactly as it does for a
  timed entry.

`CombatEventBus.BuffDebuffApplied` carries the new `untilRemoved` flag so a listener (e.g. a HUD)
knows up front whether to show a countdown or an indefinite indicator, without waiting to see
whether a `BuffDebuffTicked` ever arrives.

## Cancellation on death: `CancelOnEntityDeath` and `CancelOnApplierDeath`

Two more independent booleans govern whether an entry survives a death, on either end of the
buff/debuff relationship:

- **`CancelOnEntityDeath`** (default `true` in the GameData Editor) — when true, the entry is
  removed the instant the entity *holding* it dies, before `EntityDeath` is raised. When false, it
  is simply left in place, un-cleared — without this flag, a dead entity keeps a stale entry sitting
  inert in `CombatEntity._buffsDebuffs` forever, since nothing else ever purges it. Note that
  `CombatEntity.Revive` does not itself clear `IsDead`: its only caller today, `LivingDeadTriggeredEffect`,
  runs *before* `MarkDead()` inside `HandleDefeat`, preventing death from ever being marked in the
  first place, rather than reversing it after the fact — so `CancelOnEntityDeath` is not a "does
  this survive a resurrection" flag for anything in the engine today, just "does this get cleaned up
  once the entity is actually dead."
- **`CancelOnApplierDeath`** (default `false`) — when true, the entry is removed the instant the
  entity that *applied* it dies, even though the entity holding it is someone else and stays alive.
  When false, the entry outlives its applier, same as today.

Both removals raise the same `BuffDebuffExpired` event a natural tick-expiry does, with empty
`counteredBySourceId`/`counteredBySourceName` — a death is not an opposite-polarity countering, so
there's no "countered by" to report, the same shape `TickBuffDebuffs` already uses for natural
expiry.

`CancelOnApplierDeath` only ever affects *other* entities. A self-applied entry's fate on the
applier's own death is governed entirely by `CancelOnEntityDeath`: the applier-death broadcast
(`CombatRoster.OnEntityDeath`, subscribed to `CombatEventBus.EntityDeath`) only reaches entities
still alive at the moment it fires via `CombatRoster.GetLivingEntities()`, and the entity that just
died has already been excluded from that set — `CombatEntity.HandleDefeat` calls `MarkDead()`
before raising `EntityDeath`.

On refresh, same-polarity re-application carries the newest values of both flags forward, exactly
like `SourceId`/`SourceName` already do — "the newest application... wins on refresh" applies here
too.

## Target selector catalog

Resolved by `CombatRoster.ResolveBuffDebuffTargets` (`src/CombatEngine/Engine/CombatRoster.cs`),
relative to the acting entity — not to the player. A monster's `AllAllies` reaches the other
monsters, not the player's party.

| Value | Resolves to | Notes |
|---|---|---|
| `SelectedTargets` | The action's own chosen targets (`ctx.Targets`) | Filtered to the living and de-duplicated by entity id — a multi-hit action that strikes the same target twice still only lands the buff once. |
| `Self` | The actor | Always exactly one entity. |
| `RandomAlly` | One random living ally of the actor | **Excludes the actor** — use `Self` for that. Resolves to nobody if the actor has no other living ally. |
| `RandomEnemy` | One random living enemy of the actor | Resolves to nobody if the actor has no living enemies. |
| `AllAllies` | Every living entity on the actor's own side, actor included | |
| `AllEnemies` | Every living entity on the opposing side | |

`RandomAlly` and `RandomEnemy` draw from the engine's shared `Random` instance
(`CombatFunctionContext.Rng`), the same source every other roll in combat uses. Authoring one on an
action shifts the draw sequence for everything resolved after it in that fight — the same
draw-order contract `CombatRoster`'s auto-targeting and AI-targeting paths already have.

## Timing and evasion semantics

Two rules govern when and how entries apply:

- **Every entry applies once, after the whole action resolves** — after every hit, all damage, and
  all healing, never interleaved with the action's own per-hit loop. Concretely: a 3-hit action
  carrying a Defense debuff on its own target never gets to lower Defense partway through and let
  its later hits benefit from the lower number — all hits use the target's Defense as it stood at
  the start of the action, and the debuff lands only once the action is otherwise finished. One
  rule, regardless of which `BuffDebuffTarget` an entry uses.
- **Buffs apply regardless of evasion**, including an action whose every attack missed. A
  buff/debuff entry is a property of the *action*, not of any individual hit — so `Self`, `AllAllies`,
  and the other non-`SelectedTargets` selectors clearly shouldn't care whether the action's own
  attack connected, and `SelectedTargets` follows the same rule for consistency rather than being a
  special case.
- **Every selector considers living entities only.** If the action's own damage kills a target
  before the buff step runs, that target has already dropped out of `SelectedTargets` by the time
  entries are resolved.

## Duplicate and collision handling

Two entries that both move the same stat on the same entity are a data-authoring error, and it's
caught in two places that catch two different shapes of mistake:

- **Schema-level** — `parameters.buffsDebuffs` carries a custom `uniqueBy: ["stat", "target"]`
  keyword (draft-07 JSON Schema has no built-in way to express "unique by property"). It's enforced
  by both of the GameData Editor's validators — the host-side `schemaValidation.ts` and the
  webview's client-side `main.js` — and catches two entries that authored the exact same `(stat,
  target)` pair, e.g. two `{ stat: "Power", target: "Self" }` entries on one action.
- **Runtime** — `CombatFunction.ApplyBuffsDebuffs` (`src/CombatEngine/CombatFunctions/CombatFunction.cs`)
  tracks every `(resolved entity, stat)` pair it applies across all of an action's entries, and
  throws `InvalidOperationException` naming `ctx.Command.ActionId` the moment a second entry would
  land on a pair it's already touched. This is what catches the case the schema *can't* see: two
  entries with *different* authored targets that happen to resolve to the same entity — e.g. `Self`
  and `AllAllies` both moving `Power` on an actor fighting alone, where `AllAllies` (living
  entities on the actor's side, actor included) resolves right back to the same entity `Self` did.

## How it's wired end-to-end

1. **JSON data** — `tech.schema.json`, `item.schema.json`, and `monsteraction.schema.json` each
   expose `parameters.buffsDebuffs` as an array of objects, each requiring `stat`/`type`/`target`/
   `rounds`/`untilRemoved`/`cancelOnEntityDeath`/`cancelOnApplierDeath`.
2. **Data class** — `CombatFunctionParameters.BuffsDebuffs` (`IReadOnlyList<BuffDebuffSpec>?`)
   loads straight from that JSON array.
3. **Combat functions** — `BasicDamageFunction` and `BasicHealFunction` resolve their damage/healing
   loop via the shared `CalculateAndApplyDamage(ctx)`/`CalculateAndApplyHealing(ctx)` helpers (also
   defined on `CombatFunction`), then call the shared `ApplyBuffsDebuffs(ctx)` helper once, after.
4. **`CombatFunction.ApplyBuffsDebuffs`** — no-ops if `BuffsDebuffs` is null or empty; otherwise,
   for each entry, resolves its `Target` via `ctx.Roster.ResolveBuffDebuffTargets(ctx.Actor,
   entry.Target, ctx.Targets)`, checks for a duplicate `(entity, stat)` pair (see above), and calls
   `entity.AddBuffDebuff(entry.Stat, entry.Type == BuffDebuffType.Positive, entry.Rounds,
   entry.UntilRemoved, ctx.Command.SourceId, ctx.Command.SourceName, ctx.Actor.EntityId,
   entry.CancelOnEntityDeath, entry.CancelOnApplierDeath)` for each resolved entity.
5. **`CombatRoster.ResolveBuffDebuffTargets`** — reached through the `Roster` field on
   `CombatFunctionContext` (see the catalog above).
6. **`CombatEntity.AddBuffDebuff`** — `applierId` is `Actor.EntityId` (invariant for the whole
   action, like `Command.SourceId`/`SourceName` already are — not threaded as a per-entry
   parameter): a stat holds at most one buff/debuff; re-applying the same polarity extends the
   duration without compounding the magnitude (unless either side is `UntilRemoved`, see above),
   and the opposite polarity cancels the existing entry outright.
7. **`CombatEntity.HandleDefeat`** — after the triggered-effect prevention check and before
   `MarkDead()`/`RaiseEntityDeath`, sweeps the entity's own buffs/debuffs for `CancelOnEntityDeath`
   entries and removes them.
8. **`CombatRoster`** — subscribes to `CombatEventBus.EntityDeath` in its constructor; on any
   entity's death, calls `CombatEntity.CancelEffectsAppliedBy(deadEntityId)` on every other living
   entity, which removes any entry whose `ApplierId` matches and `CancelOnApplierDeath` is set.
9. **`CombatEventBus`** — `AddBuffDebuff`/`TickBuffDebuffs`/the two death-cancellation sweeps raise
   `BuffDebuffApplied`, `BuffDebuffTicked`, and `BuffDebuffExpired` (see
   [`combat-engine-public-interface.md`](combat-engine-public-interface.md)). A
   `BuffDebuffType.Positive`/`Negative` entry surfaces on these events as a plain `bool isPositive`,
   since the enum is purely an authoring-format concern, converted at the `CombatFunction` boundary.
   `BuffDebuffApplied` additionally carries `untilRemoved`; `BuffDebuffTicked`/`BuffDebuffExpired`
   are unchanged since ticking never fires for an `UntilRemoved` entry in the first place, and a
   death-cancellation removal reuses `BuffDebuffExpired`'s existing shape with empty
   `counteredBySourceId`/`Name`.

## Authoring in game data

```json
"parameters": {
  "powerFactor": 1.0,
  "buffsDebuffs": [
    { "stat": "Defense", "type": "Negative", "target": "SelectedTargets", "rounds": 2, "untilRemoved": false, "cancelOnEntityDeath": true, "cancelOnApplierDeath": false },
    { "stat": "Power",   "type": "Positive", "target": "Self",            "rounds": 3, "untilRemoved": false, "cancelOnEntityDeath": true, "cancelOnApplierDeath": false },
    { "stat": "Speed",   "type": "Negative", "target": "SelectedTargets", "rounds": 1, "untilRemoved": true,  "cancelOnEntityDeath": true, "cancelOnApplierDeath": false }
  ]
}
```

This example is a tech that debuffs Defense on whatever it hits for 2 rounds, buffs the caster's
own Power for 3 rounds, and also saddles its target with a Speed debuff that never wears off on its
own — `rounds` is authored (schema-required) but ignored at runtime since `untilRemoved` is `true`.

The GameData Editor's form view needs no bespoke UI for this: it's fully schema-driven, and
`parameters.buffsDebuffs` — an array of objects — already renders through the editor's generic
`renderObjectListField` (`src/GameDataEditor/media/main.js`), giving an "Add entry"/"Remove" list
where `stat`, `type`, `target`, `rounds`, and `untilRemoved` are each editable per entry.

Current schema versions: tech 11, item 12, monsteraction 12.

## Test coverage

### `tests/Terratopia.Tests/CombatEngine/Internal/BuffDebuffTargetResolutionTests.cs`

These tests exercise `CombatRoster.ResolveBuffDebuffTargets` directly — constructing a
`CombatRoster` by hand rather than running a full fight — so each `BuffDebuffTarget` selector's
resolution logic is checked in isolation from the once-per-action bookkeeping `CombatFunction`
layers on top.

- **`SelectedTargets_ReturnsTheGivenTargets_LivingOnly_Deduplicated`** — feeds the resolver a
  targets list containing one live entity twice and one dead entity once. The result is that one
  live entity, exactly once, proving `SelectedTargets` both drops the dead and collapses the
  repeats a multi-hit action produces.
- **`Self_ReturnsTheActor`** — resolving `Self` returns a single-element list containing exactly the
  actor that was passed in.
- **`AllAllies_ReturnsEveryLivingEntityOnTheActorsSide_ActorIncluded`** — builds a roster where the
  *enemy* side has three entities: the "actor" for this test, another living entity, and a third
  that's been killed off first. Resolving `AllAllies` from that enemy-side actor returns the two
  living enemies (actor included) and excludes both the dead one and the player's ally entirely —
  proving `AllAllies` is scoped to the actor's own side, not to "the player's side," and that dead
  entities never qualify.
- **`AllEnemies_ReturnsEveryLivingEntityOnTheOpposingSide`** — a straightforward mirror: an ally
  actor resolving `AllEnemies` gets back both living enemies.
- **`RandomAlly_ExcludesTheActor`** — with the actor and exactly one other living ally in the
  roster, `RandomAlly` is resolved 20 times, each with a differently-seeded `Random`. Every single
  draw returns the *other* ally, never the actor — since there's only one possible non-actor
  candidate, any leak of the actor into the pool would show up as a flaky failure across those 20
  seeds, so this pins the exclusion down robustly rather than by chance.
- **`RandomAlly_WithNoOtherLivingAlly_ResolvesToEmpty`** — a solo actor (no other ally in the
  roster) resolving `RandomAlly` gets back an empty list rather than an error or a self-target.
- **`RandomEnemy_ResolvesToOneOfTheLivingEnemies`** — with two living enemies, resolving
  `RandomEnemy` returns exactly one entity, and that entity is one of the two enemies (not
  necessarily deterministic which one — the test only pins down the shape: exactly one, from the
  valid pool).
- **`RandomEnemy_WithNoLivingEnemies_ResolvesToEmpty`** — the actor's one enemy is killed first,
  then `RandomEnemy` is resolved; the result is an empty list rather than an exception.

### `tests/Terratopia.Tests/CombatEngine/PublicInterface/BuffDebuffTests.cs`

This file covers both the underlying `CombatEntity` stat-modifier mechanics that buffs/debuffs are
built on and the full integration — authoring `buffsDebuffs` in a `CombatCommand` and running it
through the real engine.

**The stat getters**

- **`Buff_RaisesStat_AndDebuff_LowersIt_ByTimedBuffPct`** *(runs once per stat: Power, Defense,
  Speed)* — starts two entities with every stat at 20. One gets a positive buff on the stat under
  test, the other a negative one. `20 * 1.35 = 27` for the buff and `20 * 0.65 = 13` for the debuff
  (neither a rounding midpoint, so the expected numbers are unambiguous). All three stats are
  asserted on both entities every time, which is what proves the modifier is scoped to just the
  targeted stat and never leaks onto the other two.
- **`Buff_RaisesBuffDebuffApplied_WithBeforeAndAfterValues`** — applies a single Power buff to a
  Power=20 entity and checks the `BuffDebuffApplied` event's payload directly: it must report the
  stat, that it's positive, the round count, and the before/after values (20 → 27) — everything a
  HUD would need without having to separately query the entity back.
- **`AddBuffDebuff_WithNonPositiveRounds_IsIgnored`** — applying a buff with `roundsRemaining: 0` is
  a silent no-op: the stat is untouched and no `BuffDebuffApplied` event fires at all, the same
  no-op treatment `SpendTp` and `Heal` give their own non-positive inputs elsewhere in the engine.
- **`AddBuffDebuff_WithUntilRemovedTrue_AppliesEvenWithZeroRounds`** — the same `roundsRemaining: 0`
  input that's a no-op above *does* apply when `untilRemoved: true`, since `rounds` is irrelevant to
  an indefinite entry — proving the no-op guard is specifically about non-positive *timed* rounds,
  not `rounds` in general.

**Re-stacking**

- **`SamePolarity_RefreshesDuration_WithoutCompoundingMagnitude`** — applies a +Power buff of 2
  rounds, then another +Power buff of 3 rounds, to the same entity. The reported duration becomes 5
  (the rounds add), but the stat still reads 27 — the single-buff value — not `20 * 1.35²`. A stat
  can only ever hold one buff's worth of magnitude; stacking the same polarity only extends how long
  it lasts.
- **`OppositePolarity_CancelsTheExistingEntry_AndRaisesExpired`** — a +Power buff (2 rounds)
  followed by a −Power debuff (9 rounds) on the same entity leaves Power back at its base value of
  20 — neither side wins or averages out, they annihilate each other. Exactly one
  `BuffDebuffApplied` fires (for the first buff only) and the event raised for the cancellation is
  `BuffDebuffExpired`, reporting the polarity of the entry that was *removed* (the original `true`),
  not the incoming debuff — never a second `BuffDebuffApplied`. `sourceId`/`sourceName` on that event
  identify the buff that wore off; `counteredBySourceId`/`counteredBySourceName` identify the debuff
  that cancelled it — see `SourceThreadingTests.BuffDebuffExpired_OnOppositePolarityCancellation_...`.
- **`UntilRemoved_IsCancelledByOppositePolarity`** — the same cancellation, but the existing entry is
  `UntilRemoved`. It's removed exactly like a timed entry would be, proving cancellation doesn't
  special-case duration at all.
- **`SamePolarity_Merge_UntilRemovedIsSticky`** *(two cases)* — a timed buff refreshed by an
  `UntilRemoved` one of the same polarity ends up `UntilRemoved`; an `UntilRemoved` buff refreshed by
  a timed one *stays* `UntilRemoved` rather than being shortened back down to the incoming rounds.
  Either side being indefinite wins the merge, regardless of application order.

**Cancellation on death**

- **`CancelOnEntityDeath_True_ClearsTheEntryTheMomentItsHolderDies`** /
  **`CancelOnEntityDeath_False_LeavesTheEntryInPlaceOnDeath`** — a buff's holder dies via
  `TakeDamage`; the `true` case raises `BuffDebuffExpired` with empty `counteredBy` fields and the
  stat getter (which never special-cases `IsDead`) reads base immediately, while the `false` case
  raises nothing and the getter still reflects the buff.
- **`CancelOnApplierDeath_True_ClearsOtherEntitysEntry_WhenApplierDies`** /
  **`CancelOnApplierDeath_False_LeavesOtherEntitysEntry_WhenApplierDies`** — a bare `CombatRoster` is
  constructed directly (same technique as `BuffDebuffTargetResolutionTests`) over an applier and a
  separate holder entity; killing the applier clears the holder's `CancelOnApplierDeath: true` entry
  through `CombatRoster`'s `EntityDeath` subscription, but leaves a `false` one in place.
- **`SamePolarityRefresh_CarriesForwardTheNewestCancelFlags`** — an entry applied with both flags
  `false`, then refreshed with both `true`, behaves as if authored `true` — proving the flags follow
  the same "newest application wins" rule as `SourceId`/`SourceName`.
- **`SourceThreadingTests.CancelOnApplierDeath_ThreadsTheActorsEntityId_NotTheCommandsSourceId`** —
  an end-to-end test through the real engine: an actor's `SourceId` is deliberately authored as a
  string matching no entity's `EntityId`, so a buff it casts on another entity can only be cleared on
  the actor's death if `CancelOnApplierDeath` is keyed off `CombatCommand.ActorId` (via
  `ctx.Actor.EntityId`), not `SourceId`/`SourceName`.

**Ticking down**

- **`TickBuffDebuffs_CountsDownThenExpires_RestoringTheBaseStat`** — a +Power buff with 2 rounds
  remaining is ticked three times. The first tick raises `BuffDebuffTicked` with 1 round left and
  Power stays at 27; the second tick raises `BuffDebuffExpired` (27 → 20) and Power returns to base;
  the third tick raises nothing further, proving the entry is genuinely gone rather than sitting at
  a non-positive count waiting to expire again.
- **`UntilRemoved_NeverTicksOrExpires_AcrossManyRounds`** — an `UntilRemoved` +Power buff is ticked
  several times in a row. The stat never moves off 27 and neither `BuffDebuffTicked` nor
  `BuffDebuffExpired` fires at any point — the round clock has nothing to do with these entries.

**Reaching it from game data, through a `CombatFunction`**

All of these run a real two-entity fight (one ally, one durable enemy) through
`CombatEngineClass.BeginCombat()`, with the ally's opening move authoring the `buffsDebuffs` under
test and a scripted follow-up move finishing the enemy off so the fight terminates.

- **`BasicDamage_DefenseDebuff_AppliesOncePerTarget`** — a 3-hit `BasicDamage` action (with
  `AllowMultipleAttackOnSameTarget`) strikes the one enemy three times while carrying a Defense
  debuff on `SelectedTargets`. Every single hit deals the same 28 damage — proving the debuff, which
  only applies after the whole action resolves, never gets a chance to lower Defense partway
  through and affect the action's own later hits. Exactly one `BuffDebuffApplied` event fires (not
  three), confirming the debuff lands once per target no matter how many times the action struck
  it — without that de-duplication, its duration would have been tripled to 6 rounds instead of the
  authored 2.
- **`BasicHeal_CanCarryABuff_ToItsTarget`** — proves the rider works on healing too, not just
  damage: the ally heals itself while carrying a `SelectedTargets` Power buff, and its Power moves
  from 20 to 27 by the time the action resolves. The fight wraps up two rounds later, so the buff
  (authored for 2 rounds) has ticked down to 1 remaining round and is confirmed still active at the
  end.
- **`MultipleEntries_EachApply_ToTheirOwnTarget`** — a single `BasicDamage` hit carries *two*
  entries at once: a Defense debuff on `SelectedTargets` (the enemy it's hitting) and a Power buff
  on `Self` (the caster). Both `BuffDebuffApplied` events are captured, and both show up — the
  enemy's Defense entry and the ally's Power entry — demonstrating an action can carry more than one
  independently-targeted buff/debuff in one move.
- **`SelfTarget_Buff_AppliesEvenWhenTheActionsOwnHitIsFullyEvaded`** — sets the enemy's evasion to
  exactly 1.0, which guarantees the very first attack roll against it evades (a roll is always
  strictly less than 1.0, so it can never meet-or-beat an evasion of 1.0). The ally's opening
  `BasicDamage` move, carrying a `Self` Power buff, is guaranteed to fully miss. The test confirms
  no damage landed on the enemy from that first action, yet the Power buff still applied to the
  ally — proving a buff/debuff entry is tied to the action, not to whether any individual attack
  actually connected.
- **`RandomAlly_WithNoOtherLivingAlly_IsASilentNoOp`** — the ally fights entirely alone. Its opening
  move authors a `RandomAlly` Power buff; since there's no other living ally to draw, and
  `RandomAlly` deliberately excludes the actor itself, no `BuffDebuffApplied` event for Power fires
  at any point in the fight — the entry quietly resolves to nobody rather than erroring or
  defaulting onto the caster.
- **`CollidingBuffDebuffEntries_Throw_NamingTheAction`** — authors two entries on one action: a
  Power buff targeting `Self` and a Power buff targeting `AllAllies`, on an ally fighting alone. For
  a solo actor, `AllAllies` (every living entity on the actor's side, actor included) resolves to
  that exact same entity `Self` already pointed at — so both entries try to move the same entity's
  Power. Resolving the action throws `InvalidOperationException`, and the message names the
  action's id (`"broken_tech"`), so a bad tech's failure points a developer straight at the file
  that authored it rather than at the engine internals.

### `tests/Terratopia.Tests/CombatEngine/Internal/CombatFunctionRegistryTests.cs`

- **`BuffDebuffSpec_MatchesSchemaSuperset`** — this is a drift guard, not a behavioral test. It
  reflects over `BuffDebuffSpec`'s public properties, camelCases each name the way the engine's JSON
  deserializer does, and asserts that set is exactly equal to the properties declared under
  `parameters.buffsDebuffs.items.properties` in each of the three action schemas (tech, item,
  monsteraction). Those schemas are hand-maintained to mirror the C# type — `additionalProperties:
  false` means a field present in C# but missing from the schema would be silently unauthorable in
  game data, while a field in the schema with no matching C# property would be silently discarded on
  load — so this test exists purely to catch that mirror ever falling out of sync. It's the same
  guarantee `CombatFunctionParameters_MatchesSchemaSuperset` already provides for the top-level
  `parameters` block, applied one level deeper to the array's item shape.

## See also

- [`combat-functions.md`](combat-functions.md) — the `CombatFunctionParameters` reference table
  documents `BuffsDebuffs` inline as well; keep the two in sync.
- [`combat-engine-public-interface.md`](combat-engine-public-interface.md) — the
  `BuffDebuffApplied`/`BuffDebuffTicked`/`BuffDebuffExpired` event signatures, which this feature
  doesn't change.
- [`regen-and-drain.md`](regen-and-drain.md) — the sibling feature that mirrors nearly every
  structural decision here (target selectors, `UntilRemoved`, collision handling), applied to Hp/Tp
  percentages instead of stat modifiers.
