# Raid sizes

How big each automatic realm raid is, and where the numbers come from (owner, 2026-11-04: raid sizes as they were in
the era). Until then every dragon, epic dungeon, Summoner's Hall and Darkness Falls raid needed 200 level 50s (Krondon,
Trollheim and Tur Suil 160; the others 96-120) and recruited up to 300, and the raids of 2026-11-04 ran with 148-277.

## The rule

- **Full size:** the period's *comfortable* raid at that encounter, not its minimum. It is the encounter's
  `SuggestedBots` in `AutonomousRealmRaid.cs`. A small roster lowers it to half the realm's free level 50s (never
  under 40), and `realm_raid_minimum_bots` replaces it when set.
- **Win-rate sizing** (`RealmRaidRecruitmentPolicy.Size`): every raid starts at 100% of its full size. Each win at that
  encounter trims 5 points; each defeat or time-out adds 10.2. The size settles where the encounter is won about two
  times in three.
  - **Floor 70%:** about the period's "possible, but tough". Apocalypse was "possible with good groups, but tough"
    at 80-90 against a comfortable 90-100.
  - **Ceiling 150%:** room for bots that play worse than 2003 players. Caer Sidi at 150 is about the 130-140 that
    turned up at the time. Never past the realm's 300.
- **Recruit cap:** a quarter above what the raid must stage, in whole parties (RecruitCap of the full size, scaled by
  the same percentage).
- **Short start and hub departure:** half of what the sized raid needs, at least three parties (24). The old minimum
  of 40 was 67% of a 60-bot dragon raid and 95% of one trimmed to 42.
- **Epic Special Forces:** one party per 64 raiders, rounded, one to three. That is about the 12% that their 24 were of
  a 200 raid. A dragon raid of 60 gets one party, Caer Sidi at 100 two, a raid of 160 or more all three.
- **Forced raids** (launcher) are unchanged: 200 to go in, 300 reserved. Their results are not part of the record.

## Sizes and sources

Most of the evidence is from Freddyshouse, the European DAoC community forum, in 2003-2004. Thread dates are given.
"Unverified" means no period count was found for that encounter, and the size follows a comparable boss.

