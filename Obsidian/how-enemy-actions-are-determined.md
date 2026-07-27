---
tags:
  - OriginalDesignDocs
---
1. Enemies have an ordered list of [[combat-actions]]
2. Depending on the enemy, they can execute those actions:
    1. In Order
	2. Randomly with Replacement (Pick a new entry from the list each time)
	3. Randomly without Replacement (Pick entries from the list until every entry has been picked once, then refresh)
3. Enemies can have their next action overridden by a condition.  When that occurs, they use a specified action, then return to their original pattern
	1. Example: When reduced below 50% of their Max Hp for the first time, this enemy uses "Heal" on itself with its next action, then returns to its normal behavior
4. Bosses can use one or more additional patterns, and switch between them based on conditions
	1. Example: A boss has 4 combat patterns: default (which it starts on), fire, ice, and lightning.  When it is hit with an attack with one of those elements. it switches to that combat pattern until hit by another element.
		1. Enemies do not lose their place in their previous combat pattern when switching to a new combat pattern
5. How Enemy Actions Target
	1. Once an enemy has selected their action, any targeting type involving choice means the enemy chooses randomly among all valid targets.