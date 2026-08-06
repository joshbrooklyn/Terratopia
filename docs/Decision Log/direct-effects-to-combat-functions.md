# DirectEffects Replaced by CombatFunctions - August 1, 2026


## Problem

An action's behaviour was described by data alone: `CombatCommand.DirectEffects` was a list of
`CombatDirectEffect` records, each carrying an `EffectType` enum (`Damage`, `Heal`), an element, a
calc type, and a power factor. `CombatEngineClass.ResolveAction()` looped that list and hardcoded
one resolution path for all of it.

Three problems followed from that:

- **Only damage was actually implemented.** `CombatDirectEffectType.Heal` existed in the enum and
  was authorable in every schema, but `ResolveAction` ran the damage path regardless of
  `EffectType` — a `Heal` effect dealt damage. The enum's own comment called itself a placeholder.
- **New behaviour meant editing the engine.** Anything that wasn't "roll evade, add keyword bonus,
  run the damage formula, roll crit, subtract HP" had no home. A life drain, a self-buff, a
  party-wide effect, or a plain "pass" turn would each have needed another branch inside
  `ResolveAction`.
- **The effect list was duplicated four times.** `TechDirectEffect`, `ItemDirectEffect`,
  `MonsterActionDirectEffect`, and `CombatDirectEffect` were the same four fields declared
  separately, with three near-identical mapping blocks in `GameEngineClass` to convert between
  them.

`targetStatuses` and `userStatuses` had the same shape of problem: authorable on all three action
types, read by nothing.

## Design

**The name in the data selects the class that resolves the action.** An action carries a
`combatFunction` string. At execution time `CombatFunctionRegistry` resolves it to a
`CombatFunction` instance, which owns the *entire* resolution — the target loop, evasion, crit,
damage/healing, and every event raised along the way. This mirrors how `Keywords` already resolve
through `PowerKeywordRegistry` (see [keywords.md](../keywords.md)).

**Unknown names are fatal, unlike keywords.** `PowerKeywordRegistry` silently drops a name it
doesn't recognise — acceptable, because the action just loses a bonus. `CombatFunctionRegistry`
throws, because the CombatFunction *is* the action: dropping it would turn a Tech into a silent
no-op instead of surfacing the typo.

**The engine exposes its standard steps rather than the function being subclassed.**
`CombatFunctionContext` hands the function a method for each standard step (`TryEvade`, `RollCrit`,
`ApplyKeywordBonuses`, `CalculateDamage`, `ApplyDamage`, …). A function overrides behaviour by
*declining to call* one of them, not by inheriting from the engine.

> **Update, later:** these steps started as `Func`/`Action` members individually wired in
> `CombatEngineClass.ResolveAction`, deliberately mirroring the pattern `CombatFlowMachine`'s
> constructor callbacks use. That bought per-command override of a standard step's
> implementation — a capability nothing ever used. Once it was clear only the shared, engine-owned
> implementation was ever needed, the members were turned into ordinary instance methods on
> `CombatFunctionContext`, closing over the roster/keyword collaborators directly instead of
> through per-command closures built in `ResolveAction`. Call sites (`ctx.TryEvade(...)`, etc.)
> and the override-by-not-calling contract are unchanged.

**Parameters are one flat, closed, hand-maintained bag.** `CombatFunctionParameters` is a single
class shared by every function, mirrored one-for-one by the `parameters` block in all three
schemas. Every field is nullable and optional, because JSON Schema can't express "BasicDamage
needs an element" — each function validates what it requires inside its own `Execute`.

Nullable rather than defaulted is deliberate: `double PowerFactor { get; init; } = 1.0` can't
distinguish "omitted" from "authored as 1.0", which would make per-function validation impossible.

**Functions are stateless.** The registry hands out one shared instance per name.

## New Types

### `CombatEngine/CombatFunctions/CombatFunction.cs`

Abstract base. Two members — `Name` and `Execute(CombatFunctionContext)`. The class comment carries
the implementation contract: call `ctx.DeductTpCost()` before touching a target, and validate your
own parameters by throwing `InvalidOperationException` naming `ctx.Command.SourceId`.

### `CombatEngine/CombatFunctions/CombatFunctionContext.cs`

Sealed, built fresh by `CombatEngineClass` once per resolved command. Splits into what's being
resolved (`Command`, `Actor`, `ActorIsAlly`, `Parameters`, `Targets`, `AllEntities`, `GetEntity`,
`Rng`) and the standard-step methods.

Two of the steps are deliberately split in half so a function can override one side and keep the
other:

| Split | Halves |
|---|---|
| TP | `ResolveTpCost()` owns the cost, `DeductTp(entity, amount)` owns the deduction. `DeductTpCost()` is the convenience combining both. |
| Crit | `RollCrit(actor)` owns the chance, `ApplyCritModifier(actor, amount)` owns the multiplier. |

