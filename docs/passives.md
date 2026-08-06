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
}
```

Every passive subclasses this directly and declares its registry key (`Name`). `OnBeforeDeath` is
`virtual` with a no-op default (death not prevented) rather than `abstract`, since not every passive
necessarily reacts to death — a subclass only needs to override it if it does. There is currently
only one trigger category — dying — so it isn't broken out into a separate enum or an intermediate
base class; if a second trigger category is ever added (e.g. "on taking damage"), that's the point
at which `Trigger`/`PassiveTrigger` would come back, alongside a second virtual method or a
per-trigger interface.

### `PassiveRegistry`

```csharp
public static class PassiveRegistry
{
    private static readonly Dictionary<string, Passive> _passives =
        new Passive[] { new LivingDeadPassive() }.ToDictionary(p => p.Name);

    // Yields, in order, the registered passives from passiveNames. Unrecognised names are
    // silently dropped, the same way PowerKeywordRegistry.Resolve treats unrecognised keywords.
    public static IEnumerable<Passive> Resolve(IEnumerable<string> passiveNames)
    {
        foreach (var name in passiveNames)
        {
            if (_passives.TryGetValue(name, out var passive))
                yield return passive;
        }
    }
}
```

`Resolve` is how the engine turns an entity's list of passive-name strings into live passive
instances — filtering out any name that doesn't resolve. Shaped the same way as
`CombatFunctionRegistry.Resolve`/`PowerKeywordRegistry.Resolve`.

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
3. **`GameEngineClass`** passes `monster.Passives` into the `passives` parameter when constructing a `CombatEntity` (in `MakeCombatEntity` / `InitSkirmishCombat`).
4. **`CombatEntity`** stores the names and tracks one-shot consumption:
   ```csharp
   public List<string> Passives { get; internal set; } = new();
   public HashSet<string> ConsumedPassives { get; } = new();
   ```
   `Passives` is the list of passive names attached to this entity. `ConsumedPassives` tracks which passive names have already fired their one-time effect on this entity (see `LivingDeadPassive` below) — this set is per-entity and isn't reset mid-combat, so a one-shot passive can't retrigger within the same fight.

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
    foreach (var passive in PassiveRegistry.Resolve(Passives))
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

The loop tries each of the target's passives, in the order they appear in `Passives`, and stops as
soon as one reports success (`OnBeforeDeath` returns `deathPrevented: true`). The engine itself
applies `reviveHp` to `Hp` and raises `EntityRevived` — the passive only decides *whether* and *at
what HP*, it doesn't touch `Hp` or the event bus directly. If none succeed (or the entity has no
passives), the entity actually dies.

## Catalog of implemented passives

### `LivingDeadPassive` (`"LivingDead"`)

```csharp
public class LivingDeadPassive : Passive
{
    public const string PassiveName = "LivingDead";
    public override string Name => PassiveName;

    public override (bool, int) OnBeforeDeath(CombatEntity target)
    {
        bool alreadyTriggered = target.HasConsumedPassive(Name);
        if (alreadyTriggered)
            return (false, 0);

        target.ConsumePassive(Name);
        return (true, CombatBalance.Current.LivingDeadReviveHp);
    }
}
```

The first time this entity would die, `HasConsumedPassive(Name)` is still `false`, so
`ConsumePassive(Name)` marks it fired and the passive reports `(true, reviveHp)` — the engine then
sets `Hp` to `CombatBalance.Current.LivingDeadReviveHp` (1 HP by default) and raises
`EntityRevived`. Every subsequent death attempt, `HasConsumedPassive` is already `true` —
`OnBeforeDeath` returns `(false, 0)`, and the entity dies normally.

## Extending: adding a new passive

1. Subclass `Passive` in `src/CombatEngine/Passives/`. If the passive reacts to death, override
   `OnBeforeDeath`; the base class's default no-op is fine for a passive that doesn't. There's
   currently only one trigger category (dying), so no separate trigger enum or intermediate base
   class to plug into — see the note under `Passive` above for what a second trigger category
   would look like.
2. Add an instance of the new class to the array in `PassiveRegistry`.
3. Hand-add the new passive's name to `monster.schema.json`'s `passives.items.enum` under `src/GameEngine/Schemas/` (the canonical schema), then run `npm run copy-schemas` (in `GameDataEditor`) to propagate it, so game data can reference the new passive by name.
