# wayback-fetch

A slow, polite downloader of archived 2002-2005 web pages from the Internet Archive's Wayback Machine, for the
Living Server's era research: Camelot Herald server, realm war and statistics pages, guild rosters, and raid threads
on Freddyshouse, VN Boards and Allakhazam.

It runs on a Windows PC with **Windows PowerShell 5.1 only**. Nothing to install, no admin rights. The cloud build
container cannot reach web.archive.org, so the tool runs on the owner's PC and leaves its results in a folder.

## Use it

1. Copy this whole folder to `C:\Games\OfflineDAoC\wayback-fetch\`.
2. Double-click `RUN-WAYBACK-FETCH.cmd`. It runs every job in `jobs\`, in name order.
   To run one job, drag its `.json` file onto `RUN-WAYBACK-FETCH.cmd`.
3. Close the window any time. Run it again later and it continues where it stopped: saved pages and finished index
   pages are not requested again.

Results go under `out\`:

| Path | What it is |
|---|---|
| `out\<job>\<host>\<hash>_<timestamp>.html` | The raw archived page, byte for byte, without the Wayback toolbar. `<hash>` is the first 16 hex digits of the SHA-256 of the page's path and query. |
| `out\<job>\index.csv` | One row per saved page: saved path, original URL, capture time, bytes, SHA-256, Wayback URL. For an index-only job: one row per capture found. |
| `out\<job>\manifest.jsonl` | One line per fetch outcome (`saved`, `missing`, `blocked`, `too-large`, `out-of-scope`, `error` (tried again next run), `failed`). This is what makes a run resumable. |
| `out\<job>\index\` | The CDX index pages already read, and where paging stopped (`resumeKey`). |
| `out\_logs\run-*.log` | One log per run. The last line is `SUMMARY ...`: jobs, indexed (captures in scope), fetched (saved this run), skipped (already done), failed (not saved: missing, blocked, out of scope, errors), MB, requests, today's count. |
| `out\_state\requests-<date>.txt` | Today's request count, for the daily cap. |

## Politeness rules (built in)

- **One request at a time**, at least `delaySeconds` (default 3) between the starts of two requests. The script never
  goes below 1 second against the real archive. A second copy started in the same folder refuses to run.
- **Back-off** on HTTP 429, 408, 5xx, connection resets and timeouts: 10, 20, 40, 80, 160 s (capped at 600 s), and
  never shorter than the server's `Retry-After`. The run **stops** instead of pressing on when 429s outlast 5
  retries, when `Retry-After` asks for more than 15 minutes, or when 3 addresses in a row keep failing. One address
  that keeps failing on its own (a broken capture, a too-heavy index query) is skipped for that run and tried again
  next run; a page that fails 3 runs is marked `failed` and left alone.
- **Daily cap**: 1500 requests per calendar day (index pages, page downloads, retries and redirects all count).
- **Generic User-Agent** with no name, email or URL in it.
- **Index-driven only**: pages come from the archive's CDX index for the job's own URL patterns. Links inside pages are
  never followed. Each index row is checked again on the PC against the job's pattern, years, and filters before it
  is fetched; Wayback redirects are followed only to the same page at another capture time inside the job's years.
- Pages are fetched through the `id_` form (`https://web.archive.org/web/<timestamp>id_/<original>`), so the archive
  returns the original bytes without rewriting them.

All of these are in `wayback-fetch.config.json`. Keep `delaySeconds` at 3 or more.

## Add a job

Create `jobs\<name>.json`. Every field except `targets` and `url` is optional.

```json
{
    "name": "d-herald-merlin-guild-x",
    "note": "Why we want these pages (always fill this in).",
    "from": "2002",
    "to": "2005",
    "mimetypes": ["text/html"],
    "exclude": ["/(chars|guilds)/"],
    "maxCaptures": 100,
    "indexOnly": false,
    "targets": [
        {
            "url": "camelotherald.com/guilds/",
            "matchType": "prefix",
            "guess": true,
            "note": "Why this pattern; mark guessed patterns with guess: true.",
            "cdxFilters": ["urlkey:.*merlin.*"],
            "collapse": "urlkey",
            "cdxPageSize": 200,
            "cdxMaxPages": 2,
            "maxCaptures": 30
        }
    ]
}
```