`Targets` is `ChosenTargets` resolved to entities **in order and including duplicates** —
`NumAttacks` combined with `AllowMultipleAttackOnSameTarget` can legitimately repeat a target.

Full member reference lives in [combat-functions.md](../combat-functions.md).

### `CombatEngine/CombatFunctions/CombatFunctionRegistry.cs`

Static name → instance dictionary. `Resolve(name)` throws `InvalidOperationException` listing the
registered names on a miss. `RegisteredNames` is exposed solely so a test can assert the
hand-synced schema enum hasn't drifted.

### `CombatEngine/DataClasses/CombatFunctionParameters.cs`

```csharp
public class CombatFunctionParameters
{
    public ElementType?    Element     { get; init; }
    public DamageCalcType? CalcType    { get; init; }
    public double?         PowerFactor { get; init; }
}
```

### The three shipped functions

**`BasicDamageFunction`** (`"BasicDamage"`) — the pre-refactor engine behaviour, verbatim: deduct
TP, then per target roll evasion, add keyword bonuses to the base power factor, run the damage
formula, roll a crit, apply. `Element` does not yet feed the formula; it's what the UI reports and
what elemental resistances will key off later.

**`BasicHealFunction`** (`"BasicHeal"`) — the behaviour the old `Heal` enum member never had.
Deliberately narrower than damage: a heal on an ally isn't dodgeable and doesn't crit, so it
consumes **no randomness at all**, which keeps the seeded draw order of every other test easy to
reason about. Keyword power bonuses do apply. Healing a dead target is a no-op — revival will be a
dedicated function, not a side effect of healing.

**`NoOpFunction`** (`"NoOp"`) — pays the TP cost and nothing else. Both a legitimate authored
action (a "pass"/"defend" that burns a turn) and the explicit way to say "this command resolves to
nothing".

## Deleted Types

| Type | Reason |
|---|---|
| `CombatEngine/DataClasses/CombatDirectEffect.cs` | Replaced by `combatFunction` + `CombatFunctionParameters`. |
| `CombatEngine/Enums/CombatDirectEffectType.cs` | The `Damage`/`Heal` enum is now a function name. |
| `TechDirectEffect`, `ItemDirectEffect`, `MonsterActionDirectEffect` | Three duplicate declarations of the same four fields, collapsed into the shared parameters class. |

## Changed Files

### `CombatEngine/DataClasses/CombatCommand.cs`

`List<CombatDirectEffect> DirectEffects` → `required string CombatFunction` plus
`CombatFunctionParameters Parameters` (defaulted to a new instance, so always non-null).

### `CombatEngine/Engine/CombatEngineClass.cs`

`ResolveAction()` shrank to: look up the actor, resolve the function, resolve and notify keywords,
build the context, call `Execute`. Everything it used to do inline was extracted into private
members that are now handed to the context as delegates:

| Extracted member | Was |
|---|---|
| `DeductTp(actor, amount)` | Inline TP block at the top of `ResolveAction`. |
| `TryEvade(actor, target)` | `EvasionCheck` plus the evasion-decay and event-raising that followed its call site. Rolled together so the decay can't be forgotten. |
| `ApplyDamage(actor, target, damage, isCrit)` | Inline HP write, `EntityDamaged`/`EntityHpChanged` raises, and the death check. |
| `ApplyHeal(actor, target, amount)` | **New.** Clamps to `MaxHp`, skips dead targets, raises `EntityHealed`/`EntityHpChanged`. |
| `CalculateBaseAmount(actor, powerFactor, calcType)` | **New.** The half of the formula damage and healing share. |
| `CalculateDamage(...)` | Same formula; now takes `DamageCalcType` directly instead of a `CombatDirectEffect`. |
| `CalculateHealAmount(...)` | **New.** `CalculateBaseAmount` without the target's Defense divisor — healing ignores defense. |
| `GetEntity(id)` | The repeated `TryGetValue`-or-throw target lookup. |

Behaviour was preserved exactly for damage, including RNG draw order: standard rolls still go
through `TryEvade`/`RollCrit` in the same sequence, so existing seeded tests produce identical
results.

### `GameEngine/DataClasses/Tech.cs`, `Item.cs`, `MonsterAction.cs`

Each dropped `TargetStatuses`, `UserStatuses`, and its own `DirectEffects` list, and gained
`required string CombatFunction` + `CombatFunctionParameters Parameters`.

### `GameEngine/Engine/GameEngineClass.cs`

`MakeTechCommand`, `MakeItemCommand`, and `MakeMonsterActionCommand` each lost their ~15-line
`directEffects` mapping block; they now pass `CombatFunction` and `Parameters` straight through.

`MakeFightCommand` builds a `BasicDamage` command with `PowerFactor = 1.0` and no element.

**Dropped validation:** each mapping block used to throw `ArgumentException` if a `Damage` effect
had no element. That check is gone — a null element now means non-elemental (physical), which is
what the basic Fight command needs. Per-function requirements now belong inside `Execute`.

