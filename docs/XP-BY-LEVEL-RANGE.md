# XP rates by level range

Two optional server settings let leveling speed change by level. For example, you can level at 3×
through the early game and 12× from level 35 to 50. **Both are off by default**, and the server
behaves exactly as before until you set them.

| Setting | Who it affects | When it is empty (default) |
|---|---|---|
| `xp_rate_by_level` | Your characters (real players) | `xp_rate` for every level |
| `bot_xp_rate_by_level` | Autonomous gamebots only | `bot_xp_rate` for every level |

## Format

Write each range as `first-last:rate`, with ranges separated by semicolons:
`1-19:3;20-34:5;35-50:12`

- **A single level** can be written without a dash: `50:2`.
- **Spaces** are allowed.
- **Decimals** use a dot: `1.5`, not `1,5`.
- **Rates** must be above 0 and at most 1000. A rate below 1 slows leveling down.
- **Ranges** may not overlap.

## How it applies

**Inside a listed range, its rate replaces the flat rate.** Levels outside every range keep the flat
rate.

| Case | Your characters | Autonomous gamebots |
|---|---|---|
| Base XP from monsters | Uses the range rate | Uses the range rate |
| Zone XP bonus | Uses the range rate | Uses the range rate |
| In RvR zones | Keeps `rvr_zones_xp_rate`, as before | Range rate × `rvr_zones_xp_rate`, as `bot_xp_rate` was |
| Quest XP (quests, bounties, dailies) | Uses the range rate (see [Quest XP](#quest-xp) below) | Gamebots do no quests |

- **Same parts as the flat rates:** like `xp_rate` and `bot_xp_rate`, the range rate multiplies the
  base XP of a kill and the zone bonus. Camp, group and other bonuses are added on top unmultiplied,
  as before. The per-kill XP cap is applied before any rate.
- **Which level counts:** the character's level when the XP arrives.
- **Not affected:** companion bots (`/spawn`, `/raid`).
- **An invalid setting is ignored:** the flat rate stays in use, and the server log says why once.

## Quest XP

Since Living Server 1.1.1 quest experience follows your range rate too (2026-10-30). Before that, quest XP
skipped every rate: at 12× from kills a quest that pays a bubble felt like nothing.

- **The setting is `quest_xp_by_level`.** It is on by default. It is a server setting like the others
  (the `ServerProperty` table, category `rates`); set its value to `False` with the server closed to turn
  it off. The server reads it when it starts. **It is a kill switch (2026-10-31):** the range rate and the
  one-level cap below both belong to it. Off, quest XP is exactly what it was before 1.1.1: no rate, no cap,
  one raw payment (and a bounty pays bulbs × `xp_rate` in full, level after level, as it did).
- **What it does.** When it is on and one of your `xp_rate_by_level` ranges covers your level, the XP a
  quest pays is multiplied by that range's rate. The base is still the 0.36 rule: a classic quest pays its
  share of your current level (about a bubble for a typical quest), other quests their fixed amount. At
  the plan's rates a typical quest at level 25 pays half a level instead of a bubble; at 35 and above the
  plan's 12× would be more than a level, so the cap below pays exactly one level.
- **When nothing changes.** With the setting off, a quest pays exactly what it paid before. With it on but
  no `xp_rate_by_level` set, an invalid one, or a level outside every range, the amount is what it was
  before too, within one level per payment. `xp_rate` on its own never touches quest XP; it is a kill rate.
- **Which quests.** Data quests (the classic quests and their reward quests), scripted reward quests, the
  Bounty Master's bounties, the Atlas daily, weekly and monthly quests and the battleground quests, and
  since 2026-10-31 the three Shrouded Isles quests Ancestral Secrets, The Lost Seed and Lost Stone of
  Arawn (half a level each) and the Wolf Pelt Cloak payments, which used to pay outside every rate. Only
  three Atlas quests that pay 20 points (Play the Last Song, Help Sir Lukas, Power of Nature) are left as
  they were. Kill XP, bot XP and the RvR rates are not touched.
- **Bounties.** A bounty's bulbs used to be multiplied by `xp_rate`. With the setting on they still are,
  outside your ranges; inside a range the range's rate replaces `xp_rate`, as it does for kills, and the
  cap applies. For example, a Hard bounty at level 20 (4 bulbs of level 20, 12,300,000 a level): at a flat
  `xp_rate` of 3 with no ranges it paid 14,760,000 before and pays 12,300,000 (one level) now; at the
  launcher's 10× preset it paid 49,200,000 (into level 22) before and pays 12,300,000 now. With the setting
  off both pay what they did before.
- **One payment, one level at most (while the setting is on).** Whatever the rate, one quest payment pays
  at most one level's worth of experience (the width of your level when it pays); anything beyond that is
  dropped, not carried. So a quest can never pay you two levels. The rule was chosen over "stop at the start
  of the next level" because it leaves every payout that already fit in a level unchanged (the Atlas
  weeklies pay a whole level today). Inside that one level the server pays the XP to each boundary in turn
  (the second stage at levels 40 to 49, then the next level, then the rest), which is why a big quest reward
  can show two or three "You get ... experience points" lines; it is how the ding happens now instead of on
  your next kill. At levels 40 to 48 a level is wider than half of the next one, so one payment near the top
  of a level can give you the level and then the next level's second stage (its spec points) as well; that
  is still one level.
- **The offer window shows what you will get.** The experience bar in a quest's offer and reward windows
  shows the amount the quest will pay you at your level and rate, after the cap, as a share of your level
  (it used to show the raw amount from the quest's row). The bar cannot show more than a whole level, so
  with the setting off a payment above a level shows as a whole one.
- **In the log.** Each quest payment writes one `QUEST_XP` line to the server log with the quest, your
  level, the base amount, the rate, the scaled amount and what was paid, and `quest_xp_by_level=off` when
  the switch is off.

**Automated checks (2026-10-31):** `UT_QuestExperience` (35 tests) covers the rate on and off, no range
set, an invalid range, levels outside the ranges, the bounty's `xp_rate` handling, the cap, that the cap
and the rate both go with the setting, and the offer window's percentage. Not checked in game yet.

## The plan's values

These come from the Living World plan (model v4):

| Who | Levels 1–19 | Levels 20–34 | Levels 35–50 |
|---|---|---|---|
| You | 3× | 5× | 12× |
| Gamebots | 1.5× | 2.5× | 6× |

## Setting them

The easy way is the launcher's **Living Server** tab (with the server stopped; it applies at the next start): pick **Off**, the **suggested** rates or **custom** ranges, for
you and for bots. It checks the ranges by the server's own rules before it saves.

The helper script does the same from a command line. The Living Server installer puts it in your playable
folder's `tools\claude-version\`.

**Close the server, launcher and game first.** Then, from the playable folder:

```bat
python tools\claude-version\set_xp_by_level.py
python tools\claude-version\set_xp_by_level.py --player "1-19:3;20-34:5;35-50:12" --bots "1-19:1.5;20-34:2.5;35-50:6"
python tools\claude-version\set_xp_by_level.py --player "1-19:3;20-34:5;35-50:12" --bots "1-19:1.5;20-34:2.5;35-50:6" --apply
python tools\claude-version\set_xp_by_level.py --off --apply
```

- **The first line** shows the current settings.
- **The second** checks the values (a dry run).
- **The third** writes them, after backing up the database to `runtime\server\deployment-backups`.
- **The last** goes back to the flat rates.

The launcher's XP buttons still set the flat `xp_rate` and `bot_xp_rate`. Those flat rates are what
applies outside your ranges.

**Resets:**
- **The progress transfer tool resets the XP settings.** After a transfer into a new install it sets
  `xp_rate` and `bot_xp_rate` back to 1, and it now also clears both range settings. Set them again
  afterwards.
- **Public packages always ship with both settings off.** `build_release_036.py` clears them and
  checks that they are empty.

## Status

- **Automated tests:**
  - `UT_LevelRangeXpRates` covers parsing and the range lookup;
  - `UT_BotXpRateSeparation` covers bot scaling, including RvR zones;
  - they run in the full server suite on every build (2,911 of 2,911 passed for 0.36-se.1; the 47
    test classes that need installed game files can't run on GitHub).
- **In-game check (2026-10-17, build 0.36-chris.8, Godot client):** the suggested rates were set
  in the launcher, with both flat rates at 1×. Each kill was measured with `/xpstats verbose` as
  (XP received − camp, guild and outpost bonuses) ÷ base XP:

  | Character level | Zone | Base XP | Received | Bonuses | Rate | Expected |
  |---|---|---|---|---|---|---|
  | 4 | Avalon Marsh | 20 | 72 | 12 | 3.0× | 3× |
  | 25 | Cornwall | 650,200 | 3,641,120 | 390,120 | 5.0× | 5× |
  | 40 | Lyonesse | 12,044,975 | 151,766,684 | 7,226,984 | 12.0× | 12× |
  | 25 | Snowdonia (frontier) | 650,200 | 1,105,340 | 455,140 | 1.0× | `rvr_zones_xp_rate` (1) |

  - Snowdonia, Pennine Mountains, Forest Sauvage and Hadrian's Wall are RvR zones in region 1, so
    they keep the RvR rate as designed. Test in a PvE zone.
  - The first kill after a GM `/player level` change is topped up to the new level's floor, so
    it doesn't show the rate. Use the second kill.
  - **Bot rates, partly checked (2026-10-17, build 0.36-chris.11).** The log's
    `AUTONOMOUS_XP_PROGRESS` lines show the XP of the kill that levels a bot, but not the base XP
    or the mob. I ran 10 minutes with the suggested bot rates (1.5× for levels 1–19) and compared
    these kills with about 1,250 from the day's runs at 1×:

    | Level-up from | Kills at 1× (median XP) | Kills at 1.5× (median XP) | Ratio |
    |---|---|---|---|
    | 2 | 390 (14) | 5 (20) | 1.43 |
    | 3 | 275 (26) | 14 (41) | 1.58 |
    | 4 | 123 (58) | 21 (164) | 2.83 |
    | 5 | 28 (120.5) | 9 (328) | 2.72 |

    - Every level gained more with the setting on, and levels 2 and 3 come out near 1.5×.
    - Levels 4 and 5 come out near 2.8×. The kills differ between runs: the 1× runs had
      personalities on, which steers bots to other camps. The code multiplies only the base XP,
      once (`GameBot.ScaleAutonomousExperience`); camp and group bonuses aren't scaled.
    - So bot rates raise XP in game, but the exact factor isn't confirmed this way. A log of the
      base XP per kill would settle it.
- **Code:** `source/server/GameServer/gameutils/LevelRangeXpRates.cs`, used by `GamePlayer` and
  `GameBot`; quest XP: `gameutils/QuestExperience.cs` and `GamePlayer.GainQuestExperience`.