- `matchType`: `exact` (one URL), `prefix` (everything under a path), `host` (one host), `domain` (a host and its
  subdomains). Don't use `*` in `url`.
- `cdxFilters`: CDX filters, `field:regex` or `!field:regex`, full-match Java-style regexes. Fields: `urlkey` (the
  lower-cased URL key, best for keywords), `original`, `mimetype`. They are sent to the archive and checked again
  locally.
- `include` / `exclude` (job or target): .NET regexes on the original URL, checked locally only.
- `collapse`: `urlkey` (default: one capture per distinct URL, the earliest in the years), `timestamp:6` (one per
  month, good for following one page through time with `exact`), or `none`.
- `cdxMaxPages` x `cdxPageSize` caps how much of the index is read; `maxCaptures` caps downloads per target, and the
  job-level `maxCaptures` caps the whole job.
- `indexOnly: true` lists what exists in `index.csv` without downloading. Use it to learn a site's real URLs first.
- Forum URLs carry thread IDs, not titles. Fetch the board listings first, read the titles, then add the threads you
  want as `exact` targets.
- Editing a target's query starts that target's index again; already saved pages are still skipped.

## Starter jobs

| Job | What it is for |
|---|---|
| `a-raid-sizes` | Raid sizes: board and guide listings (with thread titles) on forums.freddyshouse.com, vnboards.ign.com and camelot.allakhazam.com, plus a small sample of thread pages. |
| `b1-herald-map` | Index only: every distinct camelotherald.com URL archived 2002-2005 outside `/chars/` and `/guilds/`. Read its `index.csv` to correct the guesses below. |
| `b2-herald-pages` | Herald front page (monthly), New Frontiers pages, and keyword searches for statistics, realm war/server and patch-note pages. |
| `c-herald-guilds-chars` | A small first sample of Herald guild and character pages, for real guild sizes and class mixes. |

A first run of all four makes roughly 700 requests at most, about 35-40 minutes at 3 seconds each.

### What is known and what is guessed about the old URLs

Checked by web search (the archive itself can't be reached from the build container):

- `www.camelotherald.com/guilds/` existed: Mythic opened the per-guild database there in February 2002 (GameBanshee
  news, 25 Feb 2002: every guild has a page listing members and how often they log in).
- `www.camelotherald.com/newfrontiers/realmabilities/abilities.php` existed in May 2004 (linked from GameBanshee's
  1.69 patch notes).
- `camelot.allakhazam.com` uses `db/guides.html?guide=N`, `forum.html?forum=N` and `story.html?story=N` today; a
  February 2002 story (number 703) is still reachable at that address. That the 2002-2005 site used the same paths
  is a guess.

Guessed, and marked `"guess": true` in the jobs:

- the Herald's `/chars/` path, and whether a server name (Merlin, Percival) appears in guild or character URLs
- Herald statistics, realm war, server and release-notes pages (searched by URL keyword because the paths are unknown)
- Freddyshouse's vBulletin URLs of the time (`forumdisplay.php`, `showthread.php`); today's XenForo URLs are later
- VN Boards URLs that name Camelot; the old `board.asp?brd=N` / `message.asp?topic=N` URLs carry no name

## Third-party content

The fetched pages belong to their original authors and sites. They are research material only: **never commit
`out\` or any fetched page**, and don't ship them in a package. `tools/wayback-fetch/out/` is in `.gitignore`.
Facts we take from them (raid sizes, guild sizes, class mixes) go into our own words and data.

## Tests

`tests\run-tests.ps1` runs unit tests (CDX JSON parsing, query building, scope checks, path hashing, pacing,
back-off, Retry-After, daily cap, manifest resume, job validation) and an end-to-end run of the real script against
`tests\mock_archive.py`, a local stand-in for the archive (needs Python 3; skipped without it unless `-RequireE2E`).

```
powershell -NoProfile -ExecutionPolicy Bypass -File tools\wayback-fetch\tests\run-tests.ps1
pwsh -NoProfile -File tools/wayback-fetch/tests/run-tests.ps1
```

The `wayback-fetch` GitHub workflow runs them in Windows PowerShell 5.1 and in PowerShell 7. None of the tests
contact web.archive.org; only a run on the PC proves the real archive's answers.

Keep `wayback-fetch.ps1` ASCII and free of PowerShell 7-only syntax.
