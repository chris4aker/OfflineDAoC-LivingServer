---
version: 1.2.3
section: The server
---
- **Heap snapshots say why they failed** (#731; tests only). rc.2's first snapshot (`gc-snapshots.on`, live data
  1,603 MB) ended with `dotnet-gcdump` exit code -1 and no file, and the server kept nothing the tool said. The server
  now reads the tool's stdout and stderr asynchronously into bounded buffers (the first and last 1 KB of each) and
  writes them on the `SERVER_HEAP_SNAPSHOT done` row after the existing `exit`, `file` and `exists`, with `exitHex`,
  `meaning` (`ok`, `tool-failed` for the tool's own -1, `host-failed` when the .NET host could not start it at all),
  `seconds` and the character counts. The start row adds the resolved `tool` path, the exact `command` and the
  server's `runtime`. A tool still running after 10 minutes is stopped and logged as `SERVER_HEAP_SNAPSHOT overran`
  with its output so far. The tool now runs with `-v` (its own timeline) and `--timeout 300`: its default 30-second
  limit, counted from its start, is one of the few ways it exits -1 without a file, and the only one that grows with
  the heap. Paths and the tool's output are dropped from shipped rows (`row-policy.json`). Snapshots stay off unless
  `runtime\server\gc-snapshots.on` exists. `UT_Issue731HeapSnapshotTool`.