| Raid | Was | Now | Period evidence | Source |
|---|---|---|---|---|
| Golestandt (Albion dragon) | 200 | 60 | Feb 2003: "a number of players ranging between 125 and 160" in the guide author's four kills. Aug 2003: done with 17-24, "The ideal amount is 24 people", "It does take 2-3 hours to kill him with 23". | [Albion epic raid guide, Feb 2003](https://forums.freddyshouse.com/threads/to-anyone-planning-epic-raids-in-albion-spoilers.115688/), [How many ppl to kill the dragon, Aug 2003](https://forums.freddyshouse.com/threads/how-many-ppl-to-kill-the-dragon.139907/) |
| Cuuldurach (Hibernian dragon) | 200 | 60 | May 2003: "120 brave Hibernians", but "about 30 people attack at any one time" and "5fg attacking the dragon"; the rest held the four entrances against adds. | [How to kill Cuuldurach, May 2003](https://forums.freddyshouse.com/threads/how-to-kill-cuuldurach.124821/) |
| Gjalpinulva (Midgard dragon) | 200 | 60 | Unverified. A Nov 2003 raid killed an unnamed female dragon ("She went down quick and in first try"), likely Gjalpinulva, with 3 groups and an exit-guard group (about 32). | [Lubricador's 3 FG Dragon Raid, Nov 2003](https://forums.freddyshouse.com/threads/lubricador%C2%B4s-3-fg-dragon-raid.159276/page-2) |
| Caer Sidi (Apocalypse) | 200 | 100 | Feb 2003: "a minimum of about 100 people is required", "The lowest number I have killed Apocalypse with on a live server is 105". Jul 2003: "With 80-90 I believe Apocalypse is possible with good groups, but tough. 90-100, Apocalypse should be straight-forward", "Almost everytime 90+ ppl show up", one raid of 130. | [Albion epic raid guide, Feb 2003](https://forums.freddyshouse.com/threads/to-anyone-planning-epic-raids-in-albion-spoilers.115688/), [Caer Sidi Raid, Jul 2003](https://forums.freddyshouse.com/threads/caer-sidi-raid-sunday-20th-july.135873/) |
| Galladoria (Olcasgean) | 200 | 100 | May 2003: killed by 140 after a 340-strong attempt failed; "I think it can be done with 100 ppl quite nicely". | [Olcasgean has been defeated!, May 2003](https://forums.freddyshouse.com/threads/olcasgean-has-been-defeated.125921/) |
| Tuscaran Glacier (King Tuscar, Queen Kula) | 200 | 100 | May 2003: "we zerged King Tuscar and Queen Kula about a week ago with ~100 ppl", "with 100+ people". Jul 2003 sign-up: "4FGs would be nice, 6 awesome". | [Olcasgean has been defeated!, May 2003](https://forums.freddyshouse.com/threads/olcasgean-has-been-defeated.125921/), [Tuscaren Glacier Raid, Jul 2003](https://forums.freddyshouse.com/threads/tuscaren-glacier-raid-tuesday-15-07-03.135818/) |
| Summoner's Hall (Grand Summoner Govannon) | 200 | 64 | Feb 2003: "approximately four to five full groups" (32-40). 64 because each realm's raid there also fights the other realms' raids on the way in. | [Albion epic raid guide, Feb 2003](https://forums.freddyshouse.com/threads/to-anyone-planning-epic-raids-in-albion-spoilers.115688/) |
| Darkness Falls (Legion) | 200 | 64 | Feb 2003: "as few as about three groups", "a group of about fifty players" has it easier. Feb 2004: "4 fg took him out", "3-5fg", "first time I think we were 100 or something". 64 for the same contest as Summoner's Hall. | [Albion epic raid guide, Feb 2003](https://forums.freddyshouse.com/threads/to-anyone-planning-epic-raids-in-albion-spoilers.115688/), [Time to kill Legion?, Feb 2004](https://forums.freddyshouse.com/threads/time-to-kill-legion.4424/) |
| Krondon (Orylle) | 160 | 64 | Feb 2003: Orylle and Orshom Brong need "at least 50 or more players". | [Albion epic raid guide, Feb 2003](https://forums.freddyshouse.com/threads/to-anyone-planning-epic-raids-in-albion-spoilers.115688/) |
| Crystal Cave (Xanxicar) | 120 | 120 | Feb 2003: "a raid of well over a hundred players". Jul 2005: "i seem him wipe 5 fg". | [Albion epic raid guide, Feb 2003](https://forums.freddyshouse.com/threads/to-anyone-planning-epic-raids-in-albion-spoilers.115688/), [Avalon city dragon raid, Jul 2005](https://forums.freddyshouse.com/threads/avalon-city-dragon-raid-crystal-caves.65679/) |
| Avalon City (Duraek) | 120 | 64 | Unverified: no count found. Sized like the other Shrouded Isles dungeon finals (Orylle). | - |
| Hall of the Corrupt (Aidon, Lord Gildas) | 120 | 48 | Feb 2003: "Archwizard: good for about two groups", likely Aidon. Lord Gildas: no count found. 48 for two finals and the way in. | [Albion epic raid guide, Feb 2003](https://forums.freddyshouse.com/threads/to-anyone-planning-epic-raids-in-albion-spoilers.115688/), [The Halls of the Corrupt, Apr 2003](https://forums.freddyshouse.com/threads/the-halls-of-the-corrupt.124084/) |
| Trollheim (Nosdoden) | 160 | 64 | Unverified: no count found for Nosdoden. Sized like the other Shrouded Isles dungeon finals. | [Nosdoden Beckons, Dec 2003](https://forums.freddyshouse.com/threads/nosdoden-beckons.164875/) (no count) |
| Iarnvidiur's Lair (Iarnvidiur) | 120 | 64 | Unverified for the boss. May 2003, a named in the lair: "he wiped about 4fg of us, we beat it with 4fg later on". | [Lair vs Iarnvidur vs Trollhiem?, May 2003](https://forums.freddyshouse.com/threads/lair-vs-iarnvidur-vs-trollhiem.127197/) |
| Dodens Gruva (Spiritmaster Aros) | 96 | 40 | Jan 2003: "At guildevent we did Aros with 3fgs", after "3 fgs and got our butts kicked". | [Dodens Gruva Named mobs, Jan 2003](https://forums.freddyshouse.com/threads/dodens-gruva-named-mobs-etc.110133/) |
| Tur Suil (Balor) | 160 | 40 | Dec 2003: "After all you only need 2fg's"; the raid had 3 groups. | [Balor/Quest Raid, Dec 2003](https://forums.freddyshouse.com/threads/balor-quest-raid-today-friday.163060/) |
| Marfach Caverns (Black Lady, Caimheul, Avartack) | 64 | 64 | Unverified: no count found. Unchanged. | - |

## What the numbers mean for bots

The period sizes are for 2003 players. Two things pull the other way, and win-rate sizing is there to absorb both:
- **Easier over time:** dragons came down from 125-160 in early 2003 to 17-24 by August 2003 as gear and tactics
  improved, and to one group or less by 2008. The 60 sits between: a dragon won with 50 present on 2026-11-04.
- **Bots may play worse:** the raid grows up to 150% when it keeps losing.

None of this has been played in game yet. `REALM_RAID_SIZED` in the server log shows each raid's size and why.
