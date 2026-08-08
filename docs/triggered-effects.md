# Triggered Effects

Reference for the `TriggeredEffects` system in `CombatEngine.TriggeredEffects`.

## Overview

A **triggered effect** is a named ability attached to a combat entity that reacts to a combat *event* — currently, only that entity dying. Triggered effects are data-driven by name only: the JSON data references a triggered effect by string, and all of its actual behavior is hardcoded in a C# class. An entity can come to own a triggered effect two ways: at combat setup, via `Monster.TriggeredEffects`, or mid-combat, via a Tech/Item/MonsterAction's `triggeredEffectsApplied` rider (see "How it's wired end-to-end" below).

This is a different mechanism from **[Keywords](keywords.md)**: triggered effects live on *entities* and react to combat events, while keywords live on *actions* and react to how/when that specific action is used. See `keywords.md` for that system.

> The original design notes (`Obsidian/passive-effects.md`) describe a much broader long-term vision for "passive effects" — stat modifiers, elemental resistance changes, run/dungeon-wide modifiers, perks, gear, curses & boons, and more. The code described on this page only implements one narrow slice of that vision: on-death triggers. Don't mistake this doc for that larger design — it documents what's actually built today.

## Core types

### `TriggeredEffect` (abstract base)

```csharp
public abstract class TriggeredEffect
{
    public abstract string Name { get; }

    // Returns (deathPrevented, reviveHp). deathPrevented tells the engine whether to keep this
    // entity alive; when true, reviveHp is the HP the engine will set the entity to.
    public virtual (bool, int) OnBeforeDeath(CombatEntity target)
    {
        return (false, 0);
    }

    // Convenience wrapper around TriggeredEffectTracker.Get so a subclass doesn't have to know the
    // tracker is keyed by (Name, entity.EntityId).
    public virtual int TotalApplications(CombatEntity entity)
    {
        return TriggeredEffectTracker.Get(Name, entity.EntityId).TotalApplications;
    }

    public virtual int ApplicationsThisRound(CombatEntity entity)
    {
        return TriggeredEffectTracker.Get(Name, entity.EntityId).ApplicationsThisRound;
    }

    public virtual void RemoveFrom(CombatEntity entity)
    {
        if (TriggeredEffectTracker.Remove(Name, entity.EntityId))
            CombatEventBus.RaiseTriggeredEffectRemoved(entity.EntityId, entity.Name, Name);
    }
}
```

Every triggered effect subclasses this directly and declares its registry key (`Name`). `OnBeforeDeath` is
`virtual` with a no-op default (death not prevented) rather than `abstract`, since not every triggered effect
necessarily reacts to death — a subclass only needs to override it if it does. There is currently
only one trigger category — dying — so it isn't broken out into a separate enum or an intermediate
base class; if a second trigger category is ever added (e.g. "on taking damage"), that's the point
at which `Trigger`/`TriggerKind` would come back, alongside a second virtual method or a
per-trigger interface.

`TotalApplications`, `ApplicationsThisRound`, and `RemoveFrom` are `virtual` rather than plain
instance methods so a subclass could override how its own counts are derived or what removing it
means; today every triggered effect (just `LivingDeadTriggeredEffect`) uses the base implementations as-is, reading
and writing straight through to `TriggeredEffectTracker`.

### `TriggeredEffectRegistry`

```csharp
public static class TriggeredEffectRegistry
{
    private static readonly Dictionary<string, TriggeredEffect> _triggeredEffects =
        new TriggeredEffect[] { new LivingDeadTriggeredEffect() }.ToDictionary(p => p.Name);

    // Returns null for an unrecognised name, the same way PowerKeywordRegistry ignores
    // unrecognised keywords.
    public static TriggeredEffect? Resolve(string triggeredEffectName) =>
        _triggeredEffects.GetValueOrDefault(triggeredEffectName);

    // Every registered triggered effect name - what the "triggeredEffect" enum in the action
    // schemas and monster.schema.json's "triggeredEffects" enum are both hand-maintained against,
    // and what the matching drift-guard tests check them against.
    public static IEnumerable<string> RegisteredNames => _triggeredEffects.Keys;
}
```

