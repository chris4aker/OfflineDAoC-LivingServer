# Living Server 1.2.2: release notes (draft)

The text below the line is the draft of the GitHub release notes for 1.2.2, the raid-size release on top of 1.2.1. It
lists only what changed since 1.2.1; [RELEASE-NOTES-1.2.md](RELEASE-NOTES-1.2.md) still describes the whole of Living
Server. Publish it as Latest, not as a pre-release: the launcher's update check reads GitHub's latest release.

1.2.2 is the raid sizes only (owner, 2026-11-22). Uthgard's drop tables and the next round of fixes come in **1.2.3**.
The simulation tables behind each size are in [RAID-SIZES.md](RAID-SIZES.md); what has been seen in a played game is
tracked in [OPEN-WORK.md](OPEN-WORK.md) and the issues, not here.

Before publishing:
- **Voice (AGENTS.md, "Be honest in reports"):** the text below the line says what changed for players, clearly and
  confidently, with no testing-status framing and no word on where or by whom a change was checked.
- **Build name:** `ForkBuildInfo.BuildName` must read `1.2.2` and the changelog heading `## 1.2.2 — <date>`
  (`tools/release/check_versions.py --tag v1.2.2` checks both).
- **Files table:** the Setup row must keep exactly `(filled in when the exe is built)` and the zip row exactly
  `(zip SHA-256: filled in by hand at publish)`; the workflows replace those.
- Nothing below the line may contain a parenthesis that opens with the word "owner": the publish check reads it as a
  placeholder left in.

---

# Living Server 1.2.2

*Offline. Never alone.*

> Living Server is an unofficial, free, non-commercial fan project. It is not affiliated with, endorsed by or
> connected to Electronic Arts or Broadsword Online Games. Dark Age of Camelot belongs to them.

A small release for **1.2.1**: bigger bot raids at three encounters where 1.2.1's tactics work but the raid was too
small to win: **Summoner's Hall, Galladoria and Tuscaran Glacier**. And a new plan for **the Green Knight**, who
keeps his four to five groups. No boss was made weaker.

Uthgard's drop tables and the next round of fixes come in **1.2.3**.

