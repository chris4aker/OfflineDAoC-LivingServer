# Co-op assumptions audit (1–4 humans), release/1.2 at d1cb4c1d, 2026-11-03

Read-only. [V] verified in code or doc text, [I] inferred, [E] estimated. Paths are under `source/server/GameServer` unless they start with `source/`, `docs/` or a branch.

**Headline.** Most Living Server state is already keyed per player and gated on "any human of the realm/region", so it works for N humans [V]: memory, tells, deeds, guild invites, raid loot and keep claims. The single-player assumptions cluster in three places: **networking and accounts** (loopback exemptions, one global region address, bind defaults), **who bots follow in a group with two humans**, and **1.2 design docs written around "the player"**.

## 1. Assumptions

| System | file:line | Assumption | With 2–4 humans | Fix | Effort | When |
|---|---|---|---|---|---|---|
| Region address | `world/WorldMgr.cs:307,568`; `packets/Server/PacketLib168.cs:574` | One region IP for every client [V] | A remote friend is sent the host's address and gets stuck entering the world or changing region [I; the fork's doc lists this symptom] | Port the fork's `RegionAddress.ForClient` | M | **1.2.x** |
| Idle timeouts | `ECS-Services/ClientService.cs:762-768, 781-786`; `ECS-Components/PlayerMovementComponent.cs:58` | Only the loopback client is "the owner"; ping, position and AFK timeouts are skipped for it alone [V] | A friend idling in `/grind` is link-dead or kicked to the character screen. The code comment says the legacy client stops pinging there [V] | Exempt clients from trusted subnets (Tailscale, LAN) or players in a `/grind` session, not only loopback | S | **1.2.x** |
| Bind defaults | `source/server/CoreBase/Configs/BaseServerConfig.cs:58-64`; `source/server/CoreServer/Actions/ConsoleStart.cs:73-77`; `source/server/CoreBase/Network/BaseServer.cs:50-78` | Code defaults are `0.0.0.0`, UPnP on and DetectRegionIP on, and a missing `serverconfig.xml` is rewritten with them. UPnP mappings are never removed [V] | A lost config opens the router to the internet silently [V/I] | Defaults: loopback, UPnP off. Remove mappings in `Stop` (the fork does). `build_release_037.py` must assert the bind settings; today it asserts only AutoAccountCreation (`:209, 261-266`) [V] | S | **1.2.x** |
| Default account | `source/tools/OfflineDaoc.Launcher/PortableCredentials.cs:14` | Every install's account is named `offline` [V] | A friend who types their own `account.txt` details is refused, because the host already has `offline` [I] | Join Friend refuses `offline` and suggests a name | S | **1.2.x** |
| Same-IP creation limit | `serverproperty/ServerProperties.cs:2604` (15 min), `packets/Client/168/LoginRequestHandler.cs:298-313` | Accounts come from different IPs [V] | Two friends behind one NAT in internet mode, or two local test clients, are refused for 15 minutes [I] | Hosting sets it to 0 for allowlisted ranges, or documents it | S | **1.2.x** |
| Group leadership | `bots/BotGroupInvite.cs:109`; `gameutils/Group.cs:414, 468`; `bots/BotBrain.cs:669` | A bot follows `PlayerGroupLeader ?? Owner`. `MakeLeader` doesn't rebind bots, and a departure hands followers to the first other player, not the new leader [V] | Bots keep following the old leader [V]. Wrong behaviour | Rebind on `MakeLeader`. Followers go to `Group.Leader` | S | **1.2.x** |
| `/pull` and attacks | `bots/PlayerLedPullCoordinator.cs:44, 131` | `/pull` rebinds every helper in range to whoever issued it, and any human's attack counts as the leader's [V] | Friend B's `/spawn` companions switch to A when A pulls [V]. Bots ping-pong between the humans | Companions always follow their Owner. `/pull` orders only your own companions plus the leader's followers | M | **1.2.x** |
| Pilot | `bots/autonomous/AutonomousPlayerPilot.cs:376-386` | `PreserveSpawnedParty` rebinds every bot in the group to the piloted player [V] | Takes the other human's bots [V] | Rebind only bots this player owns or leads | S | 1.2.x |
| Host-PC files | `commands/playercommands/LocalPlayerTravelInput.cs:46-57`; `bots/autonomous/AutonomousPlayerQuickbarProfile.cs:46-60` | `/travel` presses keys in the host's foreground window. `/bot pilot` reads the host's `%APPDATA%` INI [V] | Remote `/travel` correctly fails closed [V]. A friend's pilot gets "no quickbar file" [V] | Say "host only" in 1.2.x. Move the quickbar data to the DB or the client in 1.3 | S/M | 1.2.x msg, 1.3 |
| Companion load | `bots/CompanionRaid.cs:26-28`; `commands/playercommands/TemporaryGroupCommands.cs:209` | Caps are per group. No server-wide cap was found [V] | 4 × `/raid 80` = 316 companions at the 300 ms player-led cadence (§4) [E] | A server-wide companion budget when more than one human is online | S | **1.2.x** |
| Sessions | `bots/autonomous/AutonomousLivingWorld.cs:47-62` | A session is per realm audience [V] | A friend joining an hour after the host gets no session openers [V/I]. Odd | Realm openers stay. Player-addressed openers (first tell, greeting) are per player | M | 1.3 |
| Reply throttles | `bots/autonomous/AutonomousBotChatCoordinator.cs:456, 470, 493` | Reply cooldowns and the "quiet after player" timer are per realm or region [V] | One human's line silences replies to another for 10–15 s [V] | Key on (scope, player) | S | 1.2 (cheap) |
| Memory keys | `bots/autonomous/AutonomousLivingServerStore.cs:24-25`; `AutonomousPlayerDeeds.cs:18,60`; `AutonomousLivingServer.Tells.cs:27-28` | Keyed per player **name** [V] | Correct for N humans. Renames and the planned Character Passport break the link [I] | Add the character id | S | 1.3 |
| Small oddities | `bots/autonomous/AutonomousBotSchedule.cs:394`; `AutonomousBotLinesHeard.cs:14-15` | Linkdead rolls loop over players, not groups. The no-repeat window is per realm [V] | A 2-human group is rolled twice. A late joiner misses some lines | Roll distinct groups. Keep the realm window | S | 1.3 |
| Launcher | `source/tools/OfflineDaoc.Launcher/MainForm.cs:1615-1631, 2359, 2846, 2896`; `ClientSessionDiagnostics.cs:267`; `bots/autonomous/AutonomousPopulationController.cs:506` | One `account.txt` drives GM, teleport-to-bot and the 127.0.0.1 client start. Diagnostics watch loopback only. `MakeMeGM` is one flag [V] | Right for the host. Guests have no launcher control and no session diagnostics [V/I] | 1.3: in-game equivalents (`/tele` exists), per-account GM display | S/M | 1.3 |
| Global settings | `MainForm.cs:580` (`xp_rate`); settings writes need the server stopped (`:1504, 1535`) | "YOUR PLAYER XP" applies to everyone. Changing a setting stops the world [V] | Friends inherit the host's rate. A change kicks everyone [V] | Label as server-wide. Live reload in 1.3 | S | 1.3 |
| Fidelity | `bots/BotBrain.cs:2803-2815`; `bots/autonomous/AutonomousFidelityPolicy.cs:18`; `AutonomousPopulationProperties.cs:32` | Tier = any player within 6,000 units, so it is already the union of humans [V]. `Select()` is test-only and `ai_budget_ms` is unread [V] | Cost grows with the number of separated humans (§4) | Budget by tick time, not by human count | M | 1.3 (profiler in 1.2) |
| Quest credit | `docs/QUESTING.md:105`; `bots/BotBrain.cs:113-116` | Decision 4: "credit the group's player" (singular) [V] | Two humans in a bot group: only one is credited, or double counting (defect 6) [I] | Credit every eligible human in range once per kill | M | 1.2 rule |
| Events engine | `docs/LIVE-EVENTS-ENGINE.md:44, 77, 116, 155, 202` | "the player's beat", `near_player: 48`, "the player's session clock", nothing fires offline [V] | Whose beat? Caps multiply with each human [I] | A beat goes to the first human present, with the bot fallback. Caps are a total plus a per-human share. Clock per realm audience | design | 1.2 design |
| North-star plan | `origin/audit/north-star-2026-11-03:docs/NORTH-STAR-AUDIT.md:63, 73, 112, 123, 127` | "single-player server", "the player always leads", bots invite "the player", "since you last played" [V] | One invite target. One shared absence clock [I] | Invites choose among humans, one pending invite per human. "Since you last played" uses the character's `LastPlayed` (`gameobjects/GamePlayer.cs:10136`). Catch-up uses server downtime. The Herald is launcher-only (`HeraldPage.cs`) and needs an in-game hook for guests | design | 1.2 design |
| Chat and group frameworks | `origin/research/1.2-before-coding:docs/BOT-CHAT-FRAMEWORK.md:82, 125, 156`; `docs/BOT-GROUP-FRAMEWORK.md:44-45, 56` | Player-scope facts, "where the player can see it", the director reads "the player's state", stance set by "the player", never break "the player's" mez [V] | Ambiguous with two humans | The player scope carries a player id. Audience = any human. Stance belongs to the leader or the owner. Never break any human's mez | design | 1.2 design |

