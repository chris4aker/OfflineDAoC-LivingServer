<!-- AGENTS.md: open this as a draft at your first commit, push every checkpoint, run tools/check.sh before each push,
     and add a changelog.d/<issue>.md fragment instead of editing the changelog. -->

## Issue

Closes #<!-- issue number; one issue per piece of work (AGENTS.md "an issue before building") -->

## Period sources

<!-- For any gameplay change (tactic, encounter mechanic, behaviour, number): the period sources and upstream code read
     first, with links (Camelot Herald / Wayback, period guides, patch notes, OpenDAoC-Core, DOL, the forks).
     "None: no gameplay change" if it changes none. -->

## Owner decisions this needs

<!-- Each decision, asked on the issue before building. Any listed: the issue carries `needs-owner`.
     "None" if none. -->

## Tests vs in-game

<!-- Keep them apart (AGENTS.md "Be honest in reports"), and name the build each result came from.
     - Automated: what ran locally (targeted tests, tools/check.sh) and what CI runs.
     - In game: seen in one of the owner's PC logs (which build), or "not yet: tests only" (label `tests-only`,
       `needs-PC` if a PC check is wanted). -->

- Automated:
- In game:

## Reruns owed

<!-- Simulations, sweeps or benches whose numbers this change makes stale and that must be run again on this build
     (label `reruns-owed`). "None" if none. -->
