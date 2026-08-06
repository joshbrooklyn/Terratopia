# Keywords

Reference for the `Keywords` system in `CombatEngine.Keywords`.

## Overview

A **keyword** is a named modifier tag attached to a single action (a `Tech`, `Item`, or `MonsterAction`). When that action is used in combat, its keywords adjust the action's effective power factor — the multiplier that scales into damage. Keywords are conditional (e.g. "target is above 75% HP") or stack with repeated use (e.g. "gains power the more it's used").

This is a different mechanism from **[Passives](passives.md)**: keywords live on *actions* and react to how/when that action is used, while passives live on *entities* and react to combat events (currently, death). See `passives.md` for that system.

## Core types

### `PowerKeyword` (abstract base)

```csharp
public abstract class PowerKeyword
{
    public abstract string Name { get; }

    // Called once per resolved command that carries this keyword, before any
    // bonus is computed. Default no-op; stacking keywords override this to
    // increment their usage counters.
    public virtual void OnUsed(CombatEntity actor, bool actorIsAlly, string actionId, IKeywordUsageStore store) { }

    // Raw, uncapped bonus fraction for one (effect, target) pair. The caller
    // applies the shared cap — see "Resolution order" below.
    public abstract double GetBonus(CombatEntity actor, CombatEntity target, bool actorIsAlly, string actionId, IKeywordUsageStore store);
}
```

Every keyword subclasses this and implements `Name` (its registry key) and `GetBonus` (its effect). Keywords that need to remember how many times they've been used also override `OnUsed`.

### `IKeywordUsageStore`

```csharp
public interface IKeywordUsageStore
{
    int Increment(string key);
    int GetCount(string key);
}
```

A simple named-counter store. `KeywordResolver` (`CombatEngine.Engine`) implements this interface itself, backed by a `Dictionary<string, int> _usageCounts`. `CombatEngineClass.InitCombat` builds a fresh `KeywordResolver` per encounter — so usage counts are scoped to a single combat encounter, not persisted across fights.

### `PowerKeywordRegistry`

```csharp
public static class PowerKeywordRegistry
{
    private static readonly Dictionary<string, PowerKeyword> _keywords =
        new PowerKeyword[]
        {
            new TeamworkKeyword(),
            new EngageKeyword(),
            new CruelKeyword(),
            new EmpoweredKeyword(),
            new StoicKeyword(),
            new GrowthKeyword(),
        }.ToDictionary(k => k.Name);

    public static IEnumerable<PowerKeyword> Resolve(IEnumerable<string> names) =>
        names.Distinct()
             .Select(n => _keywords.GetValueOrDefault(n))
             .Where(k => k is not null)!;
}
```

`Resolve` turns a list of keyword name strings (from a `CombatCommand`) into live `PowerKeyword` instances. Two important behaviors baked into this one method:
- **Duplicate names collapse.** `names.Distinct()` means listing the same keyword twice on an action has no additional effect.
- **Unknown names are silently dropped.** There's no error for a typo or a not-yet-implemented keyword name — it just contributes nothing.

## How it's wired end-to-end

Keyword names are pure data. The flow from game data to damage math:

1. **JSON data** — `tech.schema.json`, `item.schema.json`, and `monsteraction.schema.json` each have a `keywords` field: an array of unique strings restricted by an `enum` to the currently registered keyword names (`Teamwork`, `Engage`, `Cruel`, `Empowered`, `Stoic`, `Growth`). That enum is **hand-maintained, not generated** — it must be kept in sync by hand with `PowerKeywordRegistry.cs`'s registered `KeywordName` constants. `ContentLoader` validates every GameData file against its schema at load time and throws on an invalid value, so an out-of-sync enum (or a name not yet added to it) breaks loading, not just editor validation — see `Resolve` above for the separate, more forgiving runtime behavior once a name does pass schema validation.
2. **Data classes** — `Tech.Keywords`, `Item.Keywords`, `MonsterAction.Keywords` are `List<string>` properties that load straight from that JSON.
3. **`GameEngineClass`** copies both the keyword list and a `SourceId`/`SourceName` (`Tech.TechId`/`Tech.Name`, `Item.ItemId`/`Item.Name`, or `MonsterAction.MonsterActionId`/`MonsterAction.Name`) onto the `CombatCommand` it builds, in `MakeTechCommand`, `UseItem`, and `MakeMonsterCombatCommand`:
   ```csharp
   Keywords   = tech.Keywords,
   SourceId   = tech.TechId,
   SourceName = tech.Name,
   ```
   The `SourceId` matters because `GrowthKeyword` needs to distinguish "used this specific action before" from "used a different action before" (see catalog below); `SourceName` is echoed onto effect events so callers can report what caused them without a separate lookup. The plain "Fight" command (`MakeFightCommand`) sets `SourceId`/`SourceName` to `"fight"`/`"Fight"` and carries no keywords.
