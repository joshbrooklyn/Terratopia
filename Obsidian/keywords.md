---
tags:
  - OriginalDesignDocs
---
Keywords are reusable effects that actions can have. They come natively on some actions, and can be added or removed by other effects. None of these names are final.

1. List of Keywords
	1.  Exhaust
	    1. Usable once per combat - After an Exhaust tech has been used, it cannot be used again until the end of the combat
	2. Single Use
		1. Destroyed after one use
	    2. (Almost) All Items have the single use keyword
    3. Growth
	    1. After each use, increase the power modifier of this action by 10% until the end of combat.
	4. Teamwork
		1. This ability gains a power modifier equal to (5% * the total number of actions with the Teamwork keyword used by your allies this fight).
	5. Engage
	    1. +50% to the power modifier of this attack if its target is at >=75% of their maximum Hp.
    6. Cruel
		1. +50% to the power modifier of this attack if its target is at <=25% of their maximum Hp. 
    7. Empowered
	    1. +50% to the power modifier of this attack if its user is at >=75% of their maximum Hp.
	8. Stoic
		1. +50% to the power modifier of this attack if its user is at <=25% of their maximum Hp.
2. Power Increasing Keywords cannot increase the power modifier of an action by more than the action’s base power or 50%, whichever is lower.  Eg: a 40% Power Mod Action caps at 80% Power. A 150% Power Mod Action caps at 200% Power)
3. If two keywords are active at once, each one has its own cap. Eg: a 80% Power Mod Tech with Engage and Stoic can go up to 180%
4. Power Increasing Keywords have no effect if the base power modifier of the action is 0%.
5. Adding a keyword twice has no effect