`TriggeredEffectRegistry` is `public` (nothing outside `CombatEngine` calls `Resolve` directly, but
`RegisteredNames` is read cross-assembly by the schema drift-guard tests, the same way
`CombatFunctionRegistry.RegisteredNames` already was). `Resolve` turns a single triggered effect name into
its live (stateless, shared) instance, or `null` if the name isn't registered. It's called from
exactly one place, `TriggeredEffectTracker.Add` (see below) — once per grant, not once per dispatch, since
`TriggeredEffectTracker` caches the resolved instance rather than re-resolving it on every death check.

## How it's wired end-to-end

1. **JSON data** — `monster.schema.json`'s `triggeredEffects` field is a string array **restricted by an enum**, unlike keywords:
   ```json
   "triggeredEffects": {
     "type": "array",
     "items": { "type": "string", "enum": ["LivingDead"] }
   }
   ```
   That enum is **hand-maintained** against `TriggeredEffectRegistry.cs`'s registered `TriggeredEffectName` constants, in `src/GameEngine/Schemas/monster.schema.json` (the canonical schema — `src/GameDataEditor/schemas/` is a plain copy produced by `npm run copy-schemas`). If you add a new triggered effect class, hand-add its name to the enum there.
   - Only `Monster` has a `triggeredEffects` field today — `Adventurer` has no equivalent, so player characters can't currently carry triggered effects.
2. **Data class** — `Monster.TriggeredEffects` is `List<string>?`, loaded straight from that JSON field.
3. **`GameEngineClass.InitSkirmishCombat`** grants each monster's triggered effects *after* calling
   `CombatEngineClass.Instance.InitCombat(allies, enemies)`:
   ```csharp
   CombatEngineClass.Instance.InitCombat(allies, enemies);

   foreach (var (entityId, monster) in _enemyMonsterMap)
       foreach (var triggeredEffectName in monster.TriggeredEffects ?? [])
           TriggeredEffectTracker.Add(triggeredEffectName, entityId);
   ```
   This has to happen *after* `InitCombat`, not before or during `CombatEntity` construction,
   because `InitCombat` calls `TriggeredEffectTracker.Reset()` — anything granted earlier would be wiped.
4. **`TriggeredEffectTracker`** (see below) is the sole record of which triggered effects an entity owns.
   `CombatEntity` itself carries no triggered-effect-related state.
5. **Mid-combat, via `triggeredEffectsApplied`** — `CombatFunctionParameters.TriggeredEffectsApplied` is a
   `TriggeredEffectApplySpec[]` rider any Tech/Item/MonsterAction can carry, exactly like `buffsDebuffs`
   and `regensDrains`:
   ```json
   "triggeredEffectsApplied": [
     { "triggeredEffect": "LivingDead", "target": "Self" }
   ]
   ```
   `CombatFunction.ApplyTriggeredEffects` (called by `BasicDamageFunction`/`BasicHealFunction`/
   `NoDirectEffectsFunction`, the same three that opt into the other two riders) resolves each
   entry's `target` through `CombatRoster.ResolveBuffDebuffTargets` and calls
   `TriggeredEffectTracker.Add(spec.TriggeredEffect, entity.EntityId)` for each resolved entity. Two entries that
   resolve to the same `(entity, triggeredEffect)` pair throw, naming the offending Tech/Item/MonsterAction
   — the same collision rule `ApplyBuffsDebuffs`/`ApplyRegensDrains` enforce. When `Add` reports a
   genuine new grant (not a re-grant of something already owned), `CombatEventBus.TriggeredEffectApplied`
   fires so the UI can log it and the combatant card can show it.

## Trigger point

Triggered effects currently fire from exactly one place: `CombatEntity.HandleDefeat`, called from
`CombatEntity.TakeDamage` right after a hit brings a target to 0 HP:

```csharp
if (Hp == 0 && !IsDead)
    HandleDefeat(sourceId, sourceName);
```

`TakeDamage` is the engine's standard damage-application step — every `CombatFunction` routes
damage through it (directly, or via the shared `CalculateAndApplyDamage(ctx)` helper), so any
function that deals damage this way gets death handling for free. A bespoke function that wrote
`target.Hp` directly instead would bypass triggered effects entirely.

```csharp
private void HandleDefeat(string sourceId, string sourceName)
{
    foreach (var triggeredEffect in TriggeredEffectTracker.GetTriggeredEffects(EntityId))
    {
        var (deathPrevented, reviveHp) = triggeredEffect.OnBeforeDeath(this);

        if (deathPrevented)
        {
            Hp = reviveHp;
            CombatEventBus.RaiseEntityRevived(EntityId, Name, oldHp, Hp, sourceId, sourceName);
            return; // only one triggered effect can prevent death, so stop after the first one that does
        }
    }
    // ... no triggered effect prevented it: mark the entity dead, raise EntityDeath, etc.
}
```