4. **`CombatCommand`** just holds this data (`CombatEngine.DataClasses.CombatCommand`):
   ```csharp
   public List<string> Keywords { get; init; } = [];
   public string SourceId { get; init; } = string.Empty;
   public string SourceName { get; init; } = string.Empty;
   ```
5. **`CombatEngineClass.ResolveAction`** resolves the keywords when the command is actually executed, then hands both the `OnUsed` notification and the capped bonus summation to `KeywordResolver` (see next section). `KeywordResolver` implements `IKeywordUsageStore` itself, so it *is* the usage-counter store passed to every keyword call.

## Resolution order per action

Resolution is split across three places: `CombatEngineClass.ResolveAction` resolves the keyword list and fires `OnUsed`, the `CombatFunction` decides *when* (and whether) bonuses apply, and `KeywordResolver.ApplyKeywordBonuses` does the capping and summation.

**1. `CombatEngineClass.ResolveAction`** — once per command:

```csharp
bool actorIsAlly    = _roster.IsPlayerEntity(actor);
var  activeKeywords = PowerKeywordRegistry.Resolve(cmd.Keywords).ToList();
_keywords.NotifyKeywordsUsed(activeKeywords, actor, actorIsAlly, cmd.SourceId);

function.Execute(new CombatFunctionContext(_roster, _keywords, activeKeywords)
{
    Command = cmd, Actor = actor, ActorIsAlly = actorIsAlly, Targets = targets, Rng = _rng,
});
```

`activeKeywords` is held by the context (passed into its constructor alongside `_keywords`), so the
function never sees the keyword list at all — only `ctx.ApplyKeywordBonuses(basePowerFactor, actor,
target)`, which forwards to `KeywordResolver.ApplyKeywordBonuses` with the list already bound.

**2. The `CombatFunction`** — once per target. `CombatFunction.CalculateAndApplyDamage`, the shared
helper `BasicDamageFunction` calls, applies it like this:

```csharp
double basePowerFactor = ctx.Parameters.PowerFactor ?? DefaultPowerFactor;   // 1.0

foreach (var target in ctx.Targets)
{
    if (ctx.TryEvade(ctx.Actor, target))
        continue;                       // evaded: no bonus applied, no KeywordApplied event

    double keywordBonus         = ctx.ApplyKeywordBonuses(basePowerFactor, ctx.Actor, target);
    double effectivePowerFactor = basePowerFactor + keywordBonus;

    int damage = ctx.CalculateDamageAmount(ctx.Actor, target, effectivePowerFactor, calcType);
    // ... crit, ApplyDamage, death handling ...
}
```

This is the seam where a bespoke function diverges: simply not calling `ctx.ApplyKeywordBonuses` makes an action ignore keywords entirely.

**3. `KeywordResolver.ApplyKeywordBonuses`** — caps each contribution, then sums:

```csharp
double cap = Math.Min(basePowerFactor * 2, basePowerFactor + 0.5);

double totalBonus = 0.0;
foreach (var keyword in activeKeywords)
{
    double raw     = keyword.GetBonus(actor, target, actorIsAlly, actionId, this);
    double applied = Math.Min(raw, cap);
    if (applied > 0)
        CombatEventBus.RaiseKeywordApplied(/* ... */, applied);
    totalBonus += applied;
}
return totalBonus;
```

Step by step:

1. **Resolve once.** `cmd.Keywords` (strings) → `activeKeywords` (live instances), once per command — not once per target.
2. **`OnUsed` fires once per keyword, per command.** This happens before the `CombatFunction` runs at all, and regardless of how many targets the command has. This is the hook stacking keywords use to bump their counters.
3. **`GetBonus` fires once per keyword, per entry in `ctx.Targets`**, and is independently capped at `min(basePowerFactor * 2, basePowerFactor + 0.5)`. Each keyword's contribution is capped *separately*, then the capped contributions are **summed** — see the worked example below. Note that `ctx.Targets` preserves duplicates (`NumAttacks` with `AllowMultipleAttackOnSameTarget` can repeat a target), so a keyword can contribute more than once against the same entity.
4. **`effectivePowerFactor` feeds `CalculateDamageAmount`** (`CombatMath`), which is otherwise unaware of keywords.

## Event bus notification

Every time a keyword's capped contribution is greater than zero, `KeywordResolver.ApplyKeywordBonuses` also calls `CombatEventBus.RaiseKeywordApplied(keyword.Name, actor.EntityId, actor.Name, target.EntityId, target.Name, applied)`, where `applied` is the same capped bonus (`Math.Min(raw, cap)`) that fed `effectivePowerFactor`. This fires once per (keyword, target) pair, same granularity as `GetBonus` itself — a command with multiple effects or targets can raise it multiple times per keyword. A keyword whose condition isn't met (bonus of 0, e.g. `Cruel` against a healthy target) never raises the event. UI (`CombatantCard.cs`) and other listeners subscribe to `CombatEventBus.KeywordApplied` the same way they subscribe to `EntityDamaged`/`AttackEvaded`.

