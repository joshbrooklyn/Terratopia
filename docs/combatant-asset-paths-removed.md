# Combatant Asset Paths Removed from JSON - June 16, 2026


## Problem

`spritePath` and `hitSoundPath` were stored as empty strings in `adventurers.json` and `monsters.json`. We decided only the UI should know about those. 

## Change

Removed `spritePath` and `hitSoundPath` from all combatant data.

### Files Modified

**`GameData/Adventurers/adventurers.json`** and **`GameData/Monsters/monsters.json`**
Deleted the `spritePath` and `hitSoundPath` key-value pairs from every entry.

**`GameEngine/DataClasses/Adventurer.cs`** and **`GameEngine/DataClasses/Monster.cs`**
Removed `string SpritePath` and `string HitSoundPath` properties.

**`GameEngine/DataClasses/CombatantSeed.cs`**
Removed `string SpritePath` and `string HitSoundPath` from the record parameters.

**`GameEngine/Engine/GameEngineClass.cs`** — `StartSkirmishCombat()`
Removed those arguments from both `CombatantSeed` construction sites (ally seeds and enemy seeds).

## What Comes Next

Combatants will become Godot Resources (`.tres` files), one per entity ID, holding display-layer data (sprite, sound). The UI will load them by entity ID at combat start. This work is deferred until assets exist.
