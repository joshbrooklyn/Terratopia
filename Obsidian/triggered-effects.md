---
tags:
  - OriginalDesignDocs
---
Triggered effects do something in response to something else. Usually, but not always, in combat.
1. Types of triggers (I'm pretty sure this list is exhaustive)
	1. Start of Combat
	2. Start of Round
	3. Start of Turn
	4. On Hp change
	5. On Tp change
	6. On Action execution
	7. On Target Hp change
	8. On Target Tp change
	9. On Critical Hit
	10. On Target Death
	11. On Evade
	12. On Status Effect apply
	13. On Item Use
	14. On Death
	15. On Elixir Use
2. Triggered Effects can require additional conditions beyond the trigger: eg "When your Hp goes down", "When a positive simple status effect is applied to you", "When you use an action with the fire element"
3. Triggered effects can exist on:
	1. Combat Entities
		1. "At the start of your turn, regain 15 Hp"
	2. Battles
		1. "At the start of the next battle, all friendly combat entities gain P Up 1"
	3. Dungeons
		1. "While in {dungeon}, whenever you spend Tp, lose Hp equal to the Tp spent"
	4. Runs
		1. "All combat entities gain 5% crit chance at the start of the round. Resets after battle."
4. Triggered Effects can
	1. Use combat actions
		1. Using the power of the entity with the trigger or a set power
		2. Possible targets:
			1. The entity with the trigger
			2. The entity that caused the trigger
			3. All Monsters
			4. All Adventurers
			5. All Combat Entities
	2. Apply [passive](passive-effects), other triggered and [status](status-effects) effects
	3. Change the values of attacks that target the entity with the trigger
		1. "When you are targeted with an action that applies a positive simple status effect, increase its duration by 1"
		2. Maybe this could be treated instead as a "Apply a status effect"
5. Triggered Effects have duration
	1. Num of rounds
	2. Num of turns of the entity the effect is on
	3. Num of triggers
	4. Num of battles
	5. Num of dungeons
	6. Until the thing that provides it is unequipped
	7. End of Run