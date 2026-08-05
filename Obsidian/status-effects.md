1. A category of commonly reoccurring combat effects that have some extra rules associated with them.
2. Are Simple or Complex
	1. Simple status effects decrement at the end of the round
	2. Complex effects are everything else
3.  Are Positive or Negative
	1. Simple status effects exist in pairs: one positive, one negative.  If you have a simple positive status effect, and the complimentary negative status effect is applied, your positive effect is canceled out and no negative effect is applied. (Power up cancels out Power down). The duration of the two effects don't matter for cancelling out.
4. Can be purged off entities according to categories (Purge Simple Positive, Purge All Negative, Purge All)
5. List of Status Effects
	1. Simple Effects (Exhaustive)
		1. Power Up/Power Down - Increases/decreases Power by 30%
		2. Defense Up/Defense Down - Increases/decreases Defense by 30%
		3. Speed Up/Speed Down - Take your turn at the beginning/end of the turn order.  If multiple characters have Speed Up/Dwn, their turns are sorted by randomized speed, identically to normal turn order (see [combat-flow](combat-flow)) 
		4. Regen/Drain - Gain/Lose 10% of your max Hp or Tp at the start of every round
			1. Implemented in CombatEngine as its own parallel mechanic (RegenDrainSpec/RegenDrainStat/RegenDrainType), mirroring Power/Defense/Speed Up/Down rather than being folded into the same enum - see [docs/regen-and-drain.md](../docs/regen-and-drain.md).
	2. Complex (Not exhaustive)
		1. Immune - Take 0 damage the next time you would take Hp damage, then reduce Immune by 1
		2. Debilitate - Next attack that hits you that has an element, reduce your lowest resistance to that attack by one, then reduce Debilitate by 1
6. [Passive Effects](passive-effects) can change the effects of Status Effects: eg Power Up gives +50% instead of 30%