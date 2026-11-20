# Build 1.2-43c43aa on the PC test copy (installed 13:40, backup fork-build-20261119-134016)
FORK_BUILD 13:40:58 name=1.2 commit=43c43aa775a8 bot_schedule=off. Sweep 10 START 13:42:56 raids=37 lanes=3 cap=360.
## Seen on PC (helper on screen + log), 13:43-14:21
- A Tepok's Mine named -> "is it far?" route/trip answer -> "how do i get there?" route. PASS (#375 place names).
- B help asks: 3 different bots answered, honest refusals (Soulbow "mid camp" was on a group-grind goal). PASS (#375 floor yields).
- C /broadcast answered in /broadcast. PASS. D /lfg 22 -> "quiet at that level right now, gl" tell. PASS.
- G /personality, /lswhy. PASS. F storyline vs answer: no clash seen (UNCLEAR, no overlap occurred).
- E Albion /faction 13:43-14:22: mean 4.9/min, median 5, min 2, max 8; 13 of 39 minutes under 5 (c81eb5d: 3.9). Improved, floor not held every minute.
- Caer Caledon repaired 46->37, dashboard guardLevel=1-39 lordLevel=46 (1 = hastener). Not targetable in game.
- Launcher count 5 -> 2,875 online. Gold spammer bots (AutonomousGoldSpammers, fake address) seen: intended feature.
- RvR: Alb warband for Dun Scathaig mustered 63 and marched; alarms (spotted 5 mids Pennine), sieges announced (Murdaigean, Caledon), engines on keep-31 (door at 17 hp 14:22). 1,958 RVR_BOT_KILLED, stance/assist/CC/peel. No keep taken yet.
- Problems: RVR_RALLY_DELAY x50 (49 bots) for Hib siege parties on rvr-keep-77 Hlidskialf and Alb on rvr-keep-105 Dun Scathaig ("no approach progress for eight minutes"). ERR after startup: UPnP (13:41), QueuePacket "DrainTo in progress" (14:02), no effect seen.
- Installer cleaned 1 old world DB copy (keeps newest 3): its own designed cleanup.
- DIAG-43c-rally: RVR_RALLY_DELAY mostly false alarms (bots holding muster posts; 39/50). Real route traps: Emain Macha seam 384960,385504 (75 Hib bots looped home), Odin's Gate muster posts unreachable, "no connected exterior route" (3 bots). No keep/tower taken (no RVR_KEEP_TAKEN); keep-77 battle started 14:13. Sieges time out (12+5 min muster, 60 min march, 4 h battle). Verdict: not a blocker.
## Check 15:28 local (window 14:22-15:27)
- SEEN: RVR_KEEP_TAKEN keep="Caer Caledon" id=31 realm=Albion battleground=True at 14:25 (first keep capture on 1.2).
- Raids: Gjalpinulva boss defeated 14:53 (68 min); Golestandt boss defeated 14:54 (69 min, after 1 wipe); Cuuldurach 2 wipes, Defeated 15:11. Sweep 3 done, 32 left.
- SERVER_STALL: 1 (1.19 s). ERR: CastingService NullReferenceException 14:56:33 (CastingComponent.cs:201 StartDuringCastLosCheck), bot Carik removed from world. Sent to route engineer for a null guard in the same PR.
- PC link dropped ~14:56-15:27 (bridge not connected), back by 15:27; server kept running.

# Candidate 1.2-aa537ce (#376 RvR route traps + casting null guard; PR #97 merge ref, integration 631ba1f)
- CI success; 14/14 hashes OK. Committed to test copy ~16:10 local; install + 40 min RvR recheck helper started. Ends sweep 10 (43c43aa) at 3/35.
## aa537ce in-game RvR 16:17-16:50 (helper) + log
- SEEN: Hib siege column walked Cruachan Gorge single file, none stuck (seam fix); Alb muster at Castle Sauvage on walkable ground; Alb army 81 marched to Caer Benowyc, Mid 48 defenders; Hib 67 marched on Dun nGed, siege 16:37, trebuchet seen; BG sieges announced.
- No keep taken before the 16:51 restart. BOT SCHEDULE -> Real clock at 16:51 (FORK_BUILD 16:52:01 bot_schedule=clock). Sweep ended by the restart (not restarted: raids out of scope).
- 16:59 check: Real-clock restart 16:52:01 clean (13.1 s), no stalls, no runtime ERR to 16:59:37. Sweep checks stopped (sweep ended, testing done; publish in progress).
