# Passives

Reference for the `Passives` system in `CombatEngine.Passives`.

## Overview

A **passive** is a named ability attached to a combat entity (currently monsters only, via `Monster.Passives`) that reacts to a combat *event* — currently, only that entity dying. Passives are data-driven by name only: the JSON data references a passive by string, and all of its actual behavior is hardcoded in a C# class.

This is a different mechanism from **[Keywords](keywords.md)**: passives live on *entities* and react to combat events, while keywords live on *actions* and react to how/when that specific action is used. See `keywords.md` for that system.

> The original design notes (`Obsidian/passive-effects.md`) describe a much broader long-term vision for "passive effects" — stat modifiers, elemental resistance changes, run/dungeon-wide modifiers, perks, gear, curses & boons, and more. The code described on this page only implements one narrow slice of that vision: on-death triggers. Don't mistake this doc for that larger design — it documents what's actually built today.

## Core types

### `Passive` (abstract base)

```csharp
public abstract class Passive
{
    public abstract string Name { get; }

    // Returns (deathPrevented, reviveHp). deathPrevented tells the engine whether to keep this
    // entity alive; when true, reviveHp is the HP the engine will set the entity to.
    public virtual (bool, int) OnBeforeDeath(CombatEntity target)
    {
        return (false, 0);
    }

    // Convenience wrapper around PassiveTracker.Get so a subclass doesn't have to know the
    // tracker is keyed by (Name, entity.EntityId).
    public virtual int TotalApplications(CombatEntity entity)
    {
        return PassiveTracker.Get(Name, entity.EntityId).TotalApplications;
    }
}
```

Every passive subclasses this directly and declares its registry key (`Name`). `OnBeforeDeath` is
`virtual` with a no-op default (death not prevented) rather than `abstract`, since not every passive
necessarily reacts to death — a subclass only needs to override it if it does. There is currently
only one trigger category — dying — so it isn't broken out into a separate enum or an intermediate
base class; if a second trigger category is ever added (e.g. "on taking damage"), that's the point
at which `Trigger`/`PassiveTrigger` would come back, alongside a second virtual method or a
per-trigger interface.

`TotalApplications` is `virtual` rather than a plain instance method so a subclass could
override how its own count is derived; today every passive (just `LivingDeadPassive`) uses the
base implementation as-is, reading straight through to `PassiveTracker`.

### `PassiveRegistry`

```csharp
internal static class PassiveRegistry
{
    private static readonly Dictionary<string, Passive> _passives =
        new Passive[] { new LivingDeadPassive() }.ToDictionary(p => p.Name);

    // Returns null for an unrecognised name, the same way PowerKeywordRegistry ignores
    // unrecognised keywords.
    public static Passive? Resolve(string passiveName) =>
        _passives.GetValueOrDefault(passiveName);
}
```

`PassiveRegistry` is `internal` — nothing outside `CombatEngine` needs it directly.
`Resolve` turns a single passive name into its live (stateless, shared) instance, or `null` if
the name isn't registered. It's called from exactly one place, `PassiveTracker.Add` (see below) —
once per grant, not once per dispatch, since `PassiveTracker` caches the resolved instance rather
than re-resolving it on every death check.

## How it's wired end-to-end

1. **JSON data** — `monster.schema.json`'s `passives` field is a string array **restricted by an enum**, unlike keywords:
   ```json
   "passives": {
     "type": "array",
     "items": { "type": "string", "enum": ["LivingDead"] }
   }
   ```
   That enum is **hand-maintained** against `PassiveRegistry.cs`'s registered `PassiveName` constants, in `src/GameEngine/Schemas/monster.schema.json` (the canonical schema — `src/GameDataEditor/schemas/` is a plain copy produced by `npm run copy-schemas`). If you add a new passive class, hand-add its name to the enum there.
   - Only `Monster` has a `passives` field today — `Adventurer` has no equivalent, so player characters can't currently carry passives.
