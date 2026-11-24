# Realism audit, 2026-11-23/24: chat, interactions and movement

Internal working doc. The owner asked for "a full audit of chat and bot interactions and bot movement ... to make the bots
as real as possible. Chat is the first and larger priority", after an hour on 1.2.2 showed nine chat defects that the
/faction bench (90%) could not see. Seven report-only audits ran on main + #469 (`5346e5f7`). **Everything in these
reports is tests only**: code reading, counts and CI simulations. Only the nine screenshot defects were seen in game.

The work is tracked in the issues listed in the epic ("Bots that pass for players"). Each issue names the report and the
section it comes from.

## The reports

| File | What it covers |
|---|---|
| [structure.md](structure.md) | Why the defects keep coming: 17 exits, 7 reply orders, 9 memories. The target structure (say gate, one conversation record, line contract, one planner, one lookup) and how to measure by real play. **Read this first.** |
| [questions.md](questions.md) | 623 fresh player lines: 39% good against the bench's 90%. Topics with no knowledge. A proposal for 260 bench lines and an 80-line holdout. |
| [channels.md](channels.md) | Every channel's reply path. Per-channel scores; "dunno" removed from /faction only; hired companions in /say (3%); follow-ups lose their subject. |
| [evenings.md](evenings.md) | 18 simulated two-hour evenings read cold: banter runs dry, /say filler, the player as spectator. |
| [lines.md](lines.md) | Every written line: two voices, one reply pool for all channels, emotes by dice, pools too small, later-era words. The 100 worst lines, 14 linter rules, 30 rewrites. |
| [interactions.md](interactions.md) | Invites, camps, orders, promises, emotes, trades, tells, logouts. |
| [movement.md](movement.md) | The 15 tells that give a bot's body away, and the idle-life design. |

## Where the audit's tools are

Not merged; kept on branches (draft PRs #470, #471, #472 are closed):
- `audit/1.2.3-questions`: `tools/bot-voices/eval/audit_fresh_questions.json` (623 lines), `eval/audit/` (`judge.py`,
  `judged.json`, `proposal.json`), `UT_AuditFreshQuestions.cs`.
- `audit/1.2.3-channels`: per-channel, follow-up and "lines of the moment" tests in `UT_ChatQuestionBench.cs`.
- `audit/1.2.3-evenings`: `UT_EveningTranscripts.cs`, `tools/bot-voices/eval/evening_read.py`.

Paths under `/tmp/audit2/` in the reports were the audit's scratch space and are gone; what mattered is in these files
and on those branches.

## What the seven reports agree on

1. **The measuring was wrong.** The bench asks single questions in /faction on a stub world, written by the process that
   wrote the fixes. Fresh lines score 39%.
2. **The structure causes the defects.** A rule added in one path is missing in the next channel or pool.
3. **The written lines are in the wrong voice**, and none says where, to whom or after what it may be said.
4. **Acts without a person, words without an act.** Invites, guild joins and logouts are instant; "roger", "brb" and
   "omw" change nothing.
5. **Bodies are statues**, most of all the bots in the player's own group.

## The plan

- **Step 1, record real play** (no behaviour change): the say gate as a pass-through in every exit, the chat log, `/odd`,
  the transcript tool, and an honest bench. The number to drive down is **defects per hour of real play** (baseline: 9 in
  about an hour on 1.2.2).
- **Step 2, chat fixed by class at the gate**: no non-answer as a whole answer in any channel; one reply path, memory and
  delay for all channels; follow-ups; line contract; knowledge gaps; pools and reactions; the period voice.
- **Step 3, interactions.**
- **Step 4, movement**: one idle-life layer, the player's own group first.

Fix a whole class where the structure review says it belongs; patch only true one-offs.

## Owner decisions still open

- May an invite pull a bot out of its own group, or should it decline or finish its fight first?
- The "[Playerbot]" tag in `/who`: keep, hide, or a setting?
- Does a resting bot show as sitting on the original client? (A five-minute look on the PC.)
- Publish 1.2.3 with the fixes already on main (#465–#469), or hold for the drop tables and the PC check?
