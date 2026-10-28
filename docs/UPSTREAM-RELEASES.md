# When shadowofze publishes a new Offline DAoC release

Living Server is a server build (`GameServer.dll`) plus the launcher, installed over a released Offline DAoC game.
Each new upstream release changes that game's own server, so a Living Server build must carry the new release's
server code before it may install on it. Otherwise it would undo that release's server changes. This page is the
checklist for that day, written for 0.38.

## State as of 2026-10-27

- **Released upstream:** 0.37 / 0.37b / 0.37c (tag `v0.37`, `ab4df02`).
- **0.38 so far:** shadowofze's `main` (`9da9091`, 2026-10-20) has 0.38 work that isn't released.
- **Beta 1.1 already carries its server code:** branch `upstream/0.38-server`, all of `source/server` from
  `v0.37..9da9091`.
- **Left out of 1.1:** the new-client changes, the patch tools and baselines, and the 0.38 world-data changes
  (Campacorentin patrols, helmets already stored in a world). Those come with shadowofze's own 0.38 patch.
- **Preview branch:** `sync/upstream-0.38-preview` applies everything on `main` (client and tools too). It built and
  passed 3,457 server tests on 2026-10-27, with three trivial conflicts.
- **Installer:** `BaseRelease` in `ForkBuildInfo.cs` can list several releases (`0.37,0.38`). The installer then
  accepts a game whose newest release notes are any of them, and still refuses anything newer. It stays `0.37` until
  the steps below are done.

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
- **The siege expiry throttle bug and the effect leak are still in his `main`.** Both are fixed in Beta 1.1 (PR #27)
  and worth offering upstream.
