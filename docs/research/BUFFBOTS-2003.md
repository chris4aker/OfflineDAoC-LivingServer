# Buffbots in DAoC, 2002-2004 (patches 1.50-1.65), and how this server handles them

Research for Living Server 1.2, "No Lifer buffbots" (owner, 2026-11-15). Written 2026-11-15.
Sources are dated forum posts, patch notes and this server's own data and code. [P] = a primary
source from the period. [L] = a later source. [S] = this server (database or code). Anything not
backed by a source is marked as an assumption.

## 1. Short answers

| Question | Answer | Confidence |
|---|---|---|
| Classes per realm | Albion **Cleric** (Enhancement), Midgard **Shaman** (Augmentation), Hibernia **Druid** (Nurture) | high for Cleric and Shaman, medium for Druid |
| Concentration buff range in 2003 | **No range limit.** Players were asking Mythic to add one in March 2003 | high |
| When a range came in | Mythic's **Classic servers (12 July 2005)**: "All concentration buffs will have a range limit of 5,000 units and will be available only to the buffer and his groupmates." I found no 2002-2004 patch that added a range on the normal servers | high for the 2005 rule; no evidence of an earlier one |
| Buffing people outside your group | **Allowed** in 2003, and common: guild buffbots buffed whole groups | high |
| Typical practice | A second paid account, run with DAoX or a second PC, **parked AFK in the portal keep / a keep / town**, or following its main. The main came back for timed buffs and after a death | high |
| Mythic's stance in 2003 | No rule against them. Players said Mythic ignored them or even meant to support them | medium (player claims) |
| PvP | Buffbots were the main complaint about frontier solo fights. Parked buffbots sat in portal keeps, safe behind the guards | high |

**What this means for "parks in town":** under the 2003 rule a parked buffbot keeps its
concentration buffs on its main at any distance, so parking at a bind is the authentic practice.
This server's own code limits concentration buffs to 5,000 units for everyone (section 5). The
buffbots therefore get the 2003 rule as a setting (`bot_nolifer_buffbot_era_conc_range`, on by
default). Timed buffs still need the main within 1,000 units. So the buffbot **follows** wherever
it safely can and **parks** only where it would not follow. A parked buffbot recasts timed buffs
when its main comes back to it. That happens on its own when the main releases to its bind after
a death, because the buffbot parks at the main's bind.

## 2. Which classes

- **Albion: Cleric, Enhancement.** "Killed him with 1 group and 2 extra buffbots ... 2extra buffbots
  both 50enh with MotA2" (Allakhazam, Grey Noble, Aug 29 2003) [P]. In the Mar 2003 FreddysHouse
  thread, the classes named are clerics and the friar's buffbot. Graknak: "A Cleric who ONLY gives
  out buffs is a buffbot" (2003-03-26) [P].
