# PassiveTracker Keeps Its Own Round Counter - August 7, 2026


## Problem

`PassiveTracker` stores its own `_currentRound` field
(`src/CombatEngine/Passives/PassiveTracker.cs`), separate from `CombatEngineClass._roundNumber`
(`src/CombatEngine/Engine/CombatEngineClass.cs`), which is the round counter that actually
advances combat. That looks like the round is "tracked in two different places" and worth
collapsing into one.

## Decision

Keep both fields. `CombatEngineClass._roundNumber` stays the sole writer; `PassiveTracker`
keeps its own `_currentRound`, updated only via `BeginRound(round)`, called from exactly one
site — `CombatEngineClass.BuildRound()`, right after it increments `_roundNumber`. No source
change.

**Why:**

1. **Not true dual-authority.** Only one call site ever increments or writes the round
   (`BuildRound` → `BeginRound`). `PassiveTracker` never advances the round on its own — it's a
   single-writer projection, not two independently-changing sources of truth. That's the kind
   of duplication *not* worth eliminating.
2. **The alternative is a worse coupling.** Having `PassiveTracker.CurrentRound` read
   `CombatEngineClass.Instance` live instead of storing a copy would reverse today's dependency
   direction — currently `Engine` depends on `Passives` (`Reset`/`BeginRound`/`Add`), not the
   other way around. It would also break the isolation that lets `LivingDeadTests.cs` drive
   round-based passive behavior by calling `PassiveTracker.BeginRound(N)` directly, with no
   `CombatEngineClass` instance involved — every round-based passive test would instead need
   full `InitCombat`/`BuildRound` engine plumbing, and shared singleton state becomes a
   test-isolation hazard.
3. `PassiveTracker.RecordActivation` is called from inside `Passive` subclasses (e.g.
   `LivingDeadPassive.OnBeforeDeath`), which have no reference to `CombatEngineClass` at all —
   the mirrored round is what lets a passive record "what round did this happen in" without
   the passives system knowing the engine exists.

The cost of one mirrored `int`, updated from a single call site, is small and contained. The
cost of coupling `Passives` to the `Engine` singleton is not.

## What Comes Next

This holds as long as `PassiveTracker` needs to stay independently testable and decoupled from
`CombatEngineClass`. If that changes — e.g. `CombatEngineClass` stops being a singleton, or
round-based passive tests start needing full engine state anyway for other reasons — revisit
whether the mirror is still worth keeping separate.
