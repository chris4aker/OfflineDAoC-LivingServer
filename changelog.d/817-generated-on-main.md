---
version: 1.2.3
section: Tools
---
- **The log-row catalog and the Alloy allowlist are generated on main** (#817, owner decision 59A). Pull requests no
  longer commit `docs/LOG-ROWS.md` or `tools/grafana/alloy/config.alloy`, so two pull requests that each add a log row
  don't conflict. After each merge the `generated-files` workflow runs `tools/grafana/kit.py --write --generated` and
  proposes the result in the `bot/generated-files` pull request. `kit.py --check` still builds both files on every pull
  request but no longer compares them; `tools/grafana/generated_guard.py` fails a pull request that commits them; the
  filter tests read the allowlist built from the checkout's code.
