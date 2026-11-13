# Character passport: take your character to a friend's world and back

New in Living Server 1.2. Built by rambodolphin13 for his DividedRealms fork, and brought to Living Server with his
work credited (commits `da78651` and `da92848`). Thank you, rambodolphin13.

## What it does

- You can take one of your own characters to a friend's world.
- You play it there with your friend. It keeps its level, gear, quests and coin.
- When you are done, you bring it home. It brings back everything it earned.
- While it is away, it is locked in your world. Nobody can play it, delete it or move it there.
- A character is only ever playable in one world. That way nothing can be copied by travelling.

So far this is tested with automated tests only. Nobody has made the trip between two real PCs yet.

## Before you start

- Your friend hosts their world for friends (Host for Friends).
- You join their world once (Join Friend). That makes your account there.
- Log out of your friend's world again.
- Stop your own server. The passport only changes your world while it is stopped.

## Take a character to your friend's world

1. In the launcher, click **CHARACTER PASSPORT…** at the bottom right.
2. Type your friend's address, their login port (10300 unless they said otherwise), and your account and password in
   their world. The launcher does not save these.
3. Pick your character.
4. Choose how much coin to carry. By default the whole realm wallet goes with it.
5. Click **TAKE TO FRIEND'S WORLD**.
6. Join your friend's world. Your character is there.

If the name is already used in your friend's world, the launcher says so. Type a **visiting name** and try again. The
visiting name is only used in your friend's world. At home your character keeps its own name.

If your friend's world does not answer, your character stays locked at home. That is on purpose: your friend's world may
already have it. Click **TAKE TO FRIEND'S WORLD** again later. It sends the same passport again, never a second one.

## Bring it home

1. Log out of your friend's world.
2. Stop your own server.
3. Open **CHARACTER PASSPORT…**, type your friend's details, and click **LIST MY VISITORS**.
4. Pick your character and click **COME HOME**.

It comes home with its levels, items, quests and coin. If something goes wrong halfway, the launcher finishes the job
the next time you open the passport window.

Before your character can leave your friend's world:
- take back anything it has for sale at the Realm Exchange or in a house,
- collect any sale money waiting for it there,
- give up any house it owns there (visitors can't buy one).

## If your friend's world is gone: Recall

Pick the locked character and click **RECALL…**.
- It comes back exactly as it left, with the coin it carried.
- Everything it earned on that trip is lost.
- That trip can never come home later, even if your friend's world comes back.

Use Recall only when you can't reach your friend's world any more.

## For hosts

- Visitors are welcome while you host. To stop new visitors, untick **Friends may bring their characters to my world**
  in the passport window (your server must be stopped). Visitors who are already there can still go home.
- The passport desk listens on your login port + 10 (10310), only while you host. HOST FOR FRIENDS opens it in Windows
  Firewall with the game's ports (home network and Tailscale), and on the router when you host over the internet.
- A visitor is a normal character in your world. You can group, trade and guild with it.
- Your world makes no new accounts through the passport. A friend must join your world once first.
- Friends are trusted. A friend who edits a passport file could give a character anything, like a GM could.

## Backups

- Every step that changes a world backs that world up first, into `runtime\passport-backups`. The newest 6 are kept.
- Each step is all or nothing. If it fails, the world is left exactly as it was.
- The travel files themselves are kept in `%LOCALAPPDATA%\OfflineDAoC\passports`.

## What travels and what stays

A character's own record travels. Things that belong to one world stay in that world, at home and at your friend's.

**Travels with the character:**
- `DOLCharacters`: the character. Its account, slot, guild and house bind stay home and are put back when it returns.
- `DOLCharactersXCustomParam`: its settings. Realm Exchange sale money and the passport's own notes stay in their world.
- `CharacterXDataQuest`, `Quest`, `Task`: quests and tasks.
- `CharacterXMasterLevel`, `CharacterXOneTimeDrop`, `FactionAggroLevel`, `PlayerXEffect`, `TimeXLevel`.
- `Inventory`: what it wears, its backpack and its own vault. House vaults, consignment stock and Realm Exchange listings
  stay.
- `ItemUnique`: the special items it carries.

**Moved by a rule of their own:**
- `AccountXMoney`: the realm wallet is shared by all your characters. The coin you choose leaves your wallet as a purse
  and joins your wallet in the other world. Coin is moved, never copied.
- `AccountXCrafting`: crafting skill is shared too. Each skill keeps the higher value of the two worlds. It never goes down.

**Stays in its world:**
- `Account`, `AccountXCustomParam`: each world has its own accounts. No password or account name is in the passport.
- `DOLCharactersBackup`, `DOLCharactersBackupXCustomParam`: copies of deleted characters.
- `bot_profiles`, `bot_settings`: companions stay in the world where you made them. They wait for you there.
- `offline_world_bots`, `offline_world_bot_guild`, `offline_bot_guilds`, `offline_bot_guild_members`: that world's gamebots
  and their guilds.
- `offline_bot_memory`, `offline_bot_nemesis`, `offline_bot_storylines`, `offline_bot_bonds`, `offline_bot_ragequits`,
  `offline_bot_lines_heard`, `offline_bot_scenes_heard`: what that world's gamebots remember. They remember you again on
  your next visit.
- `offline_player_reputation`: your deeds and nickname are what that world's gamebots say about you. Each world has its own.
- `offline_lore_memory`: which lore lines you already heard in that world.
- `offline_server_firsts`: each world's own firsts.
- `offline_auction_listings`, `offline_auction_escrow`, `offline_auction_ledger`: the gamebots' own auction.
- `realm_exchange_sales`: that world's Realm Exchange sales.
- `Guild`, `GuildRank`, `GuildAlliance`: guilds. Your character leaves its guild for the trip and rejoins it at home.
- `DBHousePermissions`, `DBHouseCharsXPerms`, `DBIndoorItem`, `DBOutdoorItem`, `HouseConsignmentMerchant`,
  `househookpointitem`: houses.
- `PlayerBoats`: boats.
- `passport_visits`, `passport_trips`: a host's own record of its visitors and of every trip it took in. That record is
  what stops a travel file from working twice.

## For developers

- Library: `source/tools/OfflineDaoc.Passport` (the file format, the engine and the launcher's flows). The server and the
  launcher compile the same files, so both follow the same rules.
- Command line tool for tests and manual use: `source/tools/OfflineDaoc.Passport.Cli`.
- Tests: `source/tools/OfflineDaoc.Passport.Tests` (synthetic saves only) and `UT_PassportLock` in the server tests.
- Server: `PassportLock` (the home lock in world entry, character delete, `/account deletecharacter` and
  `/account movecharacter`), `PassportDesk` (the host's desk; `POST /passport/arrive`, `GET /passport/visitors`,
  `POST /passport/depart`, `POST /passport/depart/confirm`, HTTP Basic with the guest's own account in that world), and
  the setting `passport` / `passport_visitors`. A visitor can't buy a lot (`LotMarker`).
- Every table in `progress-policy.json` needs a decision in `PassportTables.Decisions`, and a line above.
  `PassportPolicyTests` fails until both are there.
- A trip is named by its trip id. Home accepts a return passport only for the trip it is away on; the host accepts an
  outbound passport only once. That id is the travel file's one-time number.
