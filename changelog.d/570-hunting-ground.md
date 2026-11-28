---
version: 1.2.3
section: Bots and companions
---
- **Solo gamebots keep a hunting ground** (#570).
  - A solo gamebot's next camp is now far more often in the zone it already hunts or a neighbouring one, so it spends
    its time fighting instead of crossing the realm between camps. Crowded camps are still left for quiet ones first.
  - When its camp's own monster is gone, it hunts the other monsters around the camp that suit its level instead of
    leaving.
  - It pulls the monster standing apart from the others first, as parties already do.
  - Infiltrators, Shadowblades and Nightshades prefer blue monsters to yellow ones, as period guides advised rogues.
    Healers, Shamans, Druids, Bards, Minstrels and the archers prefer blue too, by our own choice. Everyone else keeps
    yellow first. No class fights anything harder than before.
  - Once most of its kills con green, it moves up to a harder spot nearby.
  - Groups, realm raids, dungeons and Darkness Falls are unchanged, and companions are not touched. Switch:
    `bot_hunting_grounds` (on).
