---
version: 1.2.3
section: Launcher
---
- **"Check for updates" answers when there is nothing new** (#795). Before, a check that found no update seemed to do
  nothing: its answer went only into a status line too small to show it. The launcher now shows a message box ("No newer
  version was found: you have Living Server 1.2.3.") or, when GitHub doesn't answer, says so and suggests trying again
  later. The update status line has a full-width row of its own under the version label, so every update message shows.
  The automatic check at start-up stays silent, and a found update still opens the Install / Later / Skip dialog.
