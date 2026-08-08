# TriggeredEffectTracker Keeps Its Own Round Counter - August 7, 2026


## Problem

`TriggeredEffectTracker` stores its own `_currentRound` field
(`src/CombatEngine/TriggeredEffects/TriggeredEffectTracker.cs`), separate from `CombatEngineClass._roundNumber`
(`src/CombatEngine/Engine/CombatEngineClass.cs`), which is the round counter that actually
advances combat. That looks like the round is "tracked in two different places" and worth
collapsing into one.

## Decision

Keep both fields. `CombatEngineClass._roundNumber` stays the sole writer; `TriggeredEffectTracker`
keeps its own `_currentRound`, updated only via `BeginRound(round)`, called from exactly one
site — `CombatEngineClass.BuildRound()`, right after it increments `_roundNumber`. No source
change.

**Why:**

1. **Not true dual-authority.** Only one call site ever increments or writes the round
   (`BuildRound` → `BeginRound`). `TriggeredEffectTracker` never advances the round on its own — it's a
   single-writer projection, not two independently-changing sources of truth. That's the kind
   of duplication *not* worth eliminating.
2. **The alternative is a worse coupling.** Having `TriggeredEffectTracker.CurrentRound` read
   `CombatEngineClass.Instance` live instead of storing a copy would reverse today's dependency
   direction — currently `Engine` depends on `TriggeredEffects` (`Reset`/`BeginRound`/`Add`), not the
   other way around. It would also break the isolation that lets `LivingDeadTests.cs` drive
   round-based triggered-effect behavior by calling `TriggeredEffectTracker.BeginRound(N)` directly, with no
   `CombatEngineClass` instance involved — every round-based triggered-effect test would instead need
   full `InitCombat`/`BuildRound` engine plumbing, and shared singleton state becomes a
   test-isolation hazard.
3. `TriggeredEffectTracker.Add`/`RecordActivation`/`RemoveFrom` are called from inside `TriggeredEffect`
   subclasses (e.g. `LivingDeadTriggeredEffect.OnBeforeDeath`, which calls `RemoveFrom` to enforce its
   one-shot behavior), which have no reference to `CombatEngineClass` at all — the mirrored round
   is what lets a triggered effect record "what round did this happen in" without the triggered-effects
   system knowing the engine exists.

The cost of one mirrored `int`, updated from a single call site, is small and contained. The
cost of coupling `TriggeredEffects` to the `Engine` singleton is not.

## What Comes Next

This holds as long as `TriggeredEffectTracker` needs to stay independently testable and decoupled from
`CombatEngineClass`. If that changes — e.g. `CombatEngineClass` stops being a singleton, or
round-based triggered-effect tests start needing full engine state anyway for other reasons — revisit
whether the mirror is still worth keeping separate.
