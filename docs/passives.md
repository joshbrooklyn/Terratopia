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
    public abstract PassiveTrigger Trigger { get; }
}
```

Every passive subclasses this (usually via a more specific base like `DeathPassive`) and declares its registry key (`Name`) and which event category it responds to (`Trigger`).

### `PassiveTrigger`

```csharp
public enum PassiveTrigger
{
    OnDeath,
}
```

Currently a single value. This is the enum that categorizes *when* a passive can fire; adding a new trigger category (e.g. "on taking damage", "on round start") means adding a new value here and a new base class like `DeathPassive` for it.

### `DeathPassive` (abstract, `OnDeath` passives)

```csharp
public abstract class DeathPassive : Passive
{
    public override PassiveTrigger Trigger => PassiveTrigger.OnDeath;

    // Returns true if death was prevented/reversed for this entity.
    public virtual bool TryPreventDeath(CombatEntity target)
    {
        return false;
    }
}
```

Fixes `Trigger` to `OnDeath` and adds the hook the engine actually calls: `TryPreventDeath`, which returns whether it successfully intervened.

### `PassiveRegistry`

```csharp
public static class PassiveRegistry
{
    private static readonly Dictionary<string, Passive> _passives =
        new Passive[] { new LivingDeadPassive() }.ToDictionary(p => p.Name);

    // Yields, in order, the registered passives from passiveNames that fire on `trigger`
    // and are of type T.
    public static IEnumerable<T> GetForTrigger<T>(IEnumerable<string> passiveNames, PassiveTrigger trigger)
        where T : Passive
    {
        foreach (var name in passiveNames)
        {
            if (_passives.TryGetValue(name, out var passive)
                && passive.Trigger == trigger
                && passive is T typed)
            {
                yield return typed;
            }
        }
    }
}
```

`GetForTrigger<T>` is how the engine turns an entity's list of passive-name strings into live, correctly-typed passive instances for a specific trigger — filtering out names that don't resolve, don't match the requested trigger, or aren't the requested subtype.

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

Passives currently fire from exactly one place: `CombatEngineClass.HandleEntityDefeated`, called from `CombatEngineClass.ApplyDamage` right after a hit brings a target to 0 HP:

```csharp
if (target.Hp == 0 && !target.IsDead)
    HandleEntityDefeated(target);
```

`ApplyDamage` is the engine's standard damage-application step, handed to every `CombatFunction` through `CombatFunctionContext.ApplyDamage` — so any function that deals damage through the context gets death handling for free. A bespoke function that wrote `target.Hp` directly instead would bypass passives entirely.

```csharp
private static void HandleEntityDefeated(CombatEntity target)
{
    foreach (var passive in PassiveRegistry.GetForTrigger<DeathPassive>(target.Passives, PassiveTrigger.OnDeath))
    {
        if (passive.TryPreventDeath(target))
            return; // death was prevented — stop checking further passives, entity stays alive
    }
    // ... no passive prevented it: mark the entity dead, raise EntityDeath, etc.
}
```

The loop tries each of the target's `OnDeath` passives, in the order they appear in `target.Passives`, and stops as soon as one reports success (`TryPreventDeath` returns `true`). If none succeed (or the entity has no `OnDeath` passives), the entity actually dies.

## Catalog of implemented passives

### `LivingDeadPassive` (`"LivingDead"`)

```csharp
public class LivingDeadPassive : DeathPassive
{
    public const string PassiveName = "LivingDead";
    public override string Name => PassiveName;

    public override bool TryPreventDeath(CombatEntity target)
    {
        if (!target.ConsumedPassives.Add(Name))
            return false;

        int oldHp = target.Hp;
        target.Hp = 1;
        CombatEventBus.RaiseEntityRevived(target.EntityId, target.Name, oldHp, target.Hp);
        return true;
    }
}
```

The first time this entity would die, `ConsumedPassives.Add(Name)` succeeds (returns `true` because the name wasn't already in the set), so the entity is revived to 1 HP instead of dying, and an `EntityRevived` event is raised. Every subsequent death attempt, `Add` returns `false` (the name's already there) — `TryPreventDeath` returns `false`, and the entity dies normally.

## Extending: adding a new passive

1. To add another `OnDeath` behavior, subclass `DeathPassive` in `src/CombatEngine/Passives/` and override `TryPreventDeath`. For a new category of trigger entirely (e.g. "on taking damage"), add a new `PassiveTrigger` value and a new abstract base class analogous to `DeathPassive`, plus a call site analogous to `HandleEntityDefeated` where that event actually occurs.
2. Add an instance of the new class to the array in `PassiveRegistry`.
3. Hand-add the new passive's name to `monster.schema.json`'s `passives.items.enum` under `src/GameEngine/Schemas/` (the canonical schema), then run `npm run copy-schemas` (in `GameDataEditor`) to propagate it, so game data can reference the new passive by name.
