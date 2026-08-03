# Damage Calc Type

Reference for the `DamageCalcType` enum in `CombatEngine.Enums`.

## Overview

`DamageCalcType` controls how an action's `PowerFactor` is turned into a base damage/heal amount, before Defense mitigation is applied. It's set via the `calcType` parameter on `BasicDamage` and `BasicHeal` combat functions (`CombatFunctionParameters.CalcType`), and defaults to `StandardFormula` when omitted.

## Core type

```csharp
namespace CombatEngine.Enums;

public enum DamageCalcType
{
    StandardFormula,
    FixedPower,
    FixedDamage,
}
```

Its meaning is entirely defined by the branches in `CombatMath.CalculateBaseAmount` (`src/CombatEngine/Engine/CombatMath.cs`), the shared helper behind both `CalculateDamage` and `CalculateHealAmount`:

```csharp
private static double CalculateBaseAmount(CombatEntity actor, double effectivePowerFactor, DamageCalcType calcType)
{
    double actionPower = calcType == DamageCalcType.FixedPower
        ? effectivePowerFactor
        : actor.Power * effectivePowerFactor;
    double baseAmount = calcType == DamageCalcType.FixedDamage
        ? effectivePowerFactor
        : (actionPower * 2f) + (actor.Level * 5f);
    return baseAmount;
}
```

## Catalog of values

| Value | `actionPower` | `baseAmount` | Use case |
|---|---|---|---|
| `StandardFormula` | `actor.Power * effectivePowerFactor` | `actionPower*2 + actor.Level*5` | Default — power-based actions that should scale with the actor's stats. |
| `FixedPower` | `effectivePowerFactor` (Power ignored) | `actionPower*2 + actor.Level*5` (Level still applies) | Actions with a flat power value that shouldn't scale with the actor's Power stat, but should still grow with Level. |
| `FixedDamage` | n/a (unused) | `effectivePowerFactor` directly (Power and Level both ignored) | Actions that deal an exact, unscaling base amount — e.g. a fixed-damage item or trap effect. |

Defense mitigation (`CalculateDamage`) and the lack of mitigation in `CalculateHealAmount` apply identically regardless of `calcType` — the enum only changes `baseAmount`, not what happens to it afterward:

```
rawDamage = baseAmount / ((target.Defense + 128) / 128) - (target.Defense / 2)
damage    = max(0, rawDamage)   // CalculateDamage only; CalculateHealAmount skips this entirely
```

## How it's wired end-to-end

1. **JSON data** — `tech.schema.json`, `item.schema.json`, and `monsteraction.schema.json` each expose `parameters.calcType` as a string restricted to `["StandardFormula", "FixedPower", "FixedDamage"]`, defaulting to `"StandardFormula"`.
2. **Data class** — `CombatFunctionParameters.CalcType` (`DamageCalcType?`) loads straight from that JSON field.
3. **Combat functions** — `BasicDamageFunction` and `BasicHealFunction` default a missing value: `DamageCalcType calcType = ctx.Parameters.CalcType ?? DamageCalcType.StandardFormula;`.
4. **`CombatMath`** — `calcType` is passed into `ctx.CalculateDamage`/`ctx.CalculateHealAmount`, which forward it to `CalculateBaseAmount`.

## Worked examples

From `tests/Terratopia.Tests/CombatEngine/PublicInterface/DamageTests.cs`:

**`StandardFormula` baseline** (`Damage_WithNoDefense_DealsExpectedAmount`): Power=10, Level=1, `powerFactor=1`, Defense=0 → `baseDamage = 10*2 + 1*5 = 25` → **25** damage.

**`FixedPower` ignores Power, not Level/Defense:**
- `Damage_FixedPower_DoesNotScaleWithActorPower`: two attackers, Power=10 and Power=200, both Level=1, `powerFactor=50`, Defense=0 → both deal **exactly 105 damage** (`actionPower` fixed at 50 regardless of Power; `baseDamage = 50*2 + 1*5 = 105`).
- `Damage_FixedPower_StillScalesWithLevelAndDefense`: Power=10 (irrelevant), Level=5, `powerFactor=50`, Defense=50 → `baseDamage = 50*2 + 5*5 = 125`; mitigated to `125/((50+128)/128) - 25 ≈ 64.89` → **64**.

**`FixedDamage` ignores Power and Level, Defense still applies:**
- `Damage_FixedDamage_DoesNotScaleWithActorPowerOrLevel`: Power=10/Level=1 vs Power=200/Level=20, same `powerFactor=50`, Defense=0 → both deal **exactly 50 damage** (`baseAmount` is just `powerFactor`, no doubling, no level term).
- `Damage_FixedDamage_StillMitigatedByDefense`: Power=10/Level=5 (both irrelevant), `powerFactor=50`, Defense=50 → `baseDamage = 50`; mitigated to `50/((50+128)/128) - 25 ≈ 10.96` → **10**.

**More `StandardFormula` reference points:**
- `Damage_ScalesWithPowerFactor`: Power=10, `powerFactor=2.0` → `actionPower=20` → `baseDamage=45` → **45** damage.
- `Damage_ScalesWithLevel`: Power=0, Level=5 → `baseDamage = 0*2 + 5*5 = 25` → **25** damage purely from Level.

## See also

- [`combat-functions.md`](combat-functions.md) — the `CombatFunctionParameters` reference table there documents `CalcType` inline as well; keep the two in sync.
