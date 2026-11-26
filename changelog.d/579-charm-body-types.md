---
version: 1.2.3
section: Fixes
---
- **Charms take the creatures their spell says** (#579; from DOL's public database, checked against period class guides).
  The Minstrel's Melodies now charm humanoids, then add animals, insects and magical creatures song by song, and the
  Mentalist's Illusions charms add animals, insects, magical creatures and undead tier by tier until Dream Enemy charms
  anything. Before, most Minstrel songs charmed anything and most Mentalist charms only humanoids. Control Reptile now
  charms only reptiles (it charmed anything) and Superior Call(Insect) only insects. A world from an earlier build is
  fixed once at start-up, only where a spell still holds the old value; each change is logged as
  `CHARM_BODY_TYPE_REPAIRED`.
