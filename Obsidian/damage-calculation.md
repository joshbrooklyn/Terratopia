---
tags:
  - OriginalDesignDocs
---
1. Base Damage = (Action Power * 2) + (Attacker’s Level * 5)
2. Final Damage = (Base Damage / ((Defense + 128) / 128)) - (Defense / 2)
3. “Ignore defense” actions use the Base Damage value and skip the second step
4. "Fixed damage" actions skip the calculation entirely
5. Healing actions are "Ignore defense" by default but use the same calculation
6. Actions that target Tp need to use another calculation formula (because the scale of Tp is different) but it hasn't been decided on yet