2. **Data class** — `Monster.Passives` is `List<string>?`, loaded straight from that JSON field.
3. **`GameEngineClass.InitSkirmishCombat`** grants each monster's passives *after* calling
   `CombatEngineClass.Instance.InitCombat(allies, enemies)`:
   ```csharp
   CombatEngineClass.Instance.InitCombat(allies, enemies);

   foreach (var (entityId, monster) in _enemyMonsterMap)
       foreach (var passiveName in monster.Passives ?? [])
           PassiveTracker.Add(passiveName, entityId);
   ```
   This has to happen *after* `InitCombat`, not before or during `CombatEntity` construction,
   because `InitCombat` calls `PassiveTracker.Reset()` — anything granted earlier would be wiped.
4. **`PassiveTracker`** (see below) is the sole record of which passives an entity owns.
   `CombatEntity` itself carries no passive-related state.

## Trigger point

Passives currently fire from exactly one place: `CombatEntity.HandleDefeat`, called from
`CombatEntity.TakeDamage` right after a hit brings a target to 0 HP:

```csharp
if (Hp == 0 && !IsDead)
    HandleDefeat(sourceId, sourceName);
```

`TakeDamage` is the engine's standard damage-application step — every `CombatFunction` routes
damage through it (directly, or via the shared `CalculateAndApplyDamage(ctx)` helper), so any
function that deals damage this way gets death handling for free. A bespoke function that wrote
`target.Hp` directly instead would bypass passives entirely.

```csharp
private void HandleDefeat(string sourceId, string sourceName)
{
    foreach (var passive in PassiveTracker.GetPassives(EntityId))
    {
        var (deathPrevented, reviveHp) = passive.OnBeforeDeath(this);

        if (deathPrevented)
        {
            Hp = reviveHp;
            CombatEventBus.RaiseEntityRevived(EntityId, Name, oldHp, Hp, sourceId, sourceName);
            return; // only one passive can prevent death, so stop after the first one that does
        }
    }
    // ... no passive prevented it: mark the entity dead, raise EntityDeath, etc.
}
```

`GetPassives` returns live passive instances straight out of the tracker's records — no registry
lookup happens on this path. The loop tries each of the target's passives and stops as soon as one
reports success (`OnBeforeDeath` returns `deathPrevented: true`); **iteration order is
unspecified** (the tracker is dictionary-backed), unlike a plain list. That's fine today — there's
only one passive, and only one can ever prevent death per `HandleDefeat` call regardless of order
— but don't rely on ordering between two death-preventing passives without adding an explicit
priority. The engine itself applies `reviveHp` to `Hp` and raises `EntityRevived` — the passive
only decides *whether* and *at what HP*, it doesn't touch `Hp` or the event bus directly. If none
succeed (or the entity has no passives), the entity actually dies.

## `PassiveTracker`

`PassiveTracker` (`src/CombatEngine/Passives/PassiveTracker.cs`) is the single source of truth for
both *which* passives an entity owns and *how often* they've fired. It replaces what used to be
two separate pieces of state on `CombatEntity` (`Passives`, `ConsumedPassives`) with one static
store, scoped to the current combat:

```csharp
public struct PassiveActivation
{
    public Passive Passive;               // the registry singleton, resolved once by Add
    public string  EntityId;
    public int     RoundApplied;          // round the entity acquired this passive
    public int     ApplicationsThisRound; // zeroed by BeginRound
    public int     TotalApplications;     // for the whole combat
}
```

Keyed internally by `(passiveName, entityId)`. Lifecycle:

- **`Reset()`** — called by `CombatEngineClass.InitCombat`. Clears every record and resets
  `CurrentRound` to 1. Passives must be granted (`Add`) *after* this runs.
- **`BeginRound(round)`** — called by `CombatEngineClass.BuildRound`, right after the round
  counter increments. Updates `CurrentRound` and zeroes every record's `ApplicationsThisRound`;
  `TotalApplications` and `RoundApplied` are untouched.
- **`Add(passiveName, entityId)`** — grants the passive, resolving the name through
  `PassiveRegistry` once and caching the instance. Stamps `RoundApplied = CurrentRound`. A no-op
  if the entity already owns that passive (existing history stands) or if the name doesn't
  resolve.
