---
version: 1.2.3
section: RvR
---
- **Gamebot groups open a fight with their area mez** (#531 step 2a). At first contact in the open field, a group's
  area mezzer (not its main assist) casts its area mez on the enemy pile first, at the spot that catches the most
  enemies, healers counting extra. The rest of the group then kills whoever is still awake. The opener follows the
  server's own range, sight, power and mez-immunity rules, and is spent once a mez lands or 8 seconds pass. A new fight
  20 seconds later gets a new opener. Autonomous gamebots only; companions and keep fights are unchanged.
