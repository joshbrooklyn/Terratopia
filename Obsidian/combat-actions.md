---
tags:
  - OriginalDesignDocs
---
1. Data contained in a combat action
	1. Name
	2. Text Description
		1. Generated programmatically whenever possible
	3. Flavor text
	4. What types of combat entities can use it
		1. Should support "Fighter", "Any Adventurer", "Enemy", "Boss", "Any Combat Entity"
	5. Power Modifier
		1. A %age modifier that is applied to the power stat of the entity using the action
		2. A 1.5 action used by a character with 100 power has a final result of 150 power
	6. Targeting Type
		1. Choose w/ replacement
		2. Choose w/out replacement
		3. Random w/ replacement
		4. Random w/out replacement
		5. Selective Multitargeting w/replacement
			1. "Up to 3 targets"
			2. The action gets a power penalty that grows larger the more targets you hit with the action
		6. Selective Multitargeting w/out replacement
	7. Can target enemies, allies, or both
	8. Can target living, dead, or both
	9. Num of attacks (can be same target or different, according to targeting type)
	10. Can have [keywords](keywords)
	11. Actions can have [special action behaviors](special-action-behavior). Those behaviors can happen:
		1. Before the action resolves
		2. Before each hit of the action
		3. After each hit of the action
		4. After the action resolves
	12. Actions can:
		1. Be unevadeable
		2. Be unredirectable
		3. Be unable to critically hit
		4. Affect Tp instead of Hp
		5. Add rather than subtract (Hp or Tp)
		6. Ignore the defense of the target
		7. Use a fixed power number (rather than a multiplier to the user's power)
		8. Do fixed dmg or healing ("Restore 100 hp")
		9. Only have certain valid targets ("Can only target entities at less than 50% max hp")
		10. Be used outside of combat
		11. Force you to include the user in the targets
2. Types of Combat Actions
	1. Adventurer
		1. Basic Attack
		2. Techs
		3. Job Ability
		4. Items
	2. Monsters don't have job abilities or items
3. Tech Specific Fields
	1. Tp cost
	2. Tier
		1. This is a hidden property, based on the Tp cost of the tech.  It is used to determine Ammo cost and QTE difficulty (see [[characters]])
		2. There are 3 tiers: Tp Cost 0-7, 8-13, 14+
		3. Changing the cost of a tech doesn't change its tier: It's always based on the base cost
4. Enemy Action Specific Fields
	1. Some enemy actions can be learned by [Weyland](characters), some cannot
		1. If they can be learned, they have a Tp value