## 2. Rules for 1.2 code

1. Never pick "the" player. Iterate `ClientService.Instance.GetPlayers*` or carry a `GamePlayer` or character id. `FirstOrDefault` on players is for logging only.
2. Key per-human state by character id (name only as a display slot). Realm- or region-wide state must be named as such.
3. Presence gates mean "any human of realm X here". Effects that address a human are per human. Ambient pacing is per realm.
4. A bot's human is explicit: companions follow their **Owner**, gamebots follow **Group.Leader**. No command rebinds bots it doesn't own.
5. Rewards and credit go to every eligible human, once per event.
6. Never trust loopback as "the owner" for gameplay. Loopback may only gate host-PC side effects (input, files), and must fail closed with a message.
7. Caps are server totals plus per-human shares. Never "per player" without a total.
8. Design docs say "a human" or "each human" and name the tie-break.
9. Every new player-facing system gets a two-player unit test (same and different realms).

## 3. Security baseline for hosting

- **Accounts.** Auto-creation is forced on in releases (`build_release_037.py:148, 261-266`) [V], so anyone who reaches TCP 10300 gets an account. New accounts are PrivLevel 1 (`LoginRequestHandler.cs:352`) [V]. Hosting must narrow who can reach the port (firewall allowlist), and after friends have made their accounts the host should be able to turn auto-creation off from the Host window [proposal].
- **Passwords.** Stored as unsalted MD5 (`LoginRequestHandler.cs:479-490`) [V]. A wrong password is only logged and audited, with no lockout (`:371-381`) [V]. The server reads the password as a plain string from the login packet (`:150-153`) [V]. The fork says the login isn't encrypted [V doc], so it is readable on the wire unless a tunnel carries it [I]. Each friend uses a password that is unique to this server.
- **Host account.** `offline` with 20 random hex characters (80 bits) (`PortableCredentials.cs:14`) [V]. Never shared. Join Friend refuses the name.
- **GM.** Make Me GM sets PrivLevel 2 for the `account.txt` account only (`MainForm.cs:1631`) [V]. Never grant guests GM. GM commands (`/lstest holiday`, `/lstest raid`) are server-wide [V], and GMs are immune to PvP [V fork doc].
- **Ports.** TCP 10300 and UDP 10400. Use the fork's firewall rules, scoped to `CoreServer.exe` and to 100.64.0.0/10 plus the local subnet [V doc]. Fix the bind defaults and the UPnP clean-up (§1) before shipping. Internet mode: the friend IP list, short sessions, and mappings removed on stop.
- **Tailscale (preferred).** Traffic is WireGuard-encrypted, which covers the plaintext login [I]. Share the one machine rather than the whole tailnet, and add an ACL limited to 10300/10400 [proposal].
- **Data.** Friends' characters live in the host's database, and logs record names and IPs (`:374`) [V]. Publish neither.

