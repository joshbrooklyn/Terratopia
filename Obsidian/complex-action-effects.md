1. Actions can
	1. Revive
		1. "Revive a dead ally with 20% of their maximum Hp"
		2. "Revive all dead allies with 1 Hp"
	2. Use another action
		1. Targeting either the same targets or the user
		2. "If the target is below 50% Max Hp after this action resolves, repeat this action once"
		3. "Chain Lightning 1 - Has a 50% chance to strike the same target with Chain Lightning 2"
	3. Interact with keywords
		1. Add or remove keywords
			1. "After using this attack, it gains a random keyword. Resets outside of combat."
		2. Change the numerical effect of keywords (for just this action)
			1. "If you have less than 10% of your maximum Hp, the **Stoic** doubles its bonus."
		3. Change traits of the action based on keyword conditions 
			1. eg "If {keyword} has been added to this attack, increase the number of targets by 1"
			2. "If growth has been triggered on this ability 4+ times, it gains the fire element"
	4. Apply [passive](passive-effects) or [triggered](triggered-effects), or [status](status-effects) effects to the user or target(s). This covers:
		1. All primary stat changes (Max Hp, Max Tp, Pwr, Def, Spd)
		2. Any effect that persists on the user or target(s) that persists past the resolution of the action
	5. Modify data of the action itself
		1. "Every time you use this action, permanently increase its Tp cost by 1"
		2. "If this action hits 5+ targets, reduce its Tp cost by half"
	6. Modify values on the user
		1. "If this attack kills a target, regain 50 hit points"
		2. "If this attack hits an elemental weakness, gain 40 [evasion](evasion)"
	7. All of these effects can happen
		1. Before the action
		2. Before each individual hit of the action
		3. After each individual hit of the action
		4. After the action resolves

**Complex actions need to be able to do any of these things based on conditions:** (There might be some overlap here with [triggered effects](triggered-effects))
1. There are 9 basic conditions:
	1. Simple probability
		1. "This action has a 50% chance to apply P Dwn 3"
	2. Absolute and % Hp/Tp values on target
		1. "If you have less than 25% of your Max Hp-"
	3. Absolute and % Hp/Tp values on user
	4. [Status Effects](status-effects) on target
		1. "If you are suffering from a negative status effect-"
	5. [Status Effects](status-effects) on user
	6. [Elemental Resistances](elements-and-resistance) on target
		1. "If this action hits an element the enemy is weak to-"
	7. Did the target evade?
	8. Did the action critically hit?
	9. Did the action kill the target?