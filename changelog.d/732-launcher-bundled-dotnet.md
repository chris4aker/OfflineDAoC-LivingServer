---
version: 1.2.3
section: Setup and launcher
---
- **START SERVER uses the game's own .NET, and says so when the server closes at once** (#732). The launcher now
  points the server at the .NET in the game's `tools\dotnet` folder itself, the same way `START OFFLINE DAOC.cmd`
  and its shortcut do, so START SERVER works even when the launcher's exe was opened directly on a PC without .NET 10.
  When the server closes within 10 seconds of START SERVER, a message gives the exit code and points at the end of
  `runtime\logs\server-console.log`, or, when the game's `tools\dotnet` folder is missing, says to reinstall the game
  or extract the whole download again. The same line goes to `server-console.log` and `launcher-ui.log`; before,
  nothing happened. `BundledDotnetTests`. **Tests only.**
