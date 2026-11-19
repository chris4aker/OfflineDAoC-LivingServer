# Raid sources: period research and other code, for 1.2's raid plans

Collected 2026-11-14 when the owner put every big boss into 1.2 ("make sure we scrape every bit of period data ... and
other repos and forks"). These notes are summaries in our own words, each fact with its source, date and a confidence
level; raw pages are not kept in the repository. They feed the raid plans in `EpicEncounterTactics*.cs` and the hand
notes in `docs/encounters/hand-notes.json`. Where a period source and our boss script disagree, the bots play the script,
and the hand notes say how the script differs.

| File | What it holds |
|---|---|
| [wayback-corpus.md](wayback-corpus.md) | The project's own Wayback scrapes (tools/wayback-fetch), mined boss by boss |
| [wayback-gaps.md](wayback-gaps.md) | What the corpus lacks, with the URLs to fetch next |
| [wayback-pass2.md](wayback-pass2.md) | The second pass: FreddysHouse, Allakhazam, GameBanshee, Prima (2004) and labelled maps |
| [wayback-pass3.md](wayback-pass3.md) | The third pass: Krondon's orbs, the helper packs, 14 contradictions with our scripts and hand notes, and the Wayback job for every gap URL |
| [web-albion-df.md](web-albion-df.md) | Albion, Darkness Falls and neutral bosses from period and later sources |
| [web-midgard-hibernia.md](web-midgard-hibernia.md) | Midgard and Hibernia bosses from period and later sources |
| [code-repos.md](code-repos.md) | Other code: OpenDAoC upstream, the Offline DAoC forks, Dawn of Light, playerbots designs; what to take and the licences |

Not reached from the sessions that wrote these: pages that exist only on the Wayback Machine (the shell's proxy refused
them in pass 2, and WebFetch refused them in pass 3). The URLs are in wayback-gaps.md and wayback-pass2.md, and all of
them are now one job for the owner's PC tool, `tools/wayback-fetch/jobs/b-raid-gaps.json`.

**To fetch them:** on the PC, drag `jobs\b-raid-gaps.json` onto `tools\wayback-fetch\RUN-WAYBACK-FETCH.cmd`, then run
`PACK-RESULTS.cmd` and bring back `out\_packs\b-raid-gaps-<date>.zip` (one day's run; see wayback-pass3.md section 0).
