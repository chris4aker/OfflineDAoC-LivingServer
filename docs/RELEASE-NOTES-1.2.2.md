# Living Server 1.2.2: release notes (draft)

The text below the line is the draft of the GitHub release notes for 1.2.2, the raid-size release on top of 1.2.1. It
lists only what changed since 1.2.1; [RELEASE-NOTES-1.2.md](RELEASE-NOTES-1.2.md) still describes the whole of Living
Server. Publish it as Latest, not as a pre-release: the launcher's update check reads GitHub's latest release.

1.2.2 is the raid sizes only (owner, 2026-11-22). Uthgard's drop tables and the fixes from the PC checks come in
**1.2.3**. Everything below is **tests only** (AGENTS.md: a feature counts as working only once an owner PC log shows it).

Before publishing:
- **Seen in game:** if an owner PC log shows a raised raid won before publishing, move it from "Tests only" to "Seen
  in game", with the build and the log line.
- **Automated results:** fill in "How this was tested" with the release candidate's CI run, naming its commit.
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

A small release for **1.2.1**: bigger bot raids at four encounters where 1.2.1's tactics work but the raid was too
small to win: **Summoner's Hall, the Green Knight, Galladoria and Tuscaran Glacier**. No boss was made weaker.

Uthgard's drop tables and the fixes from the PC checks come in **1.2.3**.

It installs over Offline DAoC **0.37** and plays on the **original game client**. Everything Living Server does is
still described in the [1.2 release notes](https://github.com/chris4aker/OfflineDAoC-LivingServer/blob/main/docs/RELEASE-NOTES-1.2.md).

## How to read these notes

- **Seen in game** means seen on the owner's PC, on screen or in its server log, on the build named.
- **Tests only** means unit tests, CI, simulations or test-world bot runs. It has **not** been seen in a real game yet.
- **All of 1.2.2 is tests only.** Every new raid size comes from a simulation of the fight, not from a raid played
  on a PC.

---

## 1. Seen in game

**Nothing that is new in 1.2.2 has been seen in game yet.** What 1.2.1 builds on is listed in the
[1.2.1 notes](https://github.com/chris4aker/OfflineDAoC-LivingServer/blob/main/docs/RELEASE-NOTES-1.2.1.md).

---

## 2. Tests only

### The rule

The project owner's ruling, 2026-11-22: "If the mechanics and tactics are working but the period raider size from logs
isn't cutting it, and adding more bots without it becoming a Zerg wins, we should bump the suggested raider and period
size."

- **When a raid gets bigger:** only when its simulation shows the 1.2.1 tactic working, but the raid still losing at
  its old size and winning with more bots. It then goes to the smallest size, in whole groups of 8, that wins about two
  times in three.
- **Never a zerg:**
  - at most **twice** what the 2003-2004 raid guides say the fight took;
  - never more than **300**;
  - and at the new size, the rules from before 1.2.1 still **lose**, so the extra bots win because of the tactic.
- **No boss is touched.** The Green Knight's trees still stop at 70 a wave.
- **The raid still adjusts itself.** It starts at 125% of its size, gets smaller after wins and bigger after losses,
  as before.

### What changed per raid

| Raid | Was | Now | Why (from the simulations) |
|---|---|---|---|
| **Summoner's Hall** (all three realms) | 64 | **80** | Summoner Roesia's Pain beats every raid of 64: 0 wins in 64 tries with 1.2.1's cures. At 88-96 she loses 54-61 times in 64. The guides say four to five groups, so 80 is as far as the raise goes. The raid now starts at 100, where she loses almost every time. The old cure rule wins 0-1 times at any size. |
| **The Green Knight** | 40 | **80** | With 1.2.1's stack and peel the raid wins 58 times in 64 at 80, and never at 40-64. The old rules never win, at any size up to 96. His trees still stop at 70 a wave. |
| **Galladoria** | 100 | **112** | At 14 groups, all 8 add groups go after Spindler Broodmother's newborns, and fewer are left at her death than in the one win seen on a PC. At 13 groups that doesn't happen. With the old rules it takes 16 groups. |
| **Tuscaran Glacier** | 100 | **120** | At 120, Queen Kula dies as fast as in the one win seen on a PC (20 minutes); at 100 it takes 23. The old rules are 1-7 minutes slower at 120. King Tuscar's own fight isn't simulated yet. |

**Kept:**
- **Caer Sidi** stays at 100: the Crypt Lord problem is about timing the pull, not raid size.
- **Trollheim** stays at 64: the 1.2.1 fix for the timer never times out, at any size.
- **The three dragons** stay at 70: they win at 70, and at 80 the old rules win too.
- **Darkness Falls** stays at 64: Legion falls within 20 minutes at any size from 48 up.
- **Marfoirwort** stays at 40: the raid beats him at 40, and a bigger raid would win by numbers alone.

### Good to know

- **Your raid history carries over in bots.** If your world had already grown Summoner's Hall to 106 bots after
  losses, it carries on from 106, not from the same percentage of the new size.
- **A bigger size needs the bots:**
  - A raid's full size is half the realm's free level 50s, up to its new size.
  - The new sizes take effect with at least 224 free level 50s for Galladoria, 240 for Tuscaran Glacier, and 160 for
    Summoner's Hall and the Green Knight.
  - With fewer, nothing changes.
- **GM test:** `/epictest start <raid> era` runs a raid at its new size; `/epictest start <raid> source` runs it at
  the old one.

### Also in 1.2.2

- **The launcher shows your Living Server version:** "LIVING SERVER 1.2.2" where it used to say "VERSION 0.37".
- **GM `/jump` and bots:** jumping a dead bot tells you why it stays put (its body stays where it fell: raise it there
  first), and a gamebot you jump sets off again from where it lands.

### How this was tested

- **Simulations:** Python ports of the 1.2.1 raid simulations. Each port gives exactly the numbers its C# test pins.
  They were run at every size from the old one to twice the guides' figure, with the 1.2.1 tactics and with the old
  rules. The tables are in
  [docs/RAID-SIZES.md](https://github.com/chris4aker/OfflineDAoC-LivingServer/blob/main/docs/RAID-SIZES.md).
- **CI:** unit tests check that each new size is within the limit, that no boss mechanic moved, and that a world's
  raid history carries over.
- **None of these results is from a played raid.**

---

## 3. What to send back

The server log's `REALM_RAID_SIZED` line shows each raid's size and why, and `REALM_RAID_ENDED` shows how it went.
A won or lost Summoner's Hall, Green Knight, Galladoria or Tuscaran Glacier raid is what turns these from "tests only"
into "seen in game". For the Green Knight, the `EPIC_GK_TREES` lines tell whether 80 is enough.

## 4. Known issues

- **Bot raids still lose some fights (#407 to #415).** The issues stay open until each raid is seen won on the
  owner's PC. King Tuscar and Grand Summoner Govannon have no full simulation yet.
- Everything else is as in 1.2.1:
  [docs/KNOWN-ISSUES.md](https://github.com/chris4aker/OfflineDAoC-LivingServer/blob/main/docs/KNOWN-ISSUES.md). Every
  change: [CHANGELOG-LIVING-SERVER.md](https://github.com/chris4aker/OfflineDAoC-LivingServer/blob/main/CHANGELOG-LIVING-SERVER.md).

---

## 5. How to update

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