- **`RecordActivation(passive, entityId)`** — called by a passive's own trigger method (e.g.
  `LivingDeadPassive.OnBeforeDeath`) at the point it decides it actually did something.
  Increments both `TotalApplications` and `ApplicationsThisRound`. If the entity was never
  explicitly `Add`ed the passive, this lazily creates the record instead of throwing — useful for
  unit tests that drive a passive directly.
- **`GetPassives(entityId)`** / **`Get(passiveName, entityId)`** — read access; `Get` returns a
  default (all-zero) struct for an untracked pair, so callers don't need a `TryGet`.

"Rounds elapsed since this entity acquired a passive" has no dedicated method — it's just
`PassiveTracker.CurrentRound - PassiveTracker.Get(name, entityId).RoundApplied` at the call site.

### Adding and removing passives mid-combat

`Add`/`Remove` aren't only for initial setup — call them any time during a combat to grant or
strip a passive (e.g. as the effect of a tech, item, or another passive). `Remove(passiveName,
entityId)` drops the record entirely, history and all. That means a later `Add` of the same
passive starts completely fresh: `RoundApplied` is restamped to whatever round the `Add` happens
in, and both application counts go back to zero — which also **re-arms** a once-per-combat passive
like `LivingDead`, since its guard is just `TotalApplications > 0`. There's currently no way to
strip ownership while preserving history (e.g. to temporarily suppress a passive without resetting
it) — that would need an `Owned` flag on the record, which nothing today reads.

## Catalog of implemented passives

### `LivingDeadPassive` (`"LivingDead"`)

```csharp
public class LivingDeadPassive : Passive
{
    public const string PassiveName = "LivingDead";
    public override string Name => PassiveName;

    public override (bool, int) OnBeforeDeath(CombatEntity target)
    {
        bool alreadyTriggered = TotalApplications(target) > 0;
        if (alreadyTriggered)
            return (false, 0);

        PassiveTracker.RecordActivation(this, target.EntityId);
        return (true, CombatBalance.Current.LivingDeadReviveHp);
    }
}
```

The first time this entity would die, `TotalApplications(target)` — the base class's convenience
read of `(LivingDead, target.EntityId)` in `PassiveTracker` — is still `0`, so `RecordActivation`
bumps it to `1` and the passive reports `(true, reviveHp)` — the
engine then sets `Hp` to `CombatBalance.Current.LivingDeadReviveHp` (1 HP by default) and raises
`EntityRevived`. Every subsequent death attempt, `TotalApplications` is already `> 0` —
`OnBeforeDeath` returns `(false, 0)`, and the entity dies normally. (Unless something calls
`PassiveTracker.Remove` then re-`Add`s `LivingDead` on that entity in between — see "Adding and
removing passives mid-combat" above — which resets `TotalApplications` back to `0` and re-arms it.)

## Extending: adding a new passive

1. Subclass `Passive` in `src/CombatEngine/Passives/`. If the passive reacts to death, override
   `OnBeforeDeath`; the base class's default no-op is fine for a passive that doesn't. There's
   currently only one trigger category (dying), so no separate trigger enum or intermediate base
   class to plug into — see the note under `Passive` above for what a second trigger category
   would look like. Use `PassiveTracker.Get`/`RecordActivation` for any activation-count or
   round-based condition the passive needs, rather than inventing new state on `CombatEntity`.
2. Add an instance of the new class to the array in `PassiveRegistry`.
3. Hand-add the new passive's name to `monster.schema.json`'s `passives.items.enum` under `src/GameEngine/Schemas/` (the canonical schema), then run `npm run copy-schemas` (in `GameDataEditor`) to propagate it, so game data can reference the new passive by name.
4. Nothing else to wire up — `GameEngineClass.InitSkirmishCombat` already grants every monster's
   `Passives` via `PassiveTracker.Add` after `InitCombat`, so a new passive name in the data
   becomes usable automatically.
