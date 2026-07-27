---
tags:
  - OriginalDesignDocs
---
Everything that affects an entity that is not an action or something that is triggered by another effect
1. Passive Effects can
	1. Change primary stats of the affected entity (Hp, Tp, Pwr, Def, Spd)
		1. % or fixed num
	2. Apply or remove simple status immunities
	3. Change [elemental resistances](elements-and-resistances)
	4. Apply permanent [status effects](status-effects) (undispellable, uncancellable, lasts until the passive effect goes away) - "Gain D Up for the next 3 battles"
	5. Change the [loot system](loot-system)
	6. Interact with the [character](characters)'s innate power (see [character specializations](character-specializations))
		1. "Akio's timed hits use a 30% wider window"
		2. "Increase Weyland's number of learned enemy traits by 1"
	7. Interact with party [elixirs](elixirs)
		1. Increase your maximum number of elixirs by 1
2. Types of Passive Effects (shared with [triggered effects](triggered-effects))
	1. Run Modifiers
		1. Applied by the [Doubt Gauge](doubt-gauge)
	2. Dungeon Modifiers
		1. "**Dungeon Modifier** Treasure Horde - Each fight drops an extra piece of loot"
		2. "**Dungeon Modifier** Exercise Regimen - All enemies in this dungeon spawn with 20% more Max Hp"
	3. Room Modifiers
		1. "All monsters in this battle have 30% more defense"
		2. Usually applied by the event of a room
	4. Enemy Traits
		1. "At the beginning of battle, gain P Up, D Up, or Spd Up 2, at random"
	5. Character Specializations
		1. Rewarded by interacting with the [Doubt Gauge](doubt-gauge)
		2. Improves characters' innate power
	6. Perks
		1. Gained on level up
		2. "**Perk (White Mage)** Nourishing Nature - Increase the power mod of your healing techs by 0.5"
	7. Gear
		1. Gained from any loot source. Enemies cannot have gear
		2. +30 Max Hp
	8. Curses & Boons
		1. Catch all for every other persistent effect.  Applied primarily by enemy actions or events in dungeons  
3. Can be applied by:
	1. Run
		1. Applies for the entire run
		2. "**Doubt Gauge** - Slipperiness - All enemies gain 10 [evasion](evasion) at the start of their turn"
		3. "**Doubt Gauge** - Frailty - All adventurers gain 50% less Max Hp on level up"
	2. Dungeon
		1. Applies for an entire dungeon
		2. ""**Dungeon Modifier** Treasure Hoard - All fights drop an additional piece of loot"
	3. Events in Dungeons
	4. Something the combat entity has equipped
		1. "**Enemy Trait** - Wooly Coat - Increase all [elemental resistances](elements-and-resistances) by 1"
		2. "**Perk (White Mage)** Nourishing Nature - The power modifier of your healing techs increases by 50%"
		3. "**Gear** - Gem of Swiftness - Speed +2"
	5. An action that an entity uses
		1. "**Combat Action** Gouge - 100% Power Attack vs 1 opponent.  Permanently reduces the target's Hp by 5"
			1. Would appear on the character as "**Curse** Wound - Reduce your Max Hp by 5"
4. Can be applied to
	1. Run
		1. 
	2. Dungeon
	3. Battle
	4. Combat Entity
5. Have duration
	1. Num of rounds
	2. Num of turns of the entity the effect is on
	3. Num of battles
	4. Num of dungeons
	5. Until the thing that provides it is unequipped
	6. Until the end of the run
6. Can have the following effects
	1. Modify values on a combat entity (Hp, Tp, Max Hp, Evasion, Crit Chance, Crit Modifier, Elemental Resistances)
7. Cannot be removed except by expiring
8. See [status effects](status-effects)