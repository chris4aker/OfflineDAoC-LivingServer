# wayback-fetch

A slow, polite downloader of archived 2002-2005 web pages from the Internet Archive's Wayback Machine, for the
Living Server's era research: the Camelot Herald, Allakhazam, Classes of Camelot, Mythic's official site, and the
Freddyshouse, VN Boards and Allakhazam forums.

## What we use this for

Accurate period **lore, vocabulary, builds and specs** for the classic, Shrouded Isles and early Trials of Atlantis
era (2002-2005), for the Living Server's bots and text:

- **builds and specs:** real era templates (spec points by level, common realm ability picks, the lines people
  actually trained) to replace guessed build weights, each with the patch it belongs to
- **patch notes:** what changed when (specs, realm abilities, class balance, the SI and ToA dates)
- **vocabulary:** how players talked: LFG and trade posts, RvR and raid calls, guild recruitment, class slang
- **lore:** realm and class backstory, the Herald's lore pages, quest and book text

It is **not** for importing historical data: no character, roster or population harvesting. Pages stay on the PC.
We take facts, short quotes and statistics from them into our own data and words, and never republish the pages.

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
| `out\<job>\index.csv` | One row per saved page: saved path, capture time, `patch` (up to three patch versions the page names, such as `1.65;1.66b`), original URL, bytes, SHA-256, Wayback URL. For an index-only job: one row per capture found. |
| `out\<job>\manifest.jsonl` | One line per fetch outcome (`saved`, `missing`, `blocked`, `too-large`, `out-of-scope`, `error` (tried again next run), `failed`). This is what makes a run resumable. |
| `out\<job>\index\` | The CDX index pages already read, and where paging stopped (`resumeKey`). |
| `out\_logs\run-*.log` | One log per run. The last line is `SUMMARY ...`: jobs, indexed (captures in scope), fetched (saved this run), skipped (already done), failed (not saved: missing, blocked, out of scope, errors), copied (taken from another job's folder), MB, requests, today's count. |
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
- **Never twice**: a capture another job in `out\` already saved is copied from there (checked by SHA-256), not
  downloaded again.
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
- `dedupeKey` (job or target): a .NET regex on the lower-cased URL; captures whose first group (or whole match) is
  the same on the same host are fetched once, e.g. `[?&](?:t|threadid)=(\d+)(?:&|$)` for one capture per thread
  whatever session id its URLs carry.
- **The archive's index sorts query parameters.** `archives.php?t=game&sortby=date` is indexed as
  `archives.php?sortby=date&t=game`, so a `prefix` on `archives.php?t=game` finds nothing. Use a prefix on the path
  and a `cdxFilters` entry on `urlkey` for the parameter (as `2-patch-notes` does). `exact` targets may list
  parameters in any order.
- A domain-wide query lists URLs alphabetically and filters scan the whole domain: the first real run's domain map
  stopped inside `chardisplay.php`, and its domain-wide keyword searches got HTTP 503/504. Prefer one `prefix`
  target per section or per first letter.
- Forum URLs carry thread IDs, not titles. Fetch the board listings first, then let `MAKE-THREAD-JOBS.cmd`
  pick the threads (below), or add the ones you want as `exact` targets.
- Jobs run in file-name order: start a name with a digit to run it before the `a-...` jobs.
- Editing a target's query starts that target's index again; already saved pages are still skipped.

## Starter jobs

They run in this order (file-name order):

| Job | What it is for |
|---|---|
| `1-site-map` | Index only, cheap: the distinct URLs archived 2002-2005 on the Herald (one prefix range per letter a-z, `c` without `chardisplay.php` and `/calendar/`), Mythic's `darkageofcamelot.com` and `classesofcamelot.com`. Its `index.csv` shows the real class, realm ability, lore and news URLs, to fill jobs 2-5 on the next pass. |
| `2-patch-notes` | The Herald's game news and patch archive (`archives.php?t=game`), `article.php` pages, the front page once a month, Allakhazam news stories, and a release-notes guess. |
| `3-builds-specs` | The Herald's New Frontiers realm ability pages, Herald class pages (guess), Allakhazam class, skill and guide pages, one `<Class>_Guide.html` lookup per classic and SI class (once a year, so each patch era shows), Classes of Camelot, and VN Boards class boards. |
| `4-vocab` | Allakhazam forum threads, VN Boards and boards.ign.com server, trade, guild and RvR boards, the Herald's community archive, and 10 guild pages for recruitment wording and rank names only. |
| `5-lore` | The Herald's beginner's guide and lore/realm pages (guesses), Allakhazam quest pages with their text, and a first sample of the official site. |
| `a-raid-sizes` | The first run's forum listings and guides (Freddyshouse, VN Boards, Allakhazam). Already saved on the PC; a run of it makes no new requests. Its listings feed the thread jobs. |
| `a2-raid-threads`, `a3-build-threads`, `a4-talk-threads` | Not in the folder at first: `MAKE-THREAD-JOBS.cmd` writes them from the saved listings (below). |

A run of jobs 1-5 makes at most about 1,400 requests, a little over an hour at 3 seconds each. With the threads it
goes past the daily cap of 1,500: the run stops cleanly and continues the next day.

### Thread jobs (a2, a3, a4)

1. Run the fetcher, so `out\a-raid-sizes\` and `out\4-vocab\` hold forum listings.
2. Double-click `MAKE-THREAD-JOBS.cmd`. It reads the saved listings (offline: no requests) and writes three jobs,
   one `exact` target per thread whose title matches:
   - `a2-raid-threads` (raid): dragon, Gjalp..., Golestandt, Cuuldurach, Legion, Govannon, Apocalypse, Olcasgean,
     Tuscar, Caer Sidi, Galladoria, epic, raid, Darkness Falls, `DF`
   - `a3-build-threads` (builds): spec, template, respec, build, realm abilities, spellcraft, `RA`, `RR5`, `SC`
   - `a4-talk-threads` (talk): LFG, WTS/WTB, recruiting, guild, RvR, zerg, stealth, buffbot, Emain, Odin's,
     Hadrian's, relics, keep takes, `INC`

   Capitalised abbreviations only count in capitals. Each job's `threads` list shows the titles, where each was
   listed, and how it is looked up. At most 150 threads per job (`-MaxThreads`).
3. Double-click `RUN-WAYBACK-FETCH.cmd` to fetch them.
4. Double-click `MAKE-THREAD-JOBS.cmd` again. vBulletin links carry a session id (`s=...`) that changes per visit,
   so a thread may be archived under another one. Threads whose exact lookup found nothing move into id-filter
   targets (25 threads per lookup, page 2+ and last-post links left out, one capture per thread). Then run the
   fetcher again.

The generated jobs hold third-party thread titles: they are in `.gitignore` and never committed.

### What is known and what is guessed about the old URLs

Seen in the first real run on the PC (its Herald map, November 2026), in the archive's index for 2002-2005:

- `/chardisplay.php?s=<Server>&c=<id>`: 8,622+ character captures (Lancelot 1,494, Morgan 1,387, Guinevere 1,361,
  Merlin 1,235, Percival 729, then about 160-250 each for Gawaine, Galahad, Tristan, Igraine, Bedevere and others).
  There is no `/chars/` path. (Character pages are not fetched: we don't import historical data.)
- `/calendar/...` (about 1,070), `/article.php?id=` (95), `/archives.php?t=game|community&sortby=...` (news and patch
  archive), `/characters/` (65), `/author-results.php?author=`, and single pages such as `beginnersguide`,
  `accountsplittingfaq`, `atlantisapp` and `catacombs_beta`. That map stopped inside `c`, so nothing after it is
  known yet.

Checked by web search (the archive itself can't be reached from the build container):

- `www.camelotherald.com/guilds/` existed: Mythic opened the per-guild database there in February 2002 (GameBanshee
  news, 25 Feb 2002: every guild has a page listing members and how often they log in).
- `www.camelotherald.com/newfrontiers/realmabilities/abilities.php` existed in May 2004 (linked from GameBanshee's
  1.69 patch notes).
- `camelot.allakhazam.com` uses `db/guides.html?guide=N`, `forum.html?forum=N`, `story.html?story=N`,
  `db/class.html?cclass=N`, `db/quests.html?cquest=N` (and `quests.html?cquest=N`) and `<Class>_Guide.html` today;
  a February 2002 story (number 703, the 1.45D patch notes) is still reachable at that address. That the 2002-2005
  site used the same paths is a guess.
- `www.classesofcamelot.com`, a class-guide fansite, moved to that address in May 2002 (GameBanshee news, 28 May
  2002).

Guessed, and marked `"guess": true` in the jobs:

- the guild database's URL form under `/guilds/` (the map's `g` range will show it)
- Herald class, lore, realm and release-notes pages (`/class`, `/lore`, `/realm`, `/release` prefixes, each one cheap
  lookup) until the map shows the real paths
- VN Boards board names starting `Dark_Age_of_Camelot` and IGN's later `boards.ign.com` address
- Freddyshouse's vBulletin URLs of the time (`forumdisplay.php`, `showthread.php`); today's XenForo URLs are later
- VN Boards URLs that name Camelot; the old `board.asp?brd=N` / `message.asp?topic=N` URLs carry no name

## Third-party content

The fetched pages belong to their original authors and sites. They are research material only: **never commit
`out\` or any fetched page**, and don't ship them in a package. `tools/wayback-fetch/out/` and the generated
`jobs/a*-threads.json` are in `.gitignore`.
Facts we take from them (raid sizes, guild sizes, class mixes) go into our own words and data.

## Tests

`tests\run-tests.ps1` runs unit tests (CDX JSON parsing, query building, scope checks, path hashing, pacing,
back-off, Retry-After, daily cap, manifest resume, job validation, order-insensitive exact matching, `dedupeKey`,
what the starter jobs accept and refuse, patch versions, copying from another job, and `make-thread-job.ps1` on
canned listings with all three presets) and an end-to-end run of the real script against
`tests\mock_archive.py`, a local stand-in for the archive (needs Python 3; skipped without it unless `-RequireE2E`).

```
powershell -NoProfile -ExecutionPolicy Bypass -File tools\wayback-fetch\tests\run-tests.ps1
pwsh -NoProfile -File tools/wayback-fetch/tests/run-tests.ps1
```

The `wayback-fetch` GitHub workflow runs them in Windows PowerShell 5.1 and in PowerShell 7. None of the tests
contact web.archive.org; only a run on the PC proves the real archive's answers.

Keep `wayback-fetch.ps1` ASCII and free of PowerShell 7-only syntax.
