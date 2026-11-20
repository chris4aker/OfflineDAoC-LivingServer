# 1.2.1 handoff (written 2026-11-19 after the 1.2 release)

Start here. Then read `AGENTS.md` at the repository root, which holds the owner's rules. They apply to any assistant or developer.

## Where things stand

- **1.2 is released:** tag `v1.2` on `main` (commit 3ba8f20), published as Latest with
  `OfflineDAoC-LivingServer-1.2.zip` and `LivingServer-Setup-1.2.exe`. All CI and install tests passed.
- **Last build tested on the owner's PC:** `aa537ce`, which is `main` plus #376. Results by build are in
  [PC-RESULTS-1.2.md](PC-RESULTS-1.2.md).
- **Branches:** `integration/1.2` was squash-merged into `main` as #97. Start 1.2.1 work from `main`, on a new
  integration branch such as `integration/1.2.1`, and merge it back to `main` by pull request.
- **Not posted yet:** the Reddit post in [REDDIT-POST-1.2.md](REDDIT-POST-1.2.md). The owner posts it himself.

## What 1.2.1 fixes

The full list is in [FIX-LIST.md](FIX-LIST.md). In order:

1. **Lore clean-up:** grade C+, see [LORE-AUDIT.md](LORE-AUDIT.md). The files are in
   `source/server/GameServer/bots/autonomous/data/lore/`.
   1. Use a script to strip the developer notes from 74 `legend` texts and the duplicated lines in 55 files. Then
      add a lint check to the lore checker, so that template labels such as "Superstition:" and "Nicknames:",
      script words such as "controller", and duplicated lines fail CI.
   2. Relabel the provenance: 214 of the 262 facts marked `"mythic"` really come from Allakhazam, Prima or
      GameBanshee. Mark those `"legend"` or `"game"`, and make the checker reject `"mythic"` unless the
      source is a Mythic or Camelot Herald page.
   3. Fix the flagship entries:
      - **Gjalpinulva:** lead with Mythic's account; the svartalfar sentence doesn't parse.
      - **Legion:** reconcile its "two princes" origin with the Feast of Thrones in `history/neutral.json`, and
        remove "no raid tactics in our raid AI yet" from what players see.
      - **Cuuldurach:** "the only male dragon" is player talk, so present it as hearsay, not fact.
   4. Cap the repeated phrases with a phrase counter in CI, using `lore-audit-scripts/tics.py` as the base:
      - "the X say", currently 1,157 times;
      - "nobody", "still" and "has never";
      - Dame Ysolde, who appears 48 times.
   5. Trim the 165 stories that are over the 400-word limit. Cut minor bosses to 60–120 words, and give each
      realm's epics a voice of its own.
   - **Keep:** Golestandt's chapters, `locations/jordheim.json` and the Feast of Thrones history. The audit judged
     these the best writing.
   - **Rules:** lore is never cut wholesale; reword it. Nothing after 1.65 (8 October 2003). After editing the lore,
     regenerate anything built from it with its tool: the encounter catalog, the corpus and the debates.
2. **The release notes overclaim:** in `docs/RELEASE-NOTES-1.2.md`, "where they disagreed, the archive won"
   overstates the Herald check. Soften it in the 1.2.1 notes.
3. **RvR routes:** see [RVR-RALLY-DIAG.md](RVR-RALLY-DIAG.md) and FIX-LIST.md. Fix them spot by spot only, and
   never rebuild the navmeshes globally:
   - Castle Sauvage → Caer Benowyc, where there is no route even after the restart;
   - Midgard bots logging in at an invalid spot in Cruachan Gorge;
   - a repeat stuck spot on a hill in Connacht;
   - Midgard's defence of Dun nGed marching with 0 bots;
   - the other loops at region-200 seams.
4. **/faction pace:** it averages about 4.9 lines a minute, but some minutes drop to 2. Make the floor of 5 hold
   minute by minute.
5. **Small fixes:**
   - the casting null check for a missing `LosChecker.Out` client;
   - the launcher caption "HOW MUCH THEY TALK" (`SocialExpansionSettingsControl.cs:150`) should match the docs'
     "How much bots talk";
   - the stock merchant item templates that are missing at startup;
   - the QueuePacket "DrainTo in progress" error.

## Build and test

- Build: `dotnet build source/server/Tests/Tests.csproj -c Debug`.
- Test: `dotnet test source/server/build/Tests/Debug/lib/Tests.dll`. The full suite is about 9,200 tests, and 0
  should fail.
- Python tools: `python3 -m unittest discover -s source/tools/tests`, and the tests under
  `tools/bot-voices` and `tools/release`.
- Don't hand-edit `corpus.json` or `docs/bot-voices/DEBATES.md`. Regenerate them with
  `tools/bot-voices/build_corpus.py` and `debates_doc.py`.
- CI also checks `ALL SERVER COMMANDS.txt` (`make_command_reference.py`) and the epic encounter catalog
  (`epic_encounter_catalog.py`).
- Versions: `tools/release/check_versions.py`. The 1.2.1 heading goes in `CHANGELOG-LIVING-SERVER.md` as
  `## 1.2.1 (in development)`.

## The owner's rules that bind this work (from AGENTS.md and the 1.2 work)

- Nothing goes to `main`, gets tagged, is published or is posted without the owner's explicit OK.
- **"Tests only"** until something is seen in a log on the owner's PC. Name the build behind every result.
- Never ship or commit played databases, `account.txt`, logs, bot rosters or `serverconfig.xml`.
- **Never weaken bosses, guards, lords or keep levels.** The one exception is the battleground keep levels the owner
  asked for, which are done.
- Living Server features default on.
- No Easter eggs in marketing. It's an unofficial fan project, with no EA or Mythic marks.
- **On the owner's PC:**
  - work only in the test copy `C:\Games\OfflineDAoC\playable-v0.37b-fresh`;
  - never touch the main install `playable-v0.37b`;
  - look only inside `C:\Games\OfflineDAoC`;
  - never open Libation, and never click GET UPDATE;
  - the test copy's bot schedule is Real clock.
