# Upstream watch

Living Server is built on [OpenDAoC-Core](https://github.com/OpenDAoC/OpenDAoC-Core) (GPL-3.0, like us). Upstream keeps
fixing combat, spells, pathing, saves and leaks, so we check it every week instead of once in a while (owner,
2026-11-24: "We should definitely be checking OpenDAoC way more often then for fixes").

## How it works

- `.github/workflows/upstream-watch.yml` runs every Monday (and by hand from the Actions tab).
- `source/tools/upstream_watch.py` lists each watched repo's commits since its `reviewed` commit in
  `.github/upstream-watch.json`, grouped as likely gameplay fixes, likely features and the rest (by keywords only).
- The workflow keeps **one open issue per repo**, titled "Upstream watch: <repo>" with the label `upstream`, and comments
  when new commits arrive.

## Reviewing (a person or an agent)

1. Read the issue. For each likely fix or feature, check whether Living Server already has it (grep our code).
2. Port what's worth it on its own branch and PR, by hand where our code has diverged. Autonomous gamebots are
   `GameNPC`s, so an upstream "NPC" rule change must keep our gamebot exemptions (see AGENTS.md: tell bot types apart).
   Anything that weakens monsters, bosses, guards or keeps needs the owner's decision (AGENTS.md).
3. Move `reviewed` in `.github/upstream-watch.json` to the last commit you went through, and close the issue.

## History

- 2026-11-24: full harvest of 632 upstream commits from our base (about `d51233cb`, 2026-03-13) to `428927b33`
  (2026-11-23); the ranked picks and the issues filed from them are linked from the harvest issue.

To watch another repo (a fork, shadowofze's projects), add an entry to `.github/upstream-watch.json`.
