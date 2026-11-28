---
version: 1.2.3
section: RvR
---
- **Gamebot groups use their big realm abilities in open-field fights** (#531 step 2c-1). This covers autonomous gamebots
  in the frontier and the battlegrounds. Companions, bots in a player's group, keep fights and raids are unchanged.
  - An Albion Minstrel's group charges in under **Speed of Sound** when it has picked its target and still has 800-2,500
    to cover.
  - A caster or healer who is interrupted a second time in a fight uses **Mastery of Concentration** and casts through
    the hits for 15 seconds.
  - A Cleric drops **Bunker of Faith** on the group once a group mate has fallen.
  - Each ability works as it does for a player (the same effect, range and duration) and then waits out its 30-minute
    timer, so it comes once a fight. The bots buy them in their realm ability builds: the Minstrel buys Speed of Sound
    after Purge, casters and healers buy Mastery of Concentration once they have Augmented Acuity 3, and the Cleric buys
    Bunker of Faith after Purge.
  - `RvrRealmActives`, `UT_RvrRealmActives531`; the RvR fight rehearsal prints `open_field_realm_abilities`. **Tests only.**