It installs over Offline DAoC **0.37** and plays on the **original game client**. Everything Living Server does is
still described in the [1.2 release notes](https://github.com/chris4aker/OfflineDAoC-LivingServer/blob/main/docs/RELEASE-NOTES-1.2.md).

---

## What's new

### Bigger raids where the tactic works, never a zerg

- **When a raid gets bigger:** only where 1.2.1's tactic for the fight works and the raid still came up short at its
  old size. It grows to the smallest size, in whole groups of 8, that wins most of the time.
- **Never a zerg:**
  - at most **twice** what the 2003-2004 raid guides say the fight took;
  - never more than **300**;
  - and at the new size the raid wins because of the tactic, not by numbers alone: the rules from before 1.2.1 still
    lose there.
- **No boss is touched.** The Green Knight's trees still stop at 70 a wave.
- **The raid still adjusts itself.** It starts at 125% of its size, gets smaller after wins and bigger after losses,
  as before.

### What changed per raid

| Raid | Was | Now | What it means |
|---|---|---|---|
| **Summoner's Hall** (all three realms) | 64 | **80** | Enough raiders to outlast Summoner Roesia's Pain with 1.2.1's cures. The guides say four to five groups, so 80 is as far as the raise goes; the raid now starts at 100. |
| **Galladoria** | 100 | **112** | At 14 groups, all 8 add groups go after Spindler Broodmother's newborns, and fewer are left when she dies. |
| **Tuscaran Glacier** | 100 | **120** | Queen Kula goes down faster, so the raid reaches King Tuscar sooner. |

**Kept:**
- **The Green Knight** stays at 40, the guides' four to five groups, and his raid fights him a new way (below).
- **Caer Sidi** stays at 100: the Crypt Lord problem is about timing the pull, not raid size.
- **Trollheim** stays at 64: with the 1.2.1 fix the raid's timer no longer runs out, at any size.
- **The three dragons** stay at 70: the raid already beats them at 70.
- **Darkness Falls** stays at 64: Legion falls within 20 minutes at that size.
- **Marfoirwort** stays at 40: the raid beats him at 40, and a bigger raid would win by numbers alone.

### The Green Knight: a new plan against his trees

A tree grows at every raider near him, keeps growing every half minute, and stops a healer or caster it hits from
casting. A heal on someone a tree is hitting draws the tree onto the healer. The bots now:
- **send the casters and archers who can't hit a group of trees out of reach**, where no tree grows at them;
- **keep the healers together, away from him and his strike around his target**. They heal his target first, and
  leave someone a tree is hitting until they are under half;
- **have the melee mark every tree near them**, a healer's or caster's tree first, then go back to him and finish the
  trees on them once those are half dead;
- **keep the casters with area spells close, guarded by two melee of their group**. They burn the marked trees, but
  only while their spells won't draw the trees onto themselves.

His trees still stop at 70 a wave.

### Good to know

- **Your raid history carries over in bots.** If your world had already grown Summoner's Hall to 106 bots after
  losses, it carries on from 106, not from the same percentage of the new size.
- **A bigger size needs the bots:**
  - A raid's full size is half the realm's free level 50s, up to its new size.
  - The new sizes take effect with at least 224 free level 50s for Galladoria, 240 for Tuscaran Glacier, and 160 for
    Summoner's Hall.
  - With fewer, nothing changes.
- **GM tools:** `/epictest start <raid> era` runs a raid at its new size; `/epictest start <raid> source` runs it at
  the old one.
- **In the log:** the server log's `REALM_RAID_SIZED` line shows each raid's size and why, `REALM_RAID_ENDED` shows how
  it went, and for the Green Knight the `EPIC_GK_TREES` lines follow his trees. If a raid looks wrong, open a
  [GitHub issue](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues) with those lines and the `FORK_BUILD`
  line from the top of `runtime\server\logs\server.log`.

### Companions on the Darkness Falls stairs

- **Your companions now follow you back up the entrance stairs of Darkness Falls**, one flight at a time. Before,
  they waited at the bottom, or wandered off into the level below when you stood near the arrival point at the top.
  The server log shows each climb as `DF_STAIR_CLIMB`.

### Bots on the road

- **No more pacing at the Lough Derg / Valley of Bri Leith border.** Hibernian bots walking from the Valley toward
  Connacht, the Darkness Falls portal or Cruachan Gorge could step onto a ridge on the Lough Derg side that leads
  nowhere, turn back, step on again and finally give up their trip. They now cross where the ground goes on.
- **Companions on ladders and ledges.** A companion or bot climbing a ladder, a tower or a ledge, or jumping down one,
  no longer stops halfway when you walk on: it finishes the climb and catches up. Darkness Falls' stairs were fixed in
  1.2.1; this does the same everywhere else. A bot put back on its feet at the top or bottom shows in the server log
  as `NAV_LINK_LANDING`.

### Also in 1.2.2

- **Straighter answers in /faction:** ask what level a place is, who holds Darkness Falls or how long the epic takes, and
  the realm tells you; ask where to level and you hear a real zone for your level; ask where a class trains and you hear
  the right trainer, or that it's another realm's class; a question nobody can answer is left alone instead of drawing a
  "dunno".
- **The launcher shows your Living Server version:** "LIVING SERVER 1.2.2" where it used to say "VERSION 0.37".
- **GM `/jump` and bots:** jumping a dead bot tells you why it stays put (its body stays where it fell: raise it there
  first), and a gamebot you jump sets off again from where it lands.

## Known issues

- **Bot raids still lose some fights (#407 to #415).** King Tuscar's own fight and Grand Summoner Govannon can still
  turn a raid back.
- Everything else is as in 1.2.1:
  [docs/KNOWN-ISSUES.md](https://github.com/chris4aker/OfflineDAoC-LivingServer/blob/main/docs/KNOWN-ISSUES.md). Every
  change: [CHANGELOG-LIVING-SERVER.md](https://github.com/chris4aker/OfflineDAoC-LivingServer/blob/main/CHANGELOG-LIVING-SERVER.md).

---

## How to update

- **Have 1.2.1, 1.2, 1.1.4, 1.1.3 or 1.1.2:** in the launcher, **Check for updates**, then **GET UPDATE**. It installs
  only with the game and server closed.
- **Otherwise:** run `LivingServer-Setup-1.2.2.exe`, or unzip `OfflineDAoC-LivingServer-1.2.2.zip` into your game
  folder and run `INSTALL.cmd` with the game and server closed.
- **What stays:** your characters, bots, guilds, bot memory, companions, settings and raid history.
- **To undo:** `ROLLBACK.cmd` puts every replaced file back.

### Files

| File | SHA-256 |
|---|---|
| `LivingServer-Setup-1.2.2.exe` | (filled in when the exe is built) |
| `OfflineDAoC-LivingServer-1.2.2.zip` | (zip SHA-256: filled in by hand at publish) |

### Credits

Offline DAoC with Living Server is maintained by [chris4aker](https://github.com/chris4aker) and coded with Claude
(Anthropic). Offline DAoC was created by shadowofze; the server underneath is [OpenDAoC](https://github.com/OpenDAoC/OpenDAoC-Core).
GPL-3.0.

Living Server is a free, non-commercial, unofficial fan project, not affiliated with Electronic Arts or Broadsword.
