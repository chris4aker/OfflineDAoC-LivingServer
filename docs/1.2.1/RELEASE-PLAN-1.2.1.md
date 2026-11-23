# 1.2.1 release plan (owner, 2026-11-22)

The owner can't run the PC checks this weekend: "I err on publishing everything we can and keep the raid (407-415)
either open as unresolved issues or try to fix them as best as possible based on simulated testing." And: "Add the CI
tests for death, and plan for a release with features marked as tests only."

So 1.2.1 ships with every change that passes CI. Which changes have been seen in a played game is tracked in
[../OPEN-WORK.md](../OPEN-WORK.md) and the issues (AGENTS.md: a feature counts as working only once seen in the owner's
PC logs), not in the release notes.

## Steps

1. **Lore in.** The epic pages (#399–#402), their three realm reviews and decision 10 are merged. The major-boss and
   place depth work (#403, #404) goes in once its independent review passes.
2. **Death and rez in CI.** New `playtester` scenarios run a real server and a scripted original-client player:
   - **bot death:** the death packets arrive in order, with no "alive" update while the bot is dead;
   - **corpse target:** `/target <bot>` selects the corpse;
   - **rez:** a rez cast with no target picks a groupmate first, and the bot stands up again.

   These check packets, not what the old client draws. The fall animation is still tests only.
3. **Raids (#407–#415).** Fix what the logs and simulations clearly show, each fix labelled tests only. The issues
   stay open until the raid is seen won on the owner's PC.
4. **Loops (#420, #421) and #425.** Stay open, tests only.
5. **Release notes** (`docs/RELEASE-NOTES-1.2.1.md`) for players: what's new and fixed, grouped by area, with no
   testing-status framing and no references to the owner's PC (owner, 2026-11-22; AGENTS.md, "Be honest in reports").
   They replace the first draft's two lists ("Seen in game" / "Tests only").

   The notes also give players the switch and the tools:
   - `bot_death_animation` off brings back the old behaviour;
   - `/lstest deathanim` plays a death, a raise and a release on demand;
   - the log lines that show the new behaviour: `BOT_DEATH_ANIM`, `HIRED_COMPANION_RAISED`, `RVR_LORD_STEP_IN`,
     `RVR_ROAM_CHASE`, `RVR_FRONTIER_WAIT_LONG`, `PLAYER_CLICK_TARGET`, `LOOT_ITEMS_MISSING`.
6. **Install checks in CI:**
   - the `candidate-install` and `installer` workflows: an update over 1.2 and a new install;
   - `release.yml` as a dry run with tag `v1.2.1`.
7. **Merge `integration/1.2.1` into `main`.** The owner agreed on 2026-11-22 that main need not wait for the PC check
   this time.
8. **Publish:** `publish-release` with tag `v1.2.1`, run by the owner or with the owner's go-ahead.

## Still for the owner (not blocking)

- **Midgard's epic review:**
  - Dyranapur's 31 dark moons against the slate's forty winters;
  - the Behemoth's new fifth chapter;
  - whether chapter titles count toward Hallbjorn's cap of 12.
- **After release:** the PC checklist in [../OPEN-WORK.md](../OPEN-WORK.md). Each row moves from tests only to seen
  in game once a log shows it.