`GetTriggeredEffects` returns live triggered effect instances straight out of the tracker's records — no registry
lookup happens on this path. The loop tries each of the target's triggered effects and stops as soon as one
reports success (`OnBeforeDeath` returns `deathPrevented: true`); **iteration order is
unspecified** (the tracker is dictionary-backed), unlike a plain list. That's fine today — there's
only one triggered effect, and only one can ever prevent death per `HandleDefeat` call regardless of order
— but don't rely on ordering between two death-preventing triggered effects without adding an explicit
priority. The engine itself applies `reviveHp` to `Hp` and raises `EntityRevived` — the triggered effect
only decides *whether* and *at what HP*, it doesn't touch `Hp` or the event bus directly. If none
succeed (or the entity has no triggered effects), the entity actually dies.

## `TriggeredEffectTracker`

`TriggeredEffectTracker` (`src/CombatEngine/TriggeredEffects/TriggeredEffectTracker.cs`) is the single source of truth for
both *which* triggered effects an entity owns and *how often* they've fired. It replaces what used to be
two separate pieces of state on `CombatEntity` (`Passives`, `ConsumedPassives`) with one static
store, scoped to the current combat:

```csharp
public struct TriggeredEffectActivation
{
    public TriggeredEffect TriggeredEffect;      // the registry singleton, resolved once by Add
    public string  EntityId;
    public int     RoundApplied;          // round the entity acquired this triggered effect
    public int     ApplicationsThisRound; // zeroed by BeginRound
    public int     TotalApplications;     // for the whole combat
}
```

Keyed internally by `(triggeredEffectName, entityId)`. Lifecycle:

- **`Reset()`** — called by `CombatEngineClass.InitCombat`. Clears every record and resets
  `CurrentRound` to 1. Triggered effects must be granted (`Add`) *after* this runs.
- **`BeginRound(round)`** — called by `CombatEngineClass.BuildRound`, right after the round
  counter increments. Updates `CurrentRound` and zeroes every record's `ApplicationsThisRound`;
  `TotalApplications` and `RoundApplied` are untouched.
- **`Add(triggeredEffectName, entityId)`** — grants the triggered effect, resolving the name through
  `TriggeredEffectRegistry` once and caching the instance. Stamps `RoundApplied = CurrentRound`. A no-op
  if the entity already owns that triggered effect (existing history stands) or if the name doesn't
  resolve. Returns `true` only when a new record was actually created, so a mid-combat caller
  (`CombatFunction.ApplyTriggeredEffects`) can tell a genuine grant apart from a no-op and raise
  `CombatEventBus.TriggeredEffectApplied` only for the former.
- **`RecordActivation(triggeredEffect, entityId)`** — called by a triggered effect's own trigger method (e.g.
  `LivingDeadTriggeredEffect.OnBeforeDeath`) at the point it decides it actually did something.
  Increments both `TotalApplications` and `ApplicationsThisRound`. If the entity was never
  explicitly `Add`ed the triggered effect, this lazily creates the record instead of throwing — useful for
  unit tests that drive a triggered effect directly.
- **`GetTriggeredEffects(entityId)`** / **`Get(triggeredEffectName, entityId)`** — read access; `Get` returns a
  default (all-zero) struct for an untracked pair, so callers don't need a `TryGet`.

"Rounds elapsed since this entity acquired a triggered effect" has no dedicated method — it's just
`TriggeredEffectTracker.CurrentRound - TriggeredEffectTracker.Get(name, entityId).RoundApplied` at the call site.

### Adding and removing triggered effects mid-combat

`Add`/`Remove` aren't only for initial setup — call them any time during a combat to grant or
strip a triggered effect. The `triggeredEffectsApplied` rider (see "How it's wired end-to-end" above) is the
authored path for granting; a triggered effect can also strip itself via the base class's `RemoveFrom`
(see below) — `LivingDeadTriggeredEffect` does exactly this to enforce its own one-shot behavior. Nothing
currently authors a call to `Remove` from data, so stripping a triggered effect from outside its own trigger
is still engine/triggered-effect-code only (e.g. `LivingDeadTests` calls `TriggeredEffectTracker.Remove` directly
for test setup). `Remove(triggeredEffectName, entityId)` drops the record entirely, history and all, and
returns `true` only when a record was actually dropped — the same true-only-on-genuine-change
contract `Add` follows, so `TriggeredEffect.RemoveFrom` can tell a real removal apart from a no-op and
raise `CombatEventBus.TriggeredEffectRemoved` only for the former (the UI drops the triggered effect from the
combatant card's Effects list on that event). A later `Add` of the same triggered effect starts completely
fresh: `RoundApplied` is restamped to whatever round the `Add` happens in, and both application
counts go back to zero — which also **re-arms** a once-per-combat triggered effect like `LivingDead`.
There's currently no way to strip ownership while preserving history (e.g. to temporarily suppress
a triggered effect without resetting it) — that would need an `Owned` flag on the record, which nothing
today reads.

