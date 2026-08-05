# Adding a Custom CombatFunction

Step-by-step instructions for adding a new `CombatFunction` — the class that resolves an entire
combat action (damage, healing, or something bespoke).

Worked through one concrete example throughout: **`LifeDrain`** — deals damage to a target and
heals the actor for a percentage of the damage dealt.

## Checklist

1. [Create the function class](#1-create-the-function-class)
2. [Register it](#2-register-it)
3. [Add any new parameters it needs](#3-add-any-new-parameters-it-needs)
4. [Add it to the three schemas](#4-add-it-to-the-three-schemas)
5. [Add a migration step](#5-add-a-migration-step)
6. [Propagate schemas to the editor](#6-propagate-schemas-to-the-editor)
7. [Author it in game data](#7-author-it-in-game-data)
8. [Test it](#8-test-it)

---

## 1. Create the function class

New file: `src/CombatEngine/CombatFunctions/LifeDrainFunction.cs`

```csharp
using CombatEngine.Enums;

namespace CombatEngine.CombatFunctions;

public class LifeDrainFunction : CombatFunction
{
    public const string FunctionName = "LifeDrain";

    public override string Name => FunctionName;

    public override void Execute(CombatFunctionContext ctx)
    {
        double drainPercent = ctx.Parameters.DrainPercent
            ?? throw new InvalidOperationException(
                $"LifeDrain ('{ctx.Command.ActionId}'): parameters.drainPercent is required.");

        double               basePowerFactor = ctx.Parameters.PowerFactor ?? 1.0;
        DamageOrHealCalcType calcType        = ctx.Parameters.CalcType    ?? DamageOrHealCalcType.StandardFormula;

        ctx.DeductTpCost();

        foreach (var target in ctx.Targets)
        {
            if (ctx.TryEvade(ctx.Actor, target))
                continue;

            double keywordBonus         = ctx.ApplyKeywordBonuses(basePowerFactor, ctx.Actor, target);
            double effectivePowerFactor = basePowerFactor + keywordBonus;

            int  damage = ctx.CalculateDamageAmount(ctx.Actor, target, effectivePowerFactor, calcType);
            bool isCrit = ctx.RollCrit(ctx.Actor);
            if (isCrit)
                damage = ctx.ApplyCritModifier(ctx.Actor, damage);

            ctx.ApplyDamage(ctx.Actor, target, damage, isCrit);
            ctx.ApplyHeal(ctx.Actor, ctx.Actor, (int)(damage * drainPercent));
        }
    }
}
```

Points specific to writing your own, not just this example:
- Call `ctx.DeductTpCost()` before touching any target — it no-ops on a 0-TP command, so it's
  always safe to call unconditionally.
- Validate your own required parameters at the top of `Execute`, throwing
  `InvalidOperationException` and naming `ctx.Command.ActionId` — the schema can't express
  per-function requirements, so this is the only place they're enforced.
- Reuse `CombatFunctionContext`'s injected delegates (`TryEvade`, `RollCrit`,
  `ApplyKeywordBonuses`, `CalculateDamageAmount`/`CalculateHealAmount`, `ApplyDamage`/`ApplyHeal`,
  `DeductTp`) wherever your function's behavior matches the standard action. Skip whichever ones
  don't apply — e.g. a self-buff wouldn't call `TryEvade` or `RollCrit` at all.
- Keep the class stateless — the registry hands out one shared instance.

## 2. Register it

Edit `src/CombatEngine/CombatFunctions/CombatFunctionRegistry.cs`:

```diff
     private static readonly Dictionary<string, CombatFunction> _functions =
         new CombatFunction[]
         {
             new BasicDamageFunction(),
             new BasicHealFunction(),
             new NoOpFunction(),
+            new LifeDrainFunction(),
         }.ToDictionary(f => f.Name);
```

## 3. Add any new parameters it needs

`LifeDrain` needs a new `drainPercent` parameter. Edit
`src/CombatEngine/DataClasses/CombatFunctionParameters.cs`:

```diff
 public class CombatFunctionParameters
 {
     public ElementType?          Element     { get; init; }
     public DamageOrHealCalcType? CalcType    { get; init; }
     public double?               PowerFactor { get; init; }
+    public double?         DrainPercent { get; init; }
 }
```

It must be nullable — every field on this class is optional, since it's shared across every
registered function. (If your function only needs parameters that already exist — `Element`,
`CalcType`, `PowerFactor` — skip this step.)

## 4. Add it to the three schemas

Repeat this edit in **all three**: `src/GameEngine/Schemas/tech.schema.json`,
`src/GameEngine/Schemas/item.schema.json`, `src/GameEngine/Schemas/monsteraction.schema.json`.

Add the function name to the `combatFunction` enum:

```diff
     "combatFunction": {
       "type": "string",
-      "enum": ["BasicDamage", "BasicHeal", "NoOp"]
+      "enum": ["BasicDamage", "BasicHeal", "NoOp", "LifeDrain"]
     },
```

Add the new parameter to `parameters.properties`:

```diff
         "powerFactor": {
           "type": "number",
           "default": 1.0,
           "description": "BasicDamage/BasicHeal: optional, defaults to 1.0."
+        },
+        "drainPercent": {
+          "type": "number",
+          "description": "LifeDrain: required. Fraction of damage dealt restored to the actor as healing, e.g. 0.5 for 50%."
         }
```

Bump the `schemaVersion` const:

```diff
     "schemaVersion": {
       "type": "integer",
-      "const": 3,
+      "const": 4,
```

## 5. Add a migration step

Edit `src/GameDataEditor/src/migrations.ts` so existing v3 files migrate forward to v4. If
nothing about their *shape* needs to change (as here — `LifeDrain` is new, not a rework of
existing data), the step just bumps the version number:

```ts
function bumpToV4(data: Record<string, unknown>): MigrationResult {
	data.schemaVersion = 4;
	return { notes: [] };
}

const MIGRATIONS: Record<string, Record<number, MigrationStep>> = {
	'item.schema.json':          { 1: stripInvalidKeywords, 2: directEffectsToCombatFunction, 3: bumpToV4 },
	'tech.schema.json':          { 1: stripInvalidKeywords, 2: directEffectsToCombatFunction, 3: bumpToV4 },
	'monsteraction.schema.json': { 1: stripInvalidKeywords, 2: directEffectsToCombatFunction, 3: bumpToV4 },
};
```

If a file predating your change needs actual conversion (e.g. you renamed a field), write that
logic into the step instead — see [schema-migrations.md](schema-migrations.md).

Then, with the live GameData folder backed up first, run **"GameData: Scan & Migrate"** from the
VS Code command palette to apply it.

## 6. Propagate schemas to the editor

```powershell
cd src/GameDataEditor
npm run compile
```

This copies the three updated schemas into `src/GameDataEditor/schemas/`. No further editor code
changes are needed — the Form Editor renders `combatFunction`'s dropdown and every `parameters`
field generically off the schema, so `LifeDrain` and `drainPercent` just appear.

## 7. Author it in game data

```json
{
  "schemaVersion": 4,
  "techId": "vampiric_strike",
  "name": "Vampiric Strike",
  "jobClass": "Rogue",
  "tpCost": 8,
  "tier": 1,
  "rarity": 1,
  "description": "Deal Void damage and heal for 50% of the damage dealt.",
  "numAttacks": 1,
  "keywords": [],
  "traits": [],
  "targetingType": "Choose",
  "validTargets": "Enemies",
  "livingOrDead": "Living",
  "combatFunction": "LifeDrain",
  "parameters": {
    "element": "Void",
    "powerFactor": 1.0,
    "drainPercent": 0.5
  }
}
```

## 8. Test it

Run the suite:

```powershell
dotnet test tests\Terratopia.Tests\Terratopia.Tests.csproj
```

Two existing tests in `CombatFunctionTests.cs` automatically catch any drift from steps 2–4
without you writing anything new:
- `CombatFunctionRegistry_MatchesSchemaEnum` — fails if the registry and the three schemas'
  `combatFunction` enums disagree.
- `CombatFunctionParameters_MatchesSchemaSuperset` — fails if `CombatFunctionParameters` and the
  three schemas' `parameters.properties` disagree.

Then add a behavioral test for the function itself, in `CombatFunctionTests.cs`, following its
`// What:` / `// How:` comment convention:

```csharp
[Fact]
public void LifeDrain_HealsActorForPercentOfDamageDealt()
{
    // What: verifies LifeDrain heals the actor for exactly drainPercent × the damage it just
    //       dealt to the target, in the same action.
    // How:  Ally (Power=10, Level=1) attacks a Defense=0 target with powerFactor=1.0, dealing
    //       exactly 25 damage. With drainPercent=0.5, the actor should be healed for 12
    //       (25 * 0.5, truncated). SetupCombat's ally starts below max HP so the heal is
    //       observable; the test asserts both the damage dealt and the actor's HP increase.
    var opening = new CombatCommand
    {
        ActorId        = "ally",
        TargetingType  = TargetingType.Random,
        ValidTargets   = ValidTarget.Enemies,
        LivingOrDead   = LivingOrDead.Living,
        CombatFunction = LifeDrainFunction.FunctionName,
        Parameters     = new CombatFunctionParameters { PowerFactor = 1.0, DrainPercent = 0.5 },
    };
    var (engine, ally, _) = SetupCombat(opening, allyHp: 50);

    int? healedAmount = null;
    CombatEventBus.EntityHealed += (entityId, _, amount, _, _, _, _) =>
    {
        if (entityId == "ally") healedAmount ??= amount;
    };

    engine.BeginCombat();

    Assert.Equal(12, healedAmount);
}
```

---

## Reference: `CombatFunctionParameters`

The fields currently available to read in `ctx.Parameters`, in
`src/CombatEngine/DataClasses/CombatFunctionParameters.cs`. All are optional.

| Field | Type | Conveys |
|---|---|---|
| `Element` | `ElementType?` | Which element (`Fire`, `Ice`, `Lightning`, `Void`) the action is associated with. `null` means non-elemental (physical). |
| `CalcType` | `DamageOrHealCalcType?` | Whether `PowerFactor` is a multiplier on the actor's `Power` stat (`StandardFormula`), a flat power value that still scales with Level and is mitigated by Defense (`FixedPower`), the entire base amount outright — skipping both Power and Level, though Defense still mitigates it (`FixedAmount`), or a fraction of the target's MaxHp used as the entire base amount, again skipping Power and Level (`PercentOfMax`). |
| `PowerFactor` | `double?` | The action's base power modifier — the number that scales into a damage or heal amount before keyword bonuses are added. |
| `BuffsDebuffs` | `IReadOnlyList<BuffDebuffSpec>?` | Timed buffs/debuffs this action applies, each resolved and applied once after the action fully resolves — after all hits, all damage/healing, and regardless of what was evaded. `null` or empty means the action applies none. |
| `RegensDrains` | `IReadOnlyList<RegenDrainSpec>?` | Timed regen/drain this action applies, resolved and applied once the same way as `BuffsDebuffs`. Heals/restores or damages/spends a fixed percentage (`GameSettings.RegenDrainHpPct`/`RegenDrainTpPct`) of the target's MaxHp/MaxTp at the start of every round, with no elemental component. See [`regen-and-drain.md`](regen-and-drain.md). |

Each `BuffDebuffSpec` entry is fully self-contained — `Stat`, `Type` (`Positive`/`Negative`),
`Target`, `Rounds`, and `UntilRemoved` are all mandatory C# `required` properties, so unlike the
rest of `CombatFunctionParameters` there is no cross-field pairing left to enforce at combat time.
`UntilRemoved: true` makes `Rounds` irrelevant — the entry never expires from the round clock, only
via an opposite-polarity application on the same stat (see
[`buffs-and-debuffs.md`](buffs-and-debuffs.md#no-round-based-expiration-untilremoved)). `Target`
(`BuffDebuffTarget`) picks who the entry lands on, independent of the action's own targets:

| `BuffDebuffTarget` | Resolves to |
|---|---|
| `SelectedTargets` | The action's own chosen targets — `ctx.Targets`, de-duplicated and filtered to the living. |
| `Self` | The actor. |
| `RandomAlly` | One random living ally of the actor, excluding the actor itself. Empty if there is no other living ally. |
| `RandomEnemy` | One random living enemy of the actor. Empty if there are no living enemies. |
| `AllAllies` | Every living entity on the actor's side, actor included. |
| `AllEnemies` | Every living entity on the opposing side. |

"Ally"/"enemy" are relative to the actor, not to the player — a monster's `AllAllies` reaches the
other monsters. `RandomAlly`/`RandomEnemy` draw from the engine's shared `Rng`, so authoring one
shifts the draw sequence for everything resolved after it.

Use the shared `ApplyBuffsDebuffs(ctx)` helper on `CombatFunction` rather than resolving and
applying entries by hand — call it once, after your function's own damage/healing loop. It no-ops
when `BuffsDebuffs` is null or empty, resolves each entry's `Target` via
`ctx.ResolveBuffDebuffTargets`, and throws `InvalidOperationException` naming `ctx.Command.ActionId`
if two entries land on the same entity's stat (e.g. `Self` and `AllAllies` both moving `Power` for
a solo actor) — the schema's `uniqueBy` can only reject identical `(stat, target)` pairs, not two
different targets that happen to resolve to the same entity.

See [`buffs-and-debuffs.md`](buffs-and-debuffs.md) for the full writeup — timing/evasion semantics,
data-authoring examples, GameData Editor and migration support, and plain-English descriptions of
the test suite. `RegensDrains` (`IReadOnlyList<RegenDrainSpec>?`) works the same way, applied
through the analogous `ApplyRegensDrains(ctx)` helper; see
[`regen-and-drain.md`](regen-and-drain.md) for its full writeup.

## Reference: `CombatFunctionContext`

Everything available on `ctx` inside `Execute`, in
`src/CombatEngine/CombatFunctions/CombatFunctionContext.cs`.

| Member | Conveys |
|---|---|
| `Command` | The full `CombatCommand` being resolved — targeting rules, keyword list, action id, raw TP cost, and anything else not already broken out below. |
| `Actor` | The entity performing the action. |
| `ActorIsAlly` | Whether the actor is on the player's side or the enemy's. |
| `Parameters` | The action's parameters as authored in game data (see the table above). |
| `Targets` | The entities this action is resolving against, in order, including repeats for a multi-hit action that can strike the same target twice. |
| `AllEntities` | Every entity currently in the fight, keyed by id — for an effect that reaches beyond the chosen targets. |
| `GetEntity(id)` | Looks up any entity in the fight by id. |
| `Rng` | The combat's shared random source, for a roll with no standard equivalent below. |
| `ResolveTpCost()` | The TP cost this action should charge. |
| `DeductTp(entity, amount)` | Charges a TP amount to an entity. |
| `DeductTpCost()` | Convenience for `DeductTp(Actor, ResolveTpCost())` — charges the actor the action's own TP cost. |
| `TryEvade(actor, target)` | Whether an attack against the given target is dodged. |
| `RollCrit(actor)` | Whether the actor's next hit lands as a critical hit. |
| `ApplyCritModifier(actor, amount)` | The amount after the actor's critical-hit bonus is applied. |
| `ApplyKeywordBonuses(basePowerFactor, actor, target)` | The extra power the action's keywords contribute against the given target. |
| `CalculateDamageAmount(actor, target, powerFactor, calcType)` | The damage an attack deals. |
| `CalculateHealAmount(actor, powerFactor, calcType)` | The amount an action heals for. |
| `ApplyDamage(actor, target, amount, isCrit)` | Applies a damage amount to a target. |
| `ApplyHeal(actor, target, amount)` | Applies a heal amount to a target. |
| `ApplyBuffDebuff(target, stat, isPositive, rounds, untilRemoved)` | Applies a buff/debuff to one of a target's stats. Re-applying the same polarity extends it (or, if either side is `untilRemoved`, keeps it indefinite); the opposite polarity cancels the existing one out. `rounds` is ignored when `untilRemoved` is true. |
| `ResolveBuffDebuffTargets(selector)` | The living entities a `BuffDebuffTarget` selector resolves to, relative to `Actor`. Used by the shared `ApplyBuffsDebuffs(ctx)`/`ApplyRegensDrains(ctx)` helpers above — call that instead of this directly in almost every case. |
| `ApplyRegenDrain(target, stat, isPositive, rounds, untilRemoved)` | Applies a regen/drain to one of a target's resources (`Hp`/`Tp`). Same re-apply/cancel rules as `ApplyBuffDebuff`. |
