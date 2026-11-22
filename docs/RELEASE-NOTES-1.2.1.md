# Living Server 1.2.1: release notes (draft)

The text below the line is the draft of the GitHub release notes for 1.2.1, the first patch after 1.2 "Where Legends
Wake". 1.2.1 is a final release (build `1.2.1`, tag `v1.2.1`), like 1.2. Publish it as Latest, not as a pre-release:
the launcher's update check reads GitHub's latest release, which leaves pre-releases out.

The owner's plan for this release ([1.2.1/RELEASE-PLAN-1.2.1.md](1.2.1/RELEASE-PLAN-1.2.1.md), 2026-11-22): every change
that passes CI ships, and a change not yet seen on the owner's PC is listed as **tests only**. The notes have two lists:
**Seen in game** (only what an owner's PC log has shown, from 1.2 or earlier, with the build) and **Tests only**
(everything new in 1.2.1, the death fall and the rez included).

Before publishing:
- **Files table:** the Setup row must keep exactly `(filled in when the exe is built)`: the release workflow replaces only
  that text with the exe's SHA-256. The zip row must keep exactly `(zip SHA-256: filled in by hand at publish)`:
  `publish_release.py notes` replaces it with the zip's SHA-256. Both rows name the build (`1.2.1`); the tag is `v1.2.1`.
- The raid fixes still in review (#428, #430-#432) are summed up under "Raids" below; if one is not merged, take its
  line out.

Nothing here names the Easter eggs or the project's internal vocabulary.

---

# Living Server 1.2.1

*Fixes and lore after Where Legends Wake.*

> Living Server is an unofficial, free, non-commercial fan project. It is not affiliated with, endorsed by or
> connected to Electronic Arts or Broadsword Online Games. Dark Age of Camelot belongs to them.

1.2.1 is the first patch after 1.2. Bots now fall when they die in the original client and can be raised there, hired
companions join at your level, enemy gamebots are worth experience, and the lore is deeper and told entirely in-world.
For everything Living Server does, see the
[1.2 notes](https://github.com/chris4aker/OfflineDAoC-LivingServer/blob/main/docs/RELEASE-NOTES-1.2.md).

A feature counts as working only once it has been seen working on the owner's PC. **Nothing new in 1.2.1 has been seen
there yet: all of it is tests only.** It was built and tested by CI (unit tests, a scripted player against a real
server, and installs on Windows runners), which is not the same as being played. Each "seen" result below names the
1.2 build it came from; none of them comes from the build you are downloading.

It's for Offline DAoC **0.37 and 0.37b**, with or without the **0.37c** new client. A new install from Setup is **0.37**
(the original client, Classic and Shrouded Isles classes).

---

## 1. New in 1.2.1 (tests only)

### Bots and companions
- **Bots fall when they die in the original client.** A companion, hired companion or gamebot falls and lies like a
  monster's corpse instead of standing at zero health. Raised, it gets up where it lay; released, it appears at its
  bind point with no second body left behind. The new client already showed the fall and is unchanged. Setting
  `bot_death_animation` ("Bots fall when they die" in the launcher), on by default.
- **Raising a dead bot in the original client.** That client won't let you select a dead bot, so a rez said "You must
  select a target for this spell!". A resurrection cast with nothing dead selected now goes to a dead bot of your realm
  in range: the one you named with `/target <name>`, otherwise one from your group, otherwise the nearest. A line says
  who it goes to.
- **Hired companions join at your level**, with their spec points spent in their class's build and, above level 5,
  plain merchant armour, a weapon and a shield where their class carries one. Companions hired on 1.2 catch up once,
  the next time they come to you, keeping their gear, bag and coin.
- **`/hire train` reads line names** (`/hire train 1 smite 10`), and the trainer window shows each line's next skill at
  the top.
- **No pet cap for players:** Animist turrets and Theurgist pets are limited by power and casting only. Companions and
  gamebots keep their limits.
- **Bot parties take one more on the way:** a bot party that set off with a seat free looks for the role it lacks,
  and may ask you by /lfg or a tell.
- The names the hire window suggests no longer use the lore's own heroes. Companions already hired keep their names.

### Rewards, loot and crafting
- **Enemy gamebot kills give experience**, as a player kill does (and bounty points outside battlegrounds). A bot
  killed again within five minutes is still worth nothing.
- **Named drops in groups a gamebot leads** drop again.
- **A piece you ordered from a crafter bot is handed over:** the crafter opens the trade if you are near, or tells you
  once where to collect it.
- Classic-era drops (hides, teeth, gems, vendor loot) for monsters with no loot of their own are ready in the release
  tools; they reach a world only where the owner's data pipeline has run.

### RvR and travel
- **Keep lords take damage from the storm:** battleground and frontier attackers walk to within reach of a keep or tower
  lord before striking.
- **Roaming eight-man groups find fights:** their patrols favour where enemy bots are, and they follow a hunt of an enemy
  group nearby.
- **Frontier porters:** a gamebot without a medallion goes to the nearest porter that sells one.
- Bots whose route out of Castle Sauvage failed restart from the border keep; gamebots saved beside another realm's
  keep log in at their capital; a keep defence with no defenders left stands down.

### Raids
- Fixes from simulated fights for the raids the bots still lose (Caer Sidi, Galladoria, Tuscaran Glacier, Summoner's
  Hall, Trollheim, the Green Knight and Marfoirwort, Legion, the dragons). **None of these raids has been seen won on the
  owner's PC since**: their issues stay open (#407-#415).
- Elro the Ancient's third treant comes as his script meant.

### Chat, targeting, the launcher and stability
- **/faction quiet minutes** fill back to about 5 lines a minute; the cap of 7 still holds.
- **`/target`** finds a nearby NPC for a player too (Caer Caledon's guards).
- **The bot schedule is off by default:** the whole roster is online. A world that ran 1.2 with the untouched real-clock
  default moves to off once; a schedule you chose is kept. Bots' breaks are a separate setting and stay on.
- The launcher's chat setting reads "HOW MUCH BOTS TALK".
- The casting line-of-sight check no longer leaves a gap when it can't ask a client; a packet queued while its queue
  is drained waits instead of failing.

### Lore
- **Every great foe has its book:** all 157 epic bosses have at least four chapters, a story and two to four book pages
  written as documents of their world.
- **Legends, deeper:** each realm's heroes ride with their own legend companies, and the epic places and thin bosses
  have more to tell. Nothing was cut.
- **Told in-world:** fight advice, place lore and bot lines no longer use game terms or name real companies, and the
  fight tips now match what each boss really does.

---

## 2. Seen in game (from 1.2 builds)

Nothing that changed in 1.2.1 has been seen on the owner's PC yet. What 1.2 builds showed there, by build, is in the
[1.2 notes, section 3](https://github.com/chris4aker/OfflineDAoC-LivingServer/blob/main/docs/RELEASE-NOTES-1.2.md#3-seen-and-tests-only):
chat, bots that invite you, raid calls, raids won, battleground and frontier RvR, crafting, the launcher's count,
companions and bots as people. Those results come from 1.2 test builds (for example `43c43aa`, `c81eb5d`), not from
1.2.1.

## 3. Tests only (everything new in 1.2.1)

Everything in section 1. In particular:
- **The death fall and the rez in the original client.** CI runs a real server with a scripted original-client player:
  a bot's death packets arrive in order with no "alive" update while it is dead, `/target <bot>` selects the corpse,
  and a rez cast with no target picks a groupmate first and the bot stands up. These check packets, not what the old
  client draws: the fall itself has not been seen.
- **The installs.** On Windows runners: an update from 1.2 (and from Beta 1.1.4) through Setup with a character, a bot
  roster and changed settings kept; a new install; the class-off (0.37) edition. Not yet run on the owner's PC.
- **The raid fixes**, from simulated fights only.

## 4. Help us check it

The safety switches and checks you can run:
- **`bot_death_animation` off** (the launcher's "Bots fall when they die") brings back 1.2's behaviour: every packet is
  as before.
- **`/lstest deathanim`** (GM) plays a death, a raise and a release on a targeted bot on demand, and lists each packet
  sent or held (`/lstest deathanim raise`, `release`, `off`).
- **The log lines worth sending back**, from `runtime\server\logs\server.log`: `BOT_DEATH_ANIM`,
  `HIRED_COMPANION_RAISED`, `RVR_LORD_STEP_IN`, `RVR_ROAM_CHASE`, `RVR_FRONTIER_WAIT_LONG`, `PLAYER_CLICK_TARGET`,
  `LOOT_ITEMS_MISSING`, and the `FORK_BUILD name=1.2.1` line at the top.

## 5. Known issues

- **Raids the bots still lose** (#407-#415): Caer Sidi's Crypt Lord, Galladoria, Tuscaran Glacier, Summoner's Hall,
  Trollheim, the Green Knight and Marfoirwort, Legion's platform; Behemoth, Fomor City and Morgana have no bot raid;
  dragon fights can fall apart partway.
- **Route loops:** repeated rescues uphill to a Connacht hunting camp (#420) and the Lough Derg / Bri Leith seam loops
  (#421).
- **The bot death fall in the original client** (#425) is checked by CI's packets only, not by eye.
- A dead bot's body still can't be clicked in the original client; use `/target <name>` or cast with nothing selected.
- Everything else from 1.2 is in [Known issues](https://github.com/chris4aker/OfflineDAoC-LivingServer/blob/main/docs/KNOWN-ISSUES.md).

## 6. Upgrade notes

- **From 1.2 or any 1.1.x from 1.1.2 on:** **Check for updates** in the launcher; START SERVER then reads **GET UPDATE**.
  Or run `LivingServer-Setup-1.2.1.exe` and pick your game. Characters, bots, guilds, bot memory and your settings stay.
- **The bot schedule** moves to off once, only where your world still has 1.2's untouched real-clock default.
- **New install:** run `LivingServer-Setup-1.2.1.exe`. It downloads Offline DAoC 0.37 from the project's own copy of the
  release files, then adds Living Server.
- **By hand:** close the game and server, unzip `OfflineDAoC-LivingServer-1.2.1.zip` into your game folder and run
  `INSTALL.cmd`. `ROLLBACK.cmd` undoes it.
- Windows SmartScreen may warn, because the exe isn't code-signed: **More info**, then **Run anyway**.

### Files

| File | SHA-256 |
|---|---|
| `LivingServer-Setup-1.2.1.exe` | (filled in when the exe is built) |
| `OfflineDAoC-LivingServer-1.2.1.zip` | (zip SHA-256: filled in by hand at publish) |

Every change, with its numbers, is in the
[changelog](https://github.com/chris4aker/OfflineDAoC-LivingServer/blob/main/CHANGELOG-LIVING-SERVER.md).

### Credits

Offline DAoC is shadowofze's; Living Server builds on it and on OpenDAoC. Thanks to everyone who reported issues and
sent fixes after 1.2.
