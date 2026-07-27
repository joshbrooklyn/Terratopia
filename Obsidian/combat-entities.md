---
tags:
  - OriginalDesignDocs
---
1. Combat Entities have
	1. Level
	2. Primary Stats
		1. Power
		2. Defense
		3. Speed
		4. Hp
	3. Growth Rate for each primary stat (How much it increases on level up)
	4. [Elemental Resistances](elements-and-resistances)
	5. Critical Hit Chance - %age chance to do extra damage or healing
		1. 0 by default
	6. Critical Hit Mod - %age increase to damage and healing on crit
		1. 0.5 by default
	7. [Evasion](evasion)
	8. Can have [passive-effects]() added to them
	9. Status Effect immunities
		1. When a status effect is applied to someone with immunity to that status, no status is applied
2. Adventurer Specific
	1. Tp - Used to use Techs. Has a growth rate like Hp
	2. Perks - a type of passive effect gained on level up
	3. Gear - a type of passive effect gained from loot sources
	4. Max Gear - How many pieces of gear an adventurer can equip
	5. Fight Command - A type of [combat action](combat-actions%20#.md)
	6. Job Ability - A type of [combat action](combat-actions%20#.md) specific to their job
	7. Techs - A type of [combat action](combat-actions%20#.md), usually specific to their job. Gained from loot sources
	8. Max Techs - How many techs an adventurer can know at once
	9. Items - A type of [combat action](combat-actions%20#.md), gained from loot sources. Not job specific.
	10. Inventory size - Max num of Items they can hold