## Catalog of implemented triggered effects

### `LivingDeadTriggeredEffect` (`"LivingDead"`)

```csharp
public class LivingDeadTriggeredEffect : TriggeredEffect
{
    public const string TriggeredEffectName = "LivingDead";
    public override string Name => TriggeredEffectName;

    public override (bool, int) OnBeforeDeath(CombatEntity target)
    {
        // One-shot: drop ownership now, so HandleDefeat's dispatch loop won't find this triggered
        // effect on the entity again for any later lethal hit.
        RemoveFrom(target);
        return (true, CombatBalance.Current.LivingDeadReviveHp);
    }
}
```

The first time this entity would die, `OnBeforeDeath` calls the base class's `RemoveFrom(target)`,
which drops the `(LivingDead, target.EntityId)` record from `TriggeredEffectTracker` entirely and raises
`CombatEventBus.TriggeredEffectRemoved` (so the UI drops `LivingDead` off the combatant card), then reports
`(true, reviveHp)` — the engine sets `Hp` to `CombatBalance.Current.LivingDeadReviveHp` (1 HP by
default) and raises `EntityRevived`. Because the record is gone, `TriggeredEffectTracker.GetTriggeredEffects`
no longer returns this triggered effect for that entity, so `HandleDefeat`'s dispatch loop simply never
calls `OnBeforeDeath` on it again — the single-use guarantee lives in ownership, not in an
internal flag checked at the top of the method. (Unless something calls `TriggeredEffectTracker.Add` to
re-grant `LivingDead` on that entity in between — see "Adding and removing triggered effects mid-combat"
above — which creates a fresh record and re-arms it. An explicit `Remove` first isn't even
necessary at that point, since firing already removed the old record; `Remove` only matters for
stripping a `LivingDead` that hasn't fired yet.)

## Extending: adding a new triggered effect

1. Subclass `TriggeredEffect` in `src/CombatEngine/TriggeredEffects/`. If the triggered effect reacts to death, override
   `OnBeforeDeath`; the base class's default no-op is fine for a triggered effect that doesn't. There's
   currently only one trigger category (dying), so no separate trigger enum or intermediate base
   class to plug into — see the note under `TriggeredEffect` above for what a second trigger category
   would look like. Use `TriggeredEffectTracker.Get`/`RecordActivation` for any activation-count or
   round-based condition the triggered effect needs, and `RemoveFrom` if the triggered effect should strip its own
   ownership on firing (e.g. a one-shot like `LivingDead`), rather than inventing new state on
   `CombatEntity`.
2. Add an instance of the new class to the array in `TriggeredEffectRegistry`.
3. Hand-add the new triggered effect's name to **four** enums under `src/GameEngine/Schemas/` (the
   canonical schemas — `src/GameDataEditor/schemas/` is a plain copy): `monster.schema.json`'s
   `triggeredEffects.items.enum`, and `parameters.properties.triggeredEffectsApplied.items.properties.triggeredEffect.enum`
   in `tech.schema.json`, `item.schema.json`, and `monsteraction.schema.json`. Then run
   `npm run copy-schemas` (in `GameDataEditor`) to propagate them. `TriggeredEffectRegistry_MatchesSchemaEnum`
   (`CombatFunctionRegistryTests.cs`) fails until all four agree with `TriggeredEffectRegistry.RegisteredNames`.
4. Nothing else to wire up — `GameEngineClass.InitSkirmishCombat` already grants every monster's
   `TriggeredEffects` via `TriggeredEffectTracker.Add` after `InitCombat`, and `CombatFunction.ApplyTriggeredEffects`
   already resolves any `triggeredEffectsApplied` rider through the same `Add`, so a new triggered effect name in
   the data becomes usable automatically on both paths.