## Capping rules

These match the original design spec (`Obsidian/keywords.md`) and are verified by `MultipleKeywordsTests.cs` / `GrowthTests.cs` / `TeamworkTests.cs`:

- A keyword's bonus cannot push the power factor gain from *that keyword alone* past **double** the action's own base power factor, or the base plus 50%, whichever is lower (`cap = Math.Min(CDE.PowerFactor * 2, CDE.PowerFactor + 0.5)`).
- **If multiple keywords are active, each has its own independent cap** — they don't share a combined cap. Example: an 80% power-mod action with both `Engage` and `Stoic` active can reach 180% (80% + 50% + 50%, both well under the `min(160%, 130%) = 130%` per-keyword cap), not 130%.
- **If the action's base power factor is 0%, keywords add nothing** — the cap becomes `Math.Min(0, 0.5) = 0`. Note `OnUsed` still fires (counters still increment), only the damage contribution is zero.
- **Duplicate keyword names on one command have no extra effect** (enforced by `PowerKeywordRegistry.Resolve`'s `.Distinct()`).

## Catalog of implemented keywords

| Name | Trigger condition | Bonus | Stateful (tracks usage)? |
|---|---|---|---|
| `Engage` | target's HP ≥ 75% of their max HP | +50% | No |
| `Cruel` | target's HP ≤ 25% of their max HP | +50% | No |
| `Empowered` | actor's HP ≥ 75% of their max HP | +50% | No |
| `Stoic` | actor's HP ≤ 25% of their max HP | +50% | No |
| `Growth` | always contributes | +10% per *prior* use of this exact action by this actor, this combat | Yes — key `growth:{actorId}:{actionId}` |
| `Teamwork` | always contributes | +5% per prior `Teamwork`-tagged action used by the actor's *side*, this combat | Yes — key `ally:Teamwork` / `enemy:Teamwork` |

The four HP-threshold keywords (`Engage`, `Cruel`, `Empowered`, `Stoic`) are stateless — they just read current HP and don't call `OnUsed`. `Growth` and `Teamwork` are stateful and use disjoint counter-key schemes so they never collide, even when both are active on the same command.

`Growth`'s bonus formula is `0.10 * max(0, count - 1)`, because `OnUsed` increments the counter *before* `GetBonus` reads it — the triggering use itself gets no bonus, only uses after it do. So repeated uses of the same action stack 0%, 10%, 20%, 30%, ...

`Teamwork`'s bonus formula is `0.05 * count`, where `count` is incremented once per Teamwork-tagged command from anyone on that side (not per actor) — so it rewards the whole team using Teamwork actions, and two different allies both contribute to (and benefit from) the same counter.

### Worked example: two keywords stacking

From `MultipleKeywordsTests.cs`: an action with base `PowerFactor = 0.8` (80%) tagged with both `Engage` and `Stoic`, where both conditions are met:

- The per-keyword cap is `min(0.8 * 2, 0.8 + 0.5) = min(1.6, 1.3) = 1.3`.
- `Engage`'s raw bonus (0.5) is under that cap, so it's untouched → contributes +0.5.
- `Stoic`'s raw bonus (0.5) is under the same cap → contributes +0.5.
- `effectivePowerFactor = 0.8 + 0.5 + 0.5 = 1.8` (180%).

And from `GrowthTests.cs`, showing the cap kicking in on a stacking keyword: with base `PowerFactor = 0.1`, the cap is `min(0.2, 0.6) = 0.2`. After three prior uses, `Growth`'s raw bonus would be `0.10 * 3 = 0.30`, but it's clipped to `0.2` — so the fourth use of that action deals the same damage as the third, not more.

## Not yet implemented

The original design note (`Obsidian/keywords.md`) also specifies two keywords that have no code yet:

- **Exhaust** — usable once per combat; the action can't be used again until combat ends.
- **Single Use** — the action (typically an item) is destroyed after one use.

If you're looking for these and can't find them, that's why — they aren't wired into `PowerKeywordRegistry` or anywhere else yet. They are also intentionally **not** in the `keywords` schema enum until they're implemented — any GameData referencing them will fail schema validation (and fail to load, per `ContentLoader`) until then.

## Extending: adding a new keyword

1. Create a new class in `src/CombatEngine/Keywords/` that subclasses `PowerKeyword`, implementing `Name` and `GetBonus` (and `OnUsed` if it needs to track usage).
2. Add an instance of it to the array in `PowerKeywordRegistry`.
3. Hand-add the class's `KeywordName` to the `keywords.items.enum` array in `tech.schema.json`, `item.schema.json`, and `monsteraction.schema.json` under `src/GameEngine/Schemas/` (the canonical schemas), then run `npm run copy-schemas` in `GameDataEditor` to propagate the change to its schema copy. Game data can then reference the new keyword's `Name`.
