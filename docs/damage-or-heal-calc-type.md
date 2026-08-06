# Damage-Or-Heal Calc Type

Reference for the `DamageOrHealCalcType` enum in `CombatEngine.Enums`.

## Overview

`DamageOrHealCalcType` controls how an action's `PowerFactor` is turned into a base damage/heal amount, before Defense mitigation is applied. It's set via the `calcType` parameter on `BasicDamage` and `BasicHeal` combat functions (`CombatFunctionParameters.CalcType`), and defaults to `StandardFormula` when omitted.

## Core type

```csharp
namespace CombatEngine.Enums;

public enum DamageOrHealCalcType
{
    StandardFormula,
    FixedPower,
    FixedAmount,
    PercentOfMax,
}
```

Its meaning is entirely defined by the branches in `CombatMath.CalculateBaseAmount` (`src/CombatEngine/Engine/CombatMath.cs`), the shared helper behind both `CalculateDamageAmount` and `CalculateHealAmount`. Note it takes `target` as well as `actor` — `PercentOfMax` is the reason: it needs the *target's* MaxHp, not the actor's:

```csharp
private static double CalculateBaseAmount(CombatEntity actor, CombatEntity target, double effectivePowerFactor, DamageOrHealCalcType calcType)
{
    if (calcType == DamageOrHealCalcType.PercentOfMax)
        return effectivePowerFactor * target.MaxHp;

    double actionPower = calcType == DamageOrHealCalcType.FixedPower
        ? effectivePowerFactor
        : actor.Power * effectivePowerFactor;
    double baseAmount = calcType == DamageOrHealCalcType.FixedAmount
        ? effectivePowerFactor
        : (actionPower * 2f) + (actor.Level * 5f);
    return baseAmount;
}
```

For `CalculateDamageAmount`, `target` is the entity being hit. For `CalculateHealAmount`, `target` is the entity being healed (not the caster) — so `PercentOfMax` healing is a percentage of the *healed entity's* MaxHp, matching the standard RPG "heal for X% of target's max HP" meaning.

## Catalog of values

| Value | `actionPower` | `baseAmount` | Use case |
|---|---|---|---|
| `StandardFormula` | `actor.Power * effectivePowerFactor` | `actionPower*2 + actor.Level*5` | Default — power-based actions that should scale with the actor's stats. |
| `FixedPower` | `effectivePowerFactor` (Power ignored) | `actionPower*2 + actor.Level*5` (Level still applies) | Actions with a flat power value that shouldn't scale with the actor's Power stat, but should still grow with Level. |
| `FixedAmount` | n/a (unused) | `effectivePowerFactor` directly (Power and Level both ignored) | Actions that deal an exact, unscaling base amount — e.g. a fixed-damage item or trap effect. |
| `PercentOfMax` | n/a (unused) | `effectivePowerFactor * target.MaxHp` (Power and Level both ignored) | Actions that scale with the target's max HP rather than the actor's stats — e.g. a "deals/heals X% of target's max HP" move. `effectivePowerFactor` is the percentage as a fraction (`0.2` = 20%). |

Defense mitigation (`CalculateDamageAmount`) and the lack of mitigation in `CalculateHealAmount` apply identically regardless of `calcType` — the enum only changes `baseAmount`, not what happens to it afterward:

```
rawDamage = baseAmount / ((target.Defense + 128) / 128) - (target.Defense / 2)
damage    = max(0, rawDamage)   // CalculateDamageAmount only; CalculateHealAmount skips this entirely
```

## How it's wired end-to-end

1. **JSON data** — `tech.schema.json`, `item.schema.json`, and `monsteraction.schema.json` each expose `parameters.calcType` as a string restricted to `["StandardFormula", "FixedPower", "FixedAmount", "PercentOfMax"]`, defaulting to `"StandardFormula"`.
2. **Data class** — `CombatFunctionParameters.CalcType` (`DamageOrHealCalcType?`) loads straight from that JSON field.
3. **Combat functions** — `CombatFunction.CalculateAndApplyDamage`/`CalculateAndApplyHealing` (the shared helpers `BasicDamageFunction`/`BasicHealFunction` call) default a missing value: `DamageOrHealCalcType calcType = ctx.Parameters.CalcType ?? DamageOrHealCalcType.StandardFormula;`.
4. **`CombatMath`** — `calcType` is passed into `ctx.CalculateDamageAmount`/`ctx.CalculateHealAmount` along with `target`, which forward both to `CalculateBaseAmount`.

## Worked examples

From `tests/Terratopia.Tests/CombatEngine/PublicInterface/DamageTests.cs`:

**`StandardFormula` baseline** (`Damage_WithNoDefense_DealsExpectedAmount`): Power=10, Level=1, `powerFactor=1`, Defense=0 → `baseDamage = 10*2 + 1*5 = 25` → **25** damage.

**`FixedPower` ignores Power, not Level/Defense:**
- `Damage_FixedPower_DoesNotScaleWithActorPower`: two attackers, Power=10 and Power=200, both Level=1, `powerFactor=50`, Defense=0 → both deal **exactly 105 damage** (`actionPower` fixed at 50 regardless of Power; `baseDamage = 50*2 + 1*5 = 105`).
- `Damage_FixedPower_StillScalesWithLevelAndDefense`: Power=10 (irrelevant), Level=5, `powerFactor=50`, Defense=50 → `baseDamage = 50*2 + 5*5 = 125`; mitigated to `125/((50+128)/128) - 25 ≈ 64.89` → **64**.

**`FixedAmount` ignores Power and Level, Defense still applies:**
- `Damage_FixedAmount_DoesNotScaleWithActorPowerOrLevel`: Power=10/Level=1 vs Power=200/Level=20, same `powerFactor=50`, Defense=0 → both deal **exactly 50 damage** (`baseAmount` is just `powerFactor`, no doubling, no level term).
- `Damage_FixedAmount_StillMitigatedByDefense`: Power=10/Level=5 (both irrelevant), `powerFactor=50`, Defense=50 → `baseDamage = 50`; mitigated to `50/((50+128)/128) - 25 ≈ 10.96` → **10**.

**`PercentOfMax` ignores Power and Level, scales with the target's MaxHp, Defense still applies to damage:**
- `Damage_PercentOfMax_DoesNotScaleWithActorPowerOrLevel`: Power=10/Level=1 vs Power=200/Level=20, same `powerFactor=0.05` (5%), target MaxHp=1000, Defense=0 → both deal **exactly 50 damage** (`baseAmount = 0.05 * 1000 = 50`, no doubling, no level term).
- `Damage_PercentOfMax_StillMitigatedByDefense`: `powerFactor=0.05`, target MaxHp=1000, Defense=50 → `baseDamage = 50`; mitigated the same way as `FixedAmount`'s equivalent case → **10** damage.

**More `StandardFormula` reference points:**
- `Damage_ScalesWithPowerFactor`: Power=10, `powerFactor=2.0` → `actionPower=20` → `baseDamage=45` → **45** damage.
- `Damage_ScalesWithLevel`: Power=0, Level=5 → `baseDamage = 0*2 + 5*5 = 25` → **25** damage purely from Level.

## See also

- [`combat-functions.md`](combat-functions.md) — the `CombatFunctionParameters` reference table there documents `CalcType` inline as well; keep the two in sync.