- **Midgard: Shaman, Augmentation.** "so better use buffbot shamans with 47 aug" (FreddysHouse, GoV
  dragon raid, 2003-11-07) [P]. Census: Clerics and Shamans were the top two classes on US servers
  in June 2003. Posters explained it with "everybody has a buffbot" (FreddysHouse "Class census for
  june", 2003-06-19) [P]. The owner's request says "cleric or healer". The Shaman is Midgard's
  second healing class, and on this server only the Shaman's Augmentation line has the spec
  str/con, dex/qui and acuity buffs [S]. Healer Augmentation has haste, and its other spec buffs
  are self only [S]. So Midgard's buffbot is a Shaman.
- **Hibernia: Druid, Nurture.** Buffer druids ran Nurture high ("Nurture/Regrowth (spec buffs)",
  Nurture 44 / Regrowth 31, ERA-BUILDS-1.65.md, 2003 threads) [P]. An Aug 2003 Hibernian group
  setup lists "Buffbot, Bard, Nature/Regrowth Drood, ..." with the buffbot as a separate slot
  (FreddysHouse "What is a good tactic to..", 2003-08-14) [P]. The class is not named, but the
  Druid is the only Hibernian class with the full set of spec buffs [S]. Medium confidence.

Builds used (a pure buffbot maxes the buff line). Points left over go to the heal line, so it can
heal itself:
- Cleric: Enhancement 50, then Rejuvenation, Smite 1.
- Shaman: Augmentation 50, then Mending, Subterranean 1. The era post's 47 Aug already reached the
  top dex/qui. 50 is the same set plus the level-48 resists, which are group only.
- Druid: Nurture 50, then Regrowth, Nature 1.

## 3. Which buffs (this server's spell data, `Spell` / `LineXSpell` in the 0.37b world database) [S]

All of these are single-target ("Realm"), concentration, no duration, range 1,000, unless marked.

| | Cleric (Enhancement + Guardian Angel spec) | Shaman (Augmentation + spec) | Druid (Nurture + spec) |
|---|---|---|---|
| Base AF | Aura of Deflection 52 (conc 15) | Guardian's Lesser Protection 52 (15) | Superior Skin of the Redwood 55 (16) |
| Base str / con / dex | 50 / 44 / 48 (19 / 16 / 18) | 44 / 47 / 48 | 50 / 44 / 48 |
| Spec AF | Holy Barrier 67 (32) | none | none |
| Str/Con | Archangel's Might 69 (33) | Power of the Deep 69 (33) | Strength of the Timberland 67 (32) |
| Dex/Qui | Coordination of the Archangel 75 (37) | Shadow's Enlightenment 70 (34) | Zephyr's Dance 73 (36) |
| Acuity | Acuity of the Magi 52 (15) | Vision of the Deep 52 (15) | Nature's Vision 52 (15) |
| Haste | none | none (the Healer has it) | Hunter's Assault 20% (19) |
| Other | Heavenly Infusion health regen (16) | Ultimate Force of the Deep damage add (timed, 20 min); Superior Earth Invigoration endurance regen (5) | none |
| Group only (a buffbot can't give these) | resist buffs and the Trial by Fire proc (20 min) | resist buffs (20 min) | resist buffs (20 min) |

Concentration pool, this server (`MaxConcentrationCalculator`): (level x 8.8 + (mana stat - 50)
x 2.8) / 2. At level 50 with a mana stat around 150-200 that is about 360-430 points. A Cleric's
full single-target set is about 200, so the whole set fits on one main. The plan still checks the
pool (`NoLiferBuffBots.PlanBuffs`).

Era note: timed group buffs were 10 minutes when the Cleric's proc arrived in 1.60 ("10 minute
group cast buffs", Patch 1.60, 24 Feb 2003) [P]. This server's data has 20 minutes.

## 4. How players ran buffbots (2003)

All from "Buff Bots rock or not?", FreddysHouse, 25-26 Mar 2003 [P], unless noted:
- **A second account:** "every buf-bot is a monthly subscription"; "you need DAoX or a 2nd PC to
  run the buffbot"; "only a small minority ... pay for two accounts".
- **Parked at the portal keep:** "the trend for leaving your bot in the zones PK whilst jumping
  unbuffed players"; "bots to stand in the PKs, BKs, places where they are inaccesable or
  invulnerable"; "you often see half a dozen at the keeps"; "standing afk at a pk".
- **No range limit:** "I would love if they put in a range limit or something on the conc buffs";
  "limiting the range that buffs are effective in the frontiers"; Fafnir proposes about 5,000.
  Fightersuntzu proposes that buffs drop 60 s after the buffed player leaves a zone around the PK.
  Both are proposals, not rules at the time.
- **Buffing others:** "any buffbots belonging to Guildies buff our groups"; "i usally buff people
  when i hit the frontier".
- **Levelling:** "most buffbots are used to pwrlevel alts".
- **Death drops buffs:** "all the buffs drop off my fallen enemy".
- **Raids:** "Feel free to bring additional support chars (i.e.: Buffbots)" (FreddysHouse, dragon
  raid, 2003-12-03) [P]; "park them on the hill to the right, out of Gole's reach" (FreddysHouse,
  2008) [L].
- **How common:** "everybody has a buffbot" (June 2003) [P]; buffbots were "everywhere" by June
  2004 (Tom's Hardware Usenet mirror, in research_notes) [P]. They were silent characters.

## 5. Mythic's stance and later rules

- 2003: no rule. Players said "Mythic ignore them ... promoting this practice" and "Mythic is going
  to officialy support buffbots soon" (Mar 2003). Both are claims, not Mythic statements.
- 1.69 (14 Apr 2004, Allakhazam "1.69 Has Landed") [P]: buff shear, and new area buffs at "about
  50% as effective as their concentration counterparts". It says nothing about buffbots.
- **Classic servers, 12 Jul 2005** (MMORPG.com news) [P]: "Ranged buffs implemented: All
  concentration buffs will have a range limit of 5,000 units and will be available only to the
  buffer and his groupmates." This is the first concrete anti-buffbot rule I found. It is after
  this server's era, and it applied to the alternate-ruleset servers.
- Not verified: when, or whether, the normal servers got a concentration range. Modern OpenDAoC
  uses 5,000.

## 6. This server's implementation (what the code does today) [S]

- **Concentration range:** `EffectHelper.GetConcentrationEffectActivationRange` uses
  `buff_range` (classes category). Its DB value is 0, whose description says "0 for unlimited",
  but the code turns 0 into **5,000** (1,500 for endurance regen).
  `EffectListComponent.HandleConcentrationEffectRangeCheck` **disables** an out-of-range
  concentration buff and re-enables it in range; it is not lost. This is the 2005 Classic rule,
  not 2003. It stays as it is for players and every other caster.
- **Group-only concentration:** not enforced. Realm-target spells can be cast on anyone of the
  realm; Group-target spells fan out to the caster's own group only.
- **Conc ends** when the caster dies or leaves the world (`EffectService`: an NPC caster that is not
  alive or active ends its concentration effects).
- **The 20-spell concentration cap** (`SpellHandler.MAX_CONC_SPELLS`) does not apply to GameBots.
- **Existing bot buffing:** `BotBrain` finds missing buffs on group and raid members
  (`FindRandomMissingRealmBuffTarget`, `CastCoordinatedBuff`, `LivingHasEffect`) and casts with
  `Body.TargetObject = target; Body.CastSpell(spell, m_mobSpellLine)`. The buffbot reuses
  `LivingHasEffect` and the same cast path.

## 7. Design decisions this research leads to

1. Classes: Cleric / Shaman / Druid, buff line 50.
2. Buffs: every single-target buff the class has, in the 2003 order (spec AF, str/con, dex/qui,
   base AF, base str, con, dex, acuity for power users, then haste, damage add and endurance for
   weapon users, damage shield, regen). Group-only spells are left out, because the buffbot never
   groups.
3. Range: the 2003 rule for buffbots only (setting on by default). Turned off, a parked buffbot's
   buffs switch off beyond 5,000 and come back when the main returns.
4. Where it goes: it follows only in its realm's PvE land. It parks at the nearest friendly bind
   (the portal keep in the frontier) when the main is in the frontier. It parks at the main's own
   bind (or the capital) when the main is in a dungeon, a battleground (which is level-capped) or a
   raid. When the main is dead or riding a stable route, it waits where it is.
5. It is a real target in RvR, as parked buffbots were, and it never fights back.
6. Chat: it is silent. A tell or an invite gets one short line ("afk buffbot", "buffbot, msg
   <main>").
7. Assumption: about one in three buffbots is named after its main ("Thorgrimbuff"). No period
   name count was found; treat this as flavour.

## Sources

- FreddysHouse, "Buff Bots rock or not?" (2003-03-25/26): https://forums.freddyshouse.com/threads/buff-bots-rock-or-not.118607/
- MMORPG.com, "Dark Age Of Camelot: New Classic Servers Released" (2005-07-12): https://forums.mmorpg.com/discussion/46000/dark-age-of-camelot-new-classic-servers-released
- Allakhazam, "1.69 Has Landed" (2004-04-14): https://camelot.allakhazam.com/story.html?story=3355
- Camelot Herald wiki, Patch Notes 1.60 (2003-02-24): https://camelotherald.fandom.com/wiki/Patch_Notes:_Version_1.60
- Allakhazam, "New Buff System" (2002-01-29; base/spec/item caps, not concentration): https://camelot.allakhazam.com/story.html?story=638
- Allakhazam bestiary, Grey Noble comment (2003-08-29), local copy raid-research/wayback-pass2/allakhazam/bestiary-summoner-s-hall.txt: https://camelot.allakhazam.com/db/mob/3984
- FreddysHouse, "GoV Dragon Raid Sunday Nov. 9th" (2003-11-07), local copy raid-research/wayback-pass2/freddyshouse/gov-dragon-raid-sunday-nov-9th.txt: https://forums.freddyshouse.com/threads/gov-dragon-raid-sunday-nov-9th.157076/
- FreddysHouse, "What is a good tactic to.." (2003-08-14): https://forums.freddyshouse.com/threads/what-is-a-good-tactic-to.140721/
- FreddysHouse, "Dragon Raid: First Edition" (2003-12-03): https://forums.freddyshouse.com/threads/dragon-raid-first-edition.161859/
- FreddysHouse, "Class census for june" (2003-06-19), via ERA-BUILDS-1.65.md: https://forums.freddyshouse.com/threads/class-census-for-june-o.131872/
- FreddysHouse, "How to kill the new Golestandt fast and efficient" (2008, later): https://forums.freddyshouse.com/threads/how-to-kill-the-new-golestandt-fast-and-efficient.224718/
- Dawn of Light forum proposals for a 5,000 range (a server project, not live rules): https://dolserver.sourceforge.net/viewtopic.php?p=70226
- This server: `opendaoc.sqlite3.db` (0.37b fresh) `Spell`, `LineXSpell`, `SpellLine`, `ServerProperty`; `GameServer/gameutils/EffectHelper.cs`, `ECS-Components/EffectListComponent.cs`, `ECS-Services/EffectService.cs`, `spells/SpellHandler.cs`, `propertycalc/MaxConcentrationCalculator.cs`, `bots/BotBrain.cs`.
