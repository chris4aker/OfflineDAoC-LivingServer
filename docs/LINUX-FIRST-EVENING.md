# Linux: first evening checklist (for Ryan)

This is the first real run of the Wine route (docs/LINUX.md §0) on a Linux desktop. It uses your existing setup, so
nothing has to be downloaded again. Keep the output of each step; `game/linux/logs/` collects most of it.

You need the test package `OfflineDAoC-LivingServer-1.2-beta-linux-test.zip` (Chris sends it) and PowerShell 7
(`sudo snap install powershell --classic` on Ubuntu). Your game is `~/OfflineDAoC/game` with the Wine folder
`~/OfflineDAoC/wineprefix`; change the paths below if yours differ.

**Before you start:** close the launcher, the server and the game, then copy `~/OfflineDAoC/game/runtime/data/` (your
world) somewhere safe.

## 1. Put the Linux script in the game

```bash
cd ~/Downloads && unzip -o OfflineDAoC-LivingServer-1.2-beta-linux-test.zip
mkdir -p ~/OfflineDAoC/game/linux
cp LivingServer-1.2-beta/linux/offlinedaoc LivingServer-1.2-beta/linux/daoc_fullscreen.py ~/OfflineDAoC/game/linux/
chmod +x ~/OfflineDAoC/game/linux/offlinedaoc
export WINEPREFIX=~/OfflineDAoC/wineprefix
~/OfflineDAoC/game/linux/offlinedaoc status
```
- [ ] `status` names the game folder, `running: nothing`, `port: 10300 (free)`, your Wine version, `prefix … (ready)`,
      and pwsh.

## 2. Install 1.2-beta over your 1.1-beta

```bash
~/OfflineDAoC/game/linux/offlinedaoc install ~/Downloads/OfflineDAoC-LivingServer-1.2-beta-linux-test.zip
```
- [ ] It ends with `Installed 1.2-beta: the server into 3 folder(s) …`.
- [ ] If it stops with `NOTHING WAS CHANGED:`, send that line. Your install script copied the 1.1-beta files, so the
      installer should recognise them as a fork build it installed.
- [ ] `offlinedaoc status` now says `installed: 1.2-beta`.

## 3. Play

```bash
~/OfflineDAoC/game/linux/offlinedaoc play
```
- [ ] The launcher opens and its title shows 1.2. START SERVER reaches online, and ENTER REALM gets you into the game.
- [ ] The game window is fullscreen and the bottom buttons are visible.
- [ ] The loading screen says LIVING SERVER.
- [ ] Play about 30 minutes, then note the server's memory: `ps -C CoreServer.exe -o rss=` (in KB). If this build has
      the Options tab's **Lean server memory** switch, do one session with it off and one with it on.
- [ ] Quit the game, stop the server in the launcher, and close it.

## 4. Host for Friends (with Chris)

- [ ] In the launcher, open HOST FOR FRIENDS and START HOSTING. There should be no Windows permission prompt; the status
      line shows the `sudo ufw allow …` commands.
- [ ] Chris joins with the invite code. Does he reach the world?
- [ ] STOP HOSTING says hosting stopped.

## 5. Update and rollback

```bash
~/OfflineDAoC/game/linux/offlinedaoc update --check
~/OfflineDAoC/game/linux/offlinedaoc rollback
~/OfflineDAoC/game/linux/offlinedaoc status
```
- [ ] `--check` names the newest Living Server release.
- [ ] `rollback` ends with `Rolled back 1.2-beta`, and `status` names the build you had before (or `unknown`).
- [ ] Install 1.2-beta again (step 2) to keep playing it.

## What to send back

- `~/OfflineDAoC/game/linux/logs/` (the install and rollback logs, launcher.log, fullscreen.log)
- the newest server log (in `~/OfflineDAoC/game/runtime/logs/` or `runtime/server/logs/`)
- any step that didn't match, with what you saw instead
