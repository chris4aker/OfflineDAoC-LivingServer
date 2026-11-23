# Living Server 1.2.2: known issues

What to expect, known problems and what to do about them. What changed in 1.2.2 is in
[RELEASE-NOTES-1.2.2.md](RELEASE-NOTES-1.2.2.md), and 1.2.1's changes are in [RELEASE-NOTES-1.2.1.md](RELEASE-NOTES-1.2.1.md).
Everything Living Server does is in [RELEASE-NOTES-1.2.md](RELEASE-NOTES-1.2.md), and every change is in
[CHANGELOG-LIVING-SERVER.md](../CHANGELOG-LIVING-SERVER.md).

**Coming next:** Uthgard's drop tables in **1.2.3**, and German and French (a language selector) in **1.3**.

**If dead bots look wrong in the original client,** turn off **Bots fall when they die** (`bot_death_animation`) in
the Living Server tab. Dead bots then behave as they did in 1.2.

## Epic raids

Bot raids fight the bosses by the bosses' own rules, and the bots don't win every fight.

- **Fights the bots can still lose:**
  - Summoner Roesia, and Grand Summoner Govannon after her (Summoner's Hall, all three realms);
  - King Tuscar (Tuscaran Glacier);
  - the Green Knight and Marfoirwort;
  - a dragon fight can fall apart partway (Golestandt, Cuuldurach).
- **Not played in full yet:** at Legion the raid doesn't use his platform, the way down or his add waves.
  **Behemoth** is never fought by bots, and **Fomor City** and **Morgana** have no bot raid.
- **Xanxicar's stomp** is learned over a realm's attempts, so early raids lose to it more often.
- **Raids need the bots.** A raid's full size is half the realm's free level 50s, up to its period size, and any
  automatic raid needs 80 level-50 bots of the realm online.
- **Too hard on your world?** Each restored boss rule is a server setting of its own and can be switched off on its
  own.
- **Don't turn on `epic_earthmender_mutual_heal`:** with it on, the four Earthmenders heal each other and the bots
  can't win.

## RvR and battlegrounds

- **Battleground keeps change hands only now and then.** Bots storm the keep lord often and fail more than they win.
- **Roaming eight-man groups** can patrol for a while without finding a fight.
- **Some gamebots wait a long time at frontier teleporters** before they set off.
- **In dense warbands a regroup call can reach a neighbouring group** as well as its own.

## Travel and the world

- **Between Lough Derg and Bri Leith** (Hibernia), a bot can loop at the zone border and end up sent home to its capital.
- **A bot can search for a camp for minutes,** mostly level 50s at Tir na Nog's bind. It gives up after 3 minutes and
  picks again.

## Chat

- **/faction runs at about 5 to 7 lines a minute on a full realm, by design,** and never more than 7. If that's too
  much, lower **How much bots talk** in the Living Server tab.
- **Replies guess from your words.** A bot only knows what you type, so a bot that remembers you may still ask your
  level right after you said it.
- **"omw" means coming:** a bot that says it has to go gives a deadline. If it never answers, it may walk off for up to
  about three and a half minutes.
- **Bot parties take new members on the way only now and then.** A party that set off with a free seat looks for one
  more at times along the way, and takes one member at a time.

## Bots that play with you, and companions

- **Bot-led groups:** gamebots don't share your horse or boat. The leader takes its own and waits at the other end.
  The break at the end of a session ends the party.
- **A bot that offers to group may walk on before you meet.** If it's too far away when you invite, it tells you where
  it is.
- **Selecting bots:**
  - Dead bots can't be clicked in the original client. Use `/target <name>`, or cast a resurrection with nothing
    selected: it goes to a dead bot nearby.
  - If clicking a living bot doesn't select it, `/target <name>` always works.
- **Darkness Falls stairs:** companions follow you back up the entrance stairs one flight at a time. If one still
  stays below, GM `/jump <name> to me`.
- **GM `/jump` and dead bots:** a dead bot can't be jumped, because its body stays where it fell. Raise it there, or
  wait for it to release, then jump it.
- **Hired companions:** the healer's [companions] and [hire] links only work with the healer targeted, but typed `/hire`
  commands always work. The gear window's title reads "House Vault 0" because it reuses the house vault window.

## Crafting and the economy

- **Crafting as a player** needs the original client.
- **Few crafted pieces sell on the Exchange,** because most bot crafting is practice pieces.
- **Crafter bots:** if a stock trade window can't open, the crafter says why and keeps the deal. A finished
  commission is handed over during the crafter's break, or it tells you where to collect it.

## Lore and language

- **Lore, bot chat and menus are in English.** German and French come with the language selector in 1.3. The original
  client's own menus will stay in English.

## Play with friends (experimental)

- It needs the original client. The new client can't use Join Friend yet.
- It's still experimental: if a join or a character passport trip goes wrong, please report it (below).

## Launcher, install and updates

- **The version label** reads "LIVING SERVER 1.2.2". It used to read "VERSION 0.37"; the game underneath is still
  Offline DAoC 0.37.
- **The bot schedule is off by default:** the whole roster is online. A schedule you chose yourself is kept.
- **A maximized launcher can ignore START SERVER, STOP SERVER or ENTER REALM** when another window sits over those
  buttons. Use the launcher at its normal size, or move the other window.
- **Releases marked as pre-releases are never offered** by the launcher's update check.
- **Updating a 0.36 game with the Social Expansion:** run `PREPARE-FOR-UPDATE.cmd` from the package first.
- **Windows SmartScreen** may warn that Setup isn't code-signed. Click More info, then Run anyway.

## The server

- **A very busy server can pause for a few seconds,** most often as raids end.
- **Very long sessions:** Windows handles grow slowly, by about 1,700 an hour with 600 bots a realm. Restarting the
  server now and then clears them. The server's own memory stays flat.
- **Harmless lines at startup:** one warning that counts the stock world's missing merchant item templates, and one
  `LOOT_ITEMS_MISSING` line per loot source. Neither affects play.

## New client (0.37c)

- No crafting and no Join Friend.
- APPRAISE stays hidden, and you don't look see-through to yourself in Stealth yet.
- No guild window. `/face` keeps facing your target until you type it again, and a bow fires from the Ranged Weapon key.
- Added the new client after installing Living Server? Run `INSTALL.cmd` again.

## Always the case

- **A fresh world needs bots.** Use **+ Lv.1**, **+ Lv.20** and **+ Lv.50** in the launcher. A realm needs 8 bots of
  level 5 or higher before it forms a bot guild.
- **Bot guilds stay in your world when you turn them off.** To remove them, close the game and server, then run
  `python tools\claude-version\set_bot_guilds.py --purge --apply` in your game folder. It backs up your database first.
- **`ROLLBACK.cmd` doesn't remove saved data.** It puts back the files the install replaced. New tables and settings
  stay, and the original server ignores them. Run it once for each build you installed.
- **Versions:** Living Server is for Offline DAoC 0.37, 0.37b and 0.37c only. 0.37 stays the base game, and later fixes
  come in Living Server releases.
- **The server only listens on your own computer** unless you host for friends. If your settings let other computers
  in, the log warns at every start (`NETWORK_EXPOSURE`).

## Reporting a problem

Open an issue on [GitHub](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues) and include:

- the `FORK_BUILD` line from the top of `runtime\server\logs\server.log` (it names your build and which features are on);
- what you were doing, roughly when, and the bot or place involved;
- the log around that time, if you can. `server.log` is in `runtime\server\logs`. For a raid, the `REALM_RAID_SIZED`
  and `REALM_RAID_ENDED` lines help most. For a bot you couldn't select, the `PLAYER_CLICK_TARGET` lines help most.