## 4. Load: 2–4 humans in different zones

Mechanics [V, `AutonomousFidelityPolicy.cs`, `BotBrain.cs:2803-2815`]:
- A bot is **NearbyHuman** with any human within 6,000 units. It is **Standard** when a client saw it in the last 60 s (`GameNPC.cs:36, 699`), and **Efficient** otherwise.
- Planning interval: 2.5 s at NearbyHuman, 6.5 s at Efficient (stretched by population, capped at 12 s). Travel: 0.9 s against 2.3 s. Combat: 250 ms against 500 ms. Candidate limit: 24 against 6.
- Monster brains already wake for bots (`IsVisibleToPlayersOrBots`, `GameNPC.cs:716`) [V], so extra humans add little monster AI.

Estimates:
- 20–60 bots sit inside one human's 6,000-unit circle in the field, and 100–200 in a capital or at a siege [E].
- A NearbyHuman bot costs about 2.5–4× an Efficient one: 2.6× the decisions, and wider candidate scans only on some paths [E].
- Humans who play together share one circle and add about 0% [E].
- Four humans in separate zones: about 150 extra elevated bots in the field, roughly **+15–30% AI decision cost** at 1,500 bots [E]. Four humans at four sieges: 400–600 elevated bots, **+60–100%** [E].
- **Companions dominate.** Each `/raid 80` puts 79 bots at the 300 ms player-led cadence, about 260 thinks/s. Four raids make about 1,050 thinks/s, against an estimated 250–400/s for the whole 1,500-bot world at Efficient [E]. Cap them server-wide (§1).
- Per-client work grows linearly: world updates, and bot map markers every 4 s cost O(humans × bots) (`AutonomousBotMapMarkers.cs:19, 109`) [V/I]. That is negligible at 4 humans [E].
- Event caps (`near_player: 48`) become 4 × 48 unless they are totals [I].

Recommendation: add a **4-human soak** (4 zones, then one keep) to the 1.2 AI_PROFILE baselines next to L300/L600. Enforce `ai_budget_ms` before co-op is the default, with combat never deferred and NearbyHuman deferred last [I].
