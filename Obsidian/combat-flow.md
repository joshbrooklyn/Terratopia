---
tags:
  - OriginalDesignDocs
---
Note: Hard coded numbers should be easily editable wherever possible

See: [[trigger-execution-order]]
See: [[combat-entities]]
See: [[combat-actions]]
See: [[passive-effects]]
See: [how-enemy-actions-are-determined]()

1. Combat Starts
	1. Start of Combat effects trigger
	2. Turn Order is determined
		1. Each entity is assigned a number based on their speed +/-25%, and placed in an order from highest to lowest.
		2. If two or more entities tie they are given an order randomly
		3. Combat Entities get 1 turn by default.  If they get more than one turn, subsequent turns are put into the turn order at -(Speed Offset * num of turns beyond the first), where Speed Offset is specific per combat entity
		4. Turn order doesn't change after it has been generated.  If my speed changes it's not reflected until next turn order is generated.
	3. Round Start effects trigger
	4. Turn Start effects trigger
	5. If entity is stunned, skip their turn and decrement the stun counter by 1
		1. If the counter is 0, they are no longer stunned.
	6. Combat Entity chooses a [combat-action]() to use
		1. Check against the resource cost of the action, if it has any. If the user cannot pay the cost they cannot use the ability
	7. Combat Entity chooses targets for action
		1. If an ability has no valid targets it cannot be used
	8. Before attack effects trigger
	9. Before each hit effects trigger
	10. Action processes
		1. If the action does damage
			1. Check is the target [evaded](evasion). If yes:
				1. Reduce their evasion by 25%
				2. Skip to "After each hit effects"
		2. Action base power is calculated (User power * action power mod)
		3. Damage is calculated (See [damage calculation](damage-calculation))
		4. Check critical hit. If yes:
			1. Multiply damage by Crit Modifier %age
		5. Check [elemental resistances]() and modify dmg/healing
		6. Apply damage or healing. See [damage calculation](damage-calculation)
		7. After each hit effects trigger
		8. Repeat for each target
			1. If a target becomes invalid before the end of the execution of the action (usually because it's a multihit attack and they died after an earlier hit), the default behavior is that the remaining hits of the action retarget randomly among remaining valid targets. If there are no remaining valid targets, skip to "After attack effects trigger"
				1. I need to be able to toggle something to change this so that additional hits on an invalid target instead fizzle.  It's for a *very* dumb joke.
		9. After attack effects trigger
	11. End of Turn effects trigger
	12. If all of either side is dead, end the battle
		1. End of Battle effects trigger
	13. Repeat through turn order
	14. Round Ends
		1. End of Round effects trigger
		2. Simple status effects decrement by 1
			1. If any effect is at zero, it goes away
		3. Check Flee Conditions
			1. If (total of all enemy hp) < (total of all ally hp * 0.02 * round number), all enemies flee, ending the fight
				1. Fleeing monsters function identical to defeated ones
				2. If any monster in a fight cannot flee (bosses and some random enemies), none do
		4. Enemy power increases by 5%
			1. Both of these steps are skipped for boss fights