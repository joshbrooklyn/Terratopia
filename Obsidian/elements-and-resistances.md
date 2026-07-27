---
tags:
  - OriginalDesignDocs
---
1. List of Elements
	1. Fire
	2. Ice
	3. Lightning
	4. Void
2. [combat-actions]() can have one or more elements
3. [[combat-entities]] have resistances to each element
	1. Absorb - Regain Hp equal to 100% the damage of the attack
	2. Immune - Set Hp dmg of this attack to 0
	3. Resist - Reduce Hp dmg of this attack by 50%
	4. None - No changes
	5. Weak - Increase Hp dmg of this attack by 50%
4. All combat entities have none resistances to all elements by default
5. Effects can increase or decrease your resistance to an element, which moves it up or down on the above list
	1. Moving above absorb or below weak does nothing