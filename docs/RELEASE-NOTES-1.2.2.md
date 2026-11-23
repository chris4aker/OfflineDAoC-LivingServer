# Living Server 1.2.2: release notes (draft)

The text below the line is the draft of the GitHub release notes for 1.2.2, the raid release on top of 1.2.1. It
lists only what changed since 1.2.1; [RELEASE-NOTES-1.2.md](RELEASE-NOTES-1.2.md) still describes the whole of Living
Server. Publish it as Latest, not as a pre-release: the launcher's update check reads GitHub's latest release.

1.2.2 is the raids only (owner, 2026-11-22); its raid-size raises were withdrawn on audit (owner, 2026-11-23). Uthgard's drop tables and the next round of fixes come in **1.2.3**.
The simulation tables, and why the raises were withdrawn, are in [RAID-SIZES.md](RAID-SIZES.md); what has been seen in a played game is
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

A raid release on top of **1.2.1**: a new plan for **the Green Knight** and his trees, sharper bot tactics in six epic
fights, keep and tower lords fought in formation, straighter answers in /faction, and fixes for companions and bots on
the move. **Every bot raid stays at its period size**, the size the
2003-2004 raid guides give. No boss was made weaker.

Uthgard's drop tables and the next round of fixes come in **1.2.3**.

It installs over Offline DAoC **0.37** and plays on the **original game client**. Everything Living Server does is
still described in the [1.2 release notes](https://github.com/chris4aker/OfflineDAoC-LivingServer/blob/main/docs/RELEASE-NOTES-1.2.md).

---

## What's new

### Raid sizes stay as they were

- **Every bot raid keeps its period size**, the size the 2003-2004 raid guides give: Summoner's Hall 64, Galladoria
  100, Tuscaran Glacier 100, the Green Knight 40, and every other raid as in 1.2.1.
- **The raid still adjusts itself.** It starts at 125% of its size, gets smaller after wins and bigger after losses,
  as before.
- **What changed is how the bots fight**, below under *Raid tactics* and *The Green Knight*.

### Keep lords

- **Bots fight keep and tower lords in formation:** the tanks and melee in front, casters and archers behind them on
  the side they came in from, spread out so one blast can't catch them all, and the healers at the back.

### The Green Knight: a new plan against his trees

A tree grows at every raider near him, keeps growing every half minute, and stops a healer or caster it hits from
casting. A heal on someone a tree is hitting draws the tree onto the healer. Now every raider fights from where its
spells, arrows and heals reach:
- **The healers stand together away from him**, out of his strike around his tank and close enough for the melee to
  pull trees off them. Four of them keep his target up first.
- **The melee mark every tree near them**, healers' and casters' first, then go back to him.
- **Area casters burn the marked trees**, guarded by a melee of their group. Other casters and archers shoot the marked
  trees they can safely take, and him otherwise. Theurgists send their elementals at the trees on the healers and
  casters.
- **Necromancers, Cabalists and other pet owners stand back** where no tree grows at them, and send their pets in.
- **When he walks off to heal, the raid holds its ground** and finishes the trees until he comes back.

His trees still stop at 70 a wave, and he is as strong as ever. **A raid of 40 isn't expected to beat him yet:** he
is still the hardest fight in the game for the bots, and the next releases keep working on him.

### Good to know

- **Your raid history carries over in bots.** Each raid's Herald record now says what its size is a share of, so the
  next raid carries on with the same number of bots.
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
- **Lifts work as they did in 1.2.** Bots and companions ride the lifts of Tur Suil, Iarnvidiur's Lair and the rest
  exactly as before.

### Raid tactics

- **Marfoirwort:** only his target fights him toe to toe. Everyone else stays out of his life-drain, and melee with a
  bow, crossbow or throwing weapon shoot him from there. Animists plant their shrooms out of its reach.
- **Paladins, Friars and Wardens** fight in melee in the new raid plans instead of walking back and forth to the healers.
- **Legion:** a badly hurt raider who steps away comes back once healed, not straight back into his blast.
- **Summoner Roesia:** healers cure their own group first while it is in trouble.
- **King Tuscar:** his tank holds him with taunts while Queen Kula lives.
- **The Crypt Lord:** the raid waits for his patrol to pass before pulling his court, however long that takes.
- **Spindler Broodmother:** archers with shorter bows stand close enough to hit her.

### Also in 1.2.2

- **Straighter answers in /faction:** ask what level a place is, who holds Darkness Falls or how long the epic takes, and
  the realm tells you; ask where to level and you hear a real zone for your level; ask where a class trains and you hear
  the right trainer, or that it's another realm's class; a question nobody can answer is left alone instead of drawing a
  "dunno".
- **Smoother bot answers:** a bot no longer laughs after "dunno" or a warning, answers a lore question without asking
  it back first, and a bot that writes neatly writes "Camelot" and "Tir na Nog" with their capitals.
- **Bots know the realm better:** ask in /faction where to buy dyes or poison, how to reach the frontier or Darkness
  Falls, who teaches a craft or how to fix your NPC faction, and a bot names who and where.
- **The launcher shows your Living Server version:** "LIVING SERVER 1.2.2" where it used to say "VERSION 0.37".
- **A fallen bot that leaves is gone from your screen.** When a dead bot logs out or leaves for another region, its
  body no longer stays lying where it fell.
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