### `Game/Scenes/Battle.cs`

The combat log's effect summary was built from the `DirectEffects` list; it now reads
`cmd.CombatFunction`, suffixed with `cmd.Parameters.Element` when one is present.

## Schema Changes — v2 → v3

Identical edits to `tech.schema.json`, `item.schema.json`, and `monsteraction.schema.json`:

- `schemaVersion` const `2` → `3`
- Removed `targetStatuses`, `userStatuses`, and the `directEffects` array
- Added `combatFunction` (string, enum `["BasicDamage", "BasicHeal", "NoOp"]`) — and added it to
  each schema's `required` list
- Added `parameters` (object, `additionalProperties: false`) with `element`, `calcType`, and
  `powerFactor`

Both `combatFunction`'s enum and `parameters`' properties are **hand-mirrored** against the C#
side. See "Drift Protection" below for what catches a mistake.

## Data Migration

### `GameDataEditor/src/migrations.ts` — `directEffectsToCombatFunction`

Registered as the `2:` step for all three schemas. Converts only the shape the engine actually
supported — exactly one `Damage` effect — copying `element`/`calcType`/`powerFactor` into
`parameters` and setting `combatFunction: "BasicDamage"`.

Anything else (multiple effects, a `Heal` effect, no effects at all) is left **untouched and
un-bumped**, so `runMigrations` reports `incomplete` and the file is flagged needs-manual-fix
rather than mangled. No such data exists today; the branch is there so that if any appears it fails
loudly.

`targetStatuses`/`userStatuses` are deleted. A non-empty one produces a migration note quoting the
dropped contents, since their future home is a CombatFunction parameter rather than a top-level
field.

### `GameDataEditor/src/formEditorPanel.ts`

`handleSelect` now refuses to open a file whose `schemaVersion` is below the schema's, directing
the user to run "GameData: Scan & Migrate" first.

This closed a real data-loss path rather than being a convenience: `initStateFromSchema` keeps only
keys the schema still declares, so opening a stale v2 file would silently drop its `directEffects`
from the in-memory state, and saving would then stamp `schemaVersion: 3` over the top — destroying
exactly the data the migration existed to convert.

## Editor Support for Nested Objects

`parameters` is the first single nested object in any schema; the Form Editor only understood flat
fields and arrays-of-objects. Four places needed teaching, all generically off the schema rather
than special-cased to `parameters`:

| File | Change |
|---|---|
| `media/main.js` — `renderObjectField` | **New.** Renders a nested object as one fixed fieldset — the same body `renderObjectListField` uses per array entry, minus the add/remove buttons, since the object is always present. |
| `media/main.js` — `initStateFromSchema` / `serializeObject` | Recurse into nested object properties instead of treating the whole object as one scalar value. |
| `media/main.js` — client-side validation | Added an `'object'` case recursing into `validateObjectClient`. |
| `src/schemaValidation.ts` | Added the matching `'object'` case recursing into `validateObject` — this is what makes `additionalProperties: false` on the nested block actually reject unknown fields. |

`DISABLED_FIELDS.Techs` dropped `targetStatuses`/`userStatuses`, which no longer exist.

Because rendering is schema-driven, adding a future function or parameter needs no editor code
changes at all — see [combat-functions.md](../combat-functions.md) steps 4 and 6.

## Drift Protection

`combatFunction`'s enum and `parameters`' properties are hand-synced across four places (the
registry, the parameters class, and three schemas). Two tests in
`tests/Terratopia.Tests/CombatEngine/CombatFunctionTests.cs` fail on any divergence:

- **`CombatFunctionRegistry_MatchesSchemaEnum`** — registry names vs. each schema's
  `combatFunction` enum.
- **`CombatFunctionParameters_MatchesSchemaSuperset`** — `CombatFunctionParameters`' properties
  (camel-cased, matching `ContentLoader`'s naming policy) vs. each schema's
  `parameters.properties`.

The same file also covers registry resolution, the throw on an unknown name, TP deduction, and
`BasicHeal`'s formula, max-HP cap, dead-target no-op, and zero-randomness guarantee. Every existing
CombatEngine test file was updated to author commands with `CombatFunction` + `Parameters`.

## What Comes Next

- **Statuses.** `targetStatuses`/`userStatuses` were removed rather than reworked. When statuses
  are implemented they arrive as a `CombatFunctionParameters` field consumed by a function, not as
  a top-level action field.
- **Elemental resistances.** `Parameters.Element` is carried and reported but doesn't yet affect
  the damage formula.
- **Revival.** Explicitly not a side effect of `BasicHeal`; it will be its own function.

## See Also

- [combat-functions.md](../combat-functions.md) — step-by-step guide to adding a new
  `CombatFunction`, with full `CombatFunctionContext` and `CombatFunctionParameters` reference
  tables.
- [schema-migrations.md](../schema-migrations.md) — how migration steps are written and run.
- [keywords.md](../keywords.md) — the registry pattern this refactor followed.
