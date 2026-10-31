# When shadowofze publishes a new Offline DAoC release

Living Server is a server build (`GameServer.dll`) plus the launcher, installed over a released Offline DAoC game.
Each new upstream release changes that game's own server, so a Living Server build must carry the new release's
server code before it may install on it. Otherwise it would undo that release's server changes. This page is the
checklist for that day, written for 0.38.

## State as of 2026-10-31

- **shadowofze has stopped.** His README (commit `f630845`, 2026-10-31) says the project is on hold and will not
  receive any more updates; the 0.37 / 0.37b / 0.37c downloads stay; he has moved to a separate "True 1.65" version
  that uses only the new client, with no release promised. The checklist below still applies, read with "his final
  `main`" wherever it says the `v0.38` tag; the "0.37 → official 0.38 update → Living Server" test path no longer
  exists.
- **Released upstream:** 0.37 / 0.37b / 0.37c (tag `v0.37`, `ab4df02`). There is no `v0.38` tag; his changelog
  says "0.38 (not released yet)" and describes it as one download with the new client only, the original client
  unsupported and the Sluaghbinder discontinued (the class code is still there, it is no longer offered; a script
  `remove_sluaghbinder_npcs.py` removes its NPCs from a world — Living Server does not run it).
- **His `main`:** `b0b855d` (2026-10-31, five commits after the on-hold note, so possibly not final). Since
  `9da9091` it adds 40 commits: 127 server files (+6,293 / −539), 41 of them files Living Server also changed, and
  63 new-client files (+4,102). Quest code: only the HearthDAoC name matching, which 1.1 already has, and the tasks
  rework. His 0.38 changelog credits Living Server for ten or more fixes.
- **Beta 1.1 already carries his server code as of `9da9091`:** branch `upstream/0.38-server`, all of
  `source/server` from `v0.37..9da9091`.
- **Left out of 1.1:** the new-client changes, the patch tools and baselines, and the 0.38 world-data changes
  (Campacorentin patrols, helmets already stored in a world, and since then the 68 quest reward stats, Necromancer
  creation, the night spawns). With no upstream patch coming, they become Setup steps with rollbacks if the merge is
  taken ([RELEASE-TRAIN.md](RELEASE-TRAIN.md), decision 3).
- **Preview branch:** `sync/upstream-0.38-preview` applied everything on his `main` of 2026-10-27 (client and tools
  too). It built and passed 3,457 server tests, with three trivial conflicts. It predates most of the 40 commits.
- **Installer:** `BaseRelease` in `ForkBuildInfo.cs` can list several releases (`0.37,0.38`). With no 0.38 release
  it stays `0.37`: a merge of his final `main` is a Living Server build on a 0.37 game, not a new base release.

## Checklist

1. **What changed since what we carry.** Fetch the tag and compare the server code with what `upstream/0.38-server`
   took:
   `git diff 9da9091 v0.38 -- source/server`
   Anything new there must be merged before the build may claim 0.38.
2. **Merge the release.** Merge the tag (or apply `v0.37..v0.38` as on the preview branch). Resolve conflicts by
   keeping both sides' fixes. Then build and run the full server tests (CI exclusions applied).
3. **Claim the release.** Set `BaseRelease = "0.37,0.38"` if the build still works on a 0.37 world, or `"0.38"` if
   the new server code needs 0.38's world data. Decide by reading the release's world updates: a server change that
   reads a new world row needs that row.
4. **Content patches** (`tools/fork-build/content-patches.json`: Hall of the Corrupt's `zone277.nav` and eight
   new-client gate models).
   - A patch changes a file only when it has the 0.37 fingerprint, so a file the update changed is left alone and
     reported, not damaged.
   - For each file the update changed: if shadowofze's version already has the fix (0.38c slides portcullises
     too), drop the patch. If it doesn't, make a patch against the 0.38 file with `make_content_patch.py` and keep
     both entries.
5. **All-in-one installer** (`installer/all-in-one`, PR #26). It pins shadowofze's `Get-OfflineDAoC.ps1` by
   SHA-256 and knows the 0.37 and 0.36 starting points. It already reads a `BaseRelease` list (2026-10-28): a game
   on any listed release takes Living Server as it is, and new installs and updates go to the newest listed. For
   0.38 it still needs:
   - the new downloader and its pin;
   - an "existing 0.38" path;
   - a "0.37 → official 0.38 update → Living Server" path.
6. **Progress import tool.** Check `progress-policy.json` against any table the release adds to saved progress.
7. **Test on a real 0.38 game:**
   - install, check, rollback and `-ToRelease` on an updated game;
   - start the server and check the startup line (`FORK_BUILD name=... commit=...`);
   - log in.

   Report automated and in-game checks separately.
8. **Release notes.** Say which Offline DAoC releases the build fits, and credit shadowofze's fixes it carries.

## Things we know about 0.38

- **The Living Server meetup death cap is in 0.38** (credited to this fork). The only conflicts were its comment and
  its test.
- **shadowofze pulled the "dead gamebots stay where they fall" fix** from his 0.38 changelog, with no reason given.
  Living Server still has it.
- **The siege expiry throttle bug and the effect leak** were fixed in Beta 1.1 (PR #27) and are in his `main`
  since 2026-10-29, credited to Living Server (and stefanrows's fork for finding the siege one).
- **In his `main` and not in Living Server (2026-10-31):** area spells and add control for every class, Celerity,
  pet focus shields, Reaver auras, mesmerized and rooted monsters left alone, camp spread, tank Protect/Guard/
  Intercept, raid tether and rally fixes, performers holding one song, relic campaigns and relic captures, raid loot
  shared with a player, bots at player height, three Exchange listings per bot, seal merchants, the double dead
  body, companions' trained spells at once, keep doors not repairing under attack, Legion's throw, the tasks
  rework, loot tables rolling once per row, night spawns saved awake, the salvage type bug, Necromancer creation,
  disabled class and race ranges, Trials of Atlantis regions off, 68 quest reward stats, and the new client's
  stealth, quickbar abilities, merchant SELL, crafting window and freeze fixes.
