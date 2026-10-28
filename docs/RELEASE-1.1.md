# Beta 1.1 release plan

Beta 1.1 collects every branch in progress into one release. Each branch merges into `release/1.1` by pull
request; `release/1.1` goes to `main` (and is published) once it has been built, tested and played.

## Branches

| Branch | Pull request | What it brings | State (2026-10-28) |
|---|---|---|---|
| `feature/talking-update` | #28 | The talking update; the issue #25 stall fix (064a387) | Merged at `29fdca8`; still being worked on, merge again for later commits |
| `fixes/fork-picks` | #27 | Fixes from other forks: effect leak, siege expiry, healer casts, low-power Animists, medallion full bag, siege weapon owner | Merged; CI passed |
| `upstream/0.38-server` | #30 | Offline DAoC 0.38's server fixes; a build can fit several releases | Merged; CI passed |
| `bots/stefanrows-routes-reactions` | (pushed) | stefanrows route-failure fixes; bots react to attacks like players | Merged; tests pass locally |
| `quests/hearth` | (pushed) | HearthDAoC quest fixes (name matching, deliveries, 25-quest journal), quest markers, epic chains | Merged; tests pass locally |
| `installer/all-in-one` | #26 | All-in-one themed Setup | Merged at `e543dfa`; still being worked on, merge again for later commits; not run by hand on a real PC |

Every branch above merged into `release/1.1` with no conflicts. The combined tree builds, and all 3,979 server tests
pass on Linux.

Branches left out on purpose: the `offer-0.37/*`, `upstream-fix/*` and `upstream-offer/*` branches (offers to
shadowofze, already in his 0.38 work where he took them) and the old `se`, `ci` and `living-world` branches.

## Rules for this release

- Feature branches don't edit `CHANGELOG-LIVING-SERVER.md` beyond their own work in progress. The Beta 1.1
  section is written once here, after the merges, crediting the forks each change came from and the reporter of
  issue #25.
- The base stays Offline DAoC **0.37 / 0.37b** unless shadowofze publishes 0.38 first.
- Automated tests are reported separately from in-game checks.

## Before release

- [x] Every branch above merged into `release/1.1`, conflicts resolved (merge the talking update and installer again for later commits)
- [x] One consolidated Beta 1.1 changelog section
- [x] Build name set in `ForkBuildInfo.cs` (`1.1-beta`)
- [ ] CI green on `release/1.1` (server, launcher and installer tests)
- [ ] Played on the owner's PC at the normal bot count. Watch sieges: their expiry timers now run on the live
      clock, so musters, marches, repels and the four-hour cap will fire where earlier builds skipped them.
- [ ] Reply on issue #25 once the release is out
