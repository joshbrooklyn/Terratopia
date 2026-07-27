1. Each character has a fundamental variation on the formula of the game (called an Innate Power), and plays slightly differently
2. ???'s are TBD
3. Characters can:
	1. Be any of the four [jobs](jobs)
	2. Appear as Midbosses
		1. Midboss fights are a gauntlet of fights (3) with a boss at the end.
		2. Characters each have a unique boss fight, and an effect they apply to all of the fights in their gauntlet. These will be listed under "Hand Effects", but architecturally they'll probably be the same as any other dungeon effect
	3. Appear as the Final Boss
		1. This is another enemy, but also comes with an effect that they apply across the entire run.  This will be listed below under "TDK Effects." They're very subject to change but they're big effects so better to write them out now
	4. Every character appears as one of these things (4 jobs, Midboss, Final Boss) in every run
4. Characters can improve their Innate Powers by clearing runs of the game on successively higher difficulties
	1. See [character specializations](character-specializations)
5. List of Characters
	1. **Akio**
		1. Innate Power - Timed Hits
			1. When making a basic attack, there will be a short window during the animation where pressing the confirm button increases the damage of the attack by 30%
			2. When using a tech that heals/damages Hp/Tp, Akio is presented with a short QTE (eg https://youtu.be/9XTozylrz-Q?si=xfYhhdA3ntDk3cCd), where performance on the QTE is rated Bad/Okay/Good/Perfect, which multiplies the dmg/healing of the action by 60/90/120/150%
				1. This %age is multiplicative with all over modifiers, applied at the very end
				2. Each QTE has three different difficulties based on the Tier of the tech (See [combat actions](combat-actions) about tiers)
			3. Job specific differences
				1. Different jobs have different QTEs and attack timings
		2. Hand Effects
			1. Increases the power of all enemies in their gauntlet by 15%
		3. TDK Effects
			1. Timed defenses - For the entire run, the player must press the confirm button within a short (~6 frames) window when taking damage to avoid taking 30% more damage than normal 
	2. **Remy**
		1. Innate Power - Ammo
			1. Remy starts with higher power (+50% base and growth).  Base Power and Power Growth are determined by her job
			2. Remy doesn't have a TP stat.  She powers her techs with an Ammo resource, which is plentiful but harder to restore.  The ammo cost of a tech is based on the tier: 2 ammo/3 ammo/4 ammo (see [combat actions](combat-actions) for tier info)
			3. Using Remy's basic attack costs 1 ammo. If Remy has no ammo, her basic attack command changes to "Scavenge", which restores 1-3 ammo (33% chance of each)
			4. Ammo is restored and max ammo grows on level up
			5. Anything that restores Tp has no effect on Remy (see one exception below)
			6. Job Specific Differences
				1. The Black Mage's "Charge" Job Ability instead restores Ammo for Remy.  Haven't decided the formula yet.
				2. Remy's Maximum Ammo  and Ammo Growth is determined by her job (as a function of the base Tp and Tp growth of the job)
		2. Hand Effects
			1. Apply "Exhaust" to one random tech (that doesn't already have it) for each Adventurer. No effect if every tech has exhaust.
		3. TDK Effects
			1. Limited Uses - All techs across the entire run have a limited number of uses before they are destroyed 
				1. How this number is determined?
			2. To make up for this, more techs drop as loot. Exact formula TBD
	3. **Weyland**
		1. Innate Power - Blue Mage
			1. After winning a fight, Weyland can learn one trait from an enemy in a battle
				1. Weyland starts with one slot to learn any enemy trait and can gain more later
				2. Some traits cannot be  (Because they would be worthless or too powerful)
			2. Later on, Weyland can learn enemy actions as well
				1. All enemy actions have a Tp cost and have the Exhaust [keyword](keywords) by default
			3. No job specific differences
		2. Hand Effects
			1. ???
		3. TDK Effects
			1. All enemies get an additional Enemy trait at random
				1. This pool will need to be restricted in some way to keep it reasonable/sensible. Maybe just Weyland's blue mage pool?
	4. **Cedric**
		1. Innate Power - Spirit
			1. Cedric has a Spirit Meter that can be redeemed outside of combat for various effects. The meter can hold 2 charges.
			2. How the meter charges and what it does vary based on job
				1. Fighter - Tactics - All allies gain P Up 2 at the start of next battle
					1. Meter charges when taking damage
					2. 100% meter charge is taking 100% of his current Max Hp in damage. If his max Hp changes, it doesn't change accumulated meter, only the rate at which further meter is accumulated
				2. Rogue - Keen Eye - Reroll any draft choice once (chests, perks)
					1. Meter charges when killing opponents
					2. 100% meter charge is 2 kills
				3. White Mage - Banish Evil - Instantly win a non-elite fight. You still get loot.
					1. Meter charges when restoring HP in combat
					2. 100% meter charge is 120% of the total max HP of all allies.  If max Hp changes, it doesn't change accumulated meter, only the rate at which further meter is accumulated
				4. Black Mage - Meditation Ritual - Restore 50% of all allies maximum TP
					1. Meter charges when dealing damage
					2. 100% meter charge is an amount of damage dependent on Cedric's level, about 5x his expected average Max Hp
		2. Hand Effects
			1. The gauntlet consists of 5 fights instead of 3
		3. TDK Effects
			1. The threshold for enemies to run is tripled (meaning enemies flee more readily)
			2. For each fight that ends in a flee, the stats of all bosses (maybe just Hp?) are increased by X%
	5. **Kostaki**
		1. Innate Power - **Morph** & **Gene System**
			1. Kostaki has a 5th command in combat: Morph.  Using this command takes their turn and transforms them
			2. Kostaki has a Morph meter, visible in battle and their character sheet.
				1. It starts at 0 and increases by 1 at the end of every fight
					1. What's the maximum? 10?
				2. Morphing in combat uses up the entire meter
				3. Kostaki stays morphed for 1 turn per charge of the meter.
					1. Kostaki reverts at the start of their turn if they're out of morph turns
				4. Unmorph at the end of combat
				5. While morphed, Kostaki has X% increased Pwr & Def (maybe?  Maybe this isn't necessary?)
			3. **Genes**
				1. At the beginning of the run (as a submenu of the menu where you assign characters to jobs), Kostaki has a list of genes, of which you must pick 3. Genes have no effect unless Kostaki is morphed
				2. Sample genes:
					1. Force - Kostaki gains an additional X% Pwr & Def
					2. Visage - On morph, Kostaki applies P Dwn 2 to all enemies
					3. Burst - On morph, Kostaki gains P Up & D Up 4
					4. Siphon - Regain 5% of all damage dealt as Hp
					5. Rasp - Regain 1% of all damage dealt as Tp
					6. Fire/Ice/Lit - All attacks made while morphed gain that element
					7. Gross - +100% Current and Max Hp. When unmorphing, Hp is set to how much Hp you had when you morphed, or your current Hp, whichever is lower.
					8. Gaseous - Kostaki gains 50% evasion at the start of every round
		2. Hand Effects
			1. None
		3. TDK Effects
			1. Evolving Monsters - Each dungeon has a Monster Trait buff associated with it (themed around the monsters of the dungeon).  Once you clear that dungeon, the buff applies to all enemies for the rest of the run
	6. **Mara**
		1. Innate Power - Boost Points
			1. Mara has a reserve of Boost Points (BP), which can be spent to increase the damage or healing of any action by 25% per point spent
			2. You can spend up to 3 points at a time (for +75% dmg or heal)
			3. You gain 1 BP at the beginning of every round (including the first), except when you spent 1 or more BP the round before. Leftover BP at battle end are lost
		2. Hand Effects
			1. All enemies have +30% power when targeting opponents with 50% or less of their maximum health
				1. This is a little low on flavor
		3. TDK Effects
			1. Normally, nothing, but as a stretch goal
				1. Some variation on the break system. Every boss gets an additional action that they broadcast the use of, and must be hit a certain number of times, which interrupts the attack and causes Stun 1.
	7. Basque (Stretch goal)
		1. Maybe like a FF12 gambit system?  Character has infinite TP, but cannot be controlled, and needs to be given targeting parameters outside of combat
