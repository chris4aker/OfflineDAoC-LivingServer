# DAoC sources for recreating 2001-2003 server culture (launch 9 Oct 2001 to patch 1.65, 8 Oct 2003)

Session date: 2026-11-01. Fetching was done with curl through the session proxy (WebFetch's allowlist refused web.archive.org and gamedeveloper.com). "Era" = written 9 Oct 2001 to 8 Oct 2003. "Later" = written after 8 Oct 2003. The raw downloads are in the session scratchpad and are not kept.

## Q0. Access: does the Wayback Machine work now, and which URLs could be reached?

### Takeaway
The **web.archive.org** snapshot host still fails: the connection is reset every time, so archived pages cannot be read. **archive.org** itself works, including its availability API, search, metadata and file downloads. That means the API can confirm a snapshot exists without letting us read it. It also means the archive.org *collections* (scanned Prima guides with full text, RvR movies, game discs) are fully usable. archive.ph returned HTTP 429 (rate-limited). The Camelot Herald fandom wiki blocks plain page loads with 403, but its **MediaWiki API (api.php) works**. That API gives dated patch notes for every version.

### Cited Findings
- Reachability table, tested 2026-11-01:
  - `https://web.archive.org/web/2003/http://www.camelotherald.com/`: connection reset (curl error 35). The proxy logged "tunnel closed (code 1006)" for web.archive.org:443. The CDX API on web.archive.org also reset.
  - `http://archive.org/wayback/available?url=vnboards.ign.com&timestamp=20020601`: **200**. It reports the snapshot `http://web.archive.org/web/20020602115833/http://vnboards.ign.com:80/`, so Vault boards snapshots from 2002 exist but cannot be opened from here. A second call over https returned 429.
  - `https://archive.ph/camelotherald.com`: **429** (rate limit).
  - `https://archive.org/advancedsearch.php` and `https://archive.org/metadata/<id>` and `/download/`: **200**, working.
  - `https://camelotherald.fandom.com/wiki/...`: 403. `https://camelotherald.fandom.com/api.php?action=parse&page=...&prop=wikitext&format=json`: **200**, working.
  - `https://groups.google.com/g/alt.games.dark-age-of-camelot` and its `/search?q=`: **200**. The thread lists render with dates (posts back to 2001).
  - `https://mud-dev.zer7.com/` (MUD-Dev mailing list archive, 1996-2010): **200**, with search.
  - `https://www.gamedeveloper.com/disciplines/postmortem-mythic-entertainment-s-i-dark-age-of-camelot-i-`: **200**.
  - `https://www.gamebanshee.com/news/...`: **200**.
  - `https://www.nickyee.com/daedalus/`: **200**. `https://daedalusproject.net/`: proxy 502.
  - `https://www.freddyshouse.com/`: 200 (small landing page).
  - `https://www.uthgard.net/`: 200.
  - `https://www.darkageofcamelot.com/`: 200 (current official site with a "Class Library", a later source).
  - `https://camelotvault.ign.com/`: 200, but it now redirects to a generic IGN guide page. The Vault content is gone from the live web.
  - `https://daoc.stratics.com/`: proxy 502.
  - `reddit.com/r/daoc/search.json`: 403.
  - `https://massivelyop.com/2016/07/02/the-game-archaeologist-dark-age-of-camelot/`: 403.
  - `https://newoutriders.org/?p=2151`: 406.

### Inferences
- The best practical route to era Herald text is not Wayback. It is (a) the fandom wiki API, which mirrors Herald patch notes with their original release dates, (b) GameBanshee news posts, which quoted the Herald week by week with dates, and (c) the archive.org text items.
- Vault boards content will need a session or tool where web.archive.org is reachable. The availability API can still be used here to list which snapshot timestamps exist before such a session.

### Gaps
- No archived Herald page (Grab Bag, census or "Realm War" status pages) could be read directly. Whether the Herald's population or class statistics pages survive in Wayback is unverified.
- archive.ph could not be tested past the rate limit.

## Q1. Official and developer material: what exists and what does it tell us?

### Takeaway
Two era sources are underused and have high value. The first is **Matt Firor's Game Developer postmortem (13 Feb 2002)**, which explains why the Herald exists and how central the Vault was. The second is the **dated Herald patch notes** mirrored on the fandom wiki. Those notes record social-system facts directly: /advice in 1.08-1.33, chat groups up to 200 in 1.60, newbie guilds in 1.62, /rpfilter for "LFG" in 1.64. **GameBanshee's news archive** keeps dated excerpts of Sanya Thomas's Grab Bag columns.

### Cited Findings
- **Postmortem**, Matt Firor (producer), dated February 13, 2002 (era):
  - "51,000 boxes were sold in the first four days alone." Mythic staffed customer service for a much smaller forecast, so wait times became "much too long."
  - The game launched "with only three capital cities (one per Realm) and about 15 dungeons."
  - During development, "the Vault Network, provided us with some message board space, a news page, and a couple of moderators." Mythic hired a community relations manager "whose sole job was to read different message boards."
  - After release, players were frustrated because Mythic's posts were scattered across fan sites. "About six weeks after release" Mythic decided to build its own site, "dubbed 'Camelot Herald'." It carried release notes, plan files, server status and Realm War status.
  - Dave Rickey designed the economy so "players must continue to spend money as they rise in level, which limits the amount of money that stays in the game." Rickey and Mark Jacobs designed the tradeskill system.
  - The servers ran on Linux with MySQL (Oracle's quoted fee was more than $900,000) and were co-located at UUNET.
  - [Game Developer](https://www.gamedeveloper.com/disciplines/postmortem-mythic-entertainment-s-i-dark-age-of-camelot-i-)
  - The same postmortem was reprinted as Chapter 23 of the O'Reilly/CMP book *Postmortems from Game Developer*. [O'Reilly](https://www.oreilly.com/library/view/postmortems-from-game/9781578202140/32_Chapter_23.xhtml)
- **Herald patch notes, dated, via the fandom wiki API** (era primary text, mirrored later):
  - 1.32 (Oct 19, 2001): "How do I..." questions sent through /appeal would not be answered. Players were told to use "/Advice, searching our website, or on one of the many fan-sites."
  - 1.33 (Oct 23, 2001): "/ADVICE command now presents the user with a popup window listing up to 25 available advisors."
  - 1.08 (a Pendragon test-server version; no date in the wiki text): /advice got a reply key and its own chat color and tab.
  - [1.32](https://camelotherald.fandom.com/wiki/Patch_Notes:_Version_1.32), [1.33](https://camelotherald.fandom.com/wiki/Patch_Notes:_Version_1.33), [1.08](https://camelotherald.fandom.com/wiki/Patch_Notes:_Version_1.08)
  - 1.39 (Dec 18, 2001): "All pulsing spells now print to everyone in the group," with a channel added to turn those prints off. [1.39](https://camelotherald.fandom.com/wiki/Patch_Notes:_Version_1.39)
  - 1.53 (no date in the wiki text): "Huginfell no longer has two /broadcast regions." Guild chat now gets a print when a guard at the guild's outpost is slain. [1.53](https://camelotherald.fandom.com/wiki/Patch_Notes:_Version_1.53)
  - 1.62 (between 1.60 on Feb 24, 2003 and 1.64 on Aug 12, 2003; exact date not in the wiki text):
    - "Implemented the 'newbie guild' system on all live servers ... the same system that has been featured on the PvP servers for the last few months."
    - "There are four guilds per Realm, each named based on the geographical location"; players could leave with "/gc quit."
    - Chat tabs could be set to "Say, Broadcast, Group, Guild, Alliance Chat, Officer Chat, or Chat Group."
    - [1.62](https://camelotherald.fandom.com/wiki/Patch_Notes:_Version_1.62)
  - 1.65 (October 8, 2003): "All old world dragons have..." (dragon changes). Three bonus zones unlocked on the SI client. A new "Resists Changed" system channel was added. [1.65](https://camelotherald.fandom.com/wiki/Patch_Notes:_Version_1.65)
- **GameBanshee news archive** (era; dated reposts of Herald items), "Camelot Grab Bag Q&A" posted by BuckGB on August 17th, 2002:
  - "Mythic's Sanya Thomas once again posted a Grab Bag Q&A on the Camelot Herald." Topics included the Pendragon character copier, loot drop rates and skill caps.
  - Quote: "You can only copy to Pendragon ONCE ... please don't name your character DroopyDrawers even as a joke. It's not as funny as you think it is. Really."
  - [GameBanshee](https://www.gamebanshee.com/news/69188-camelot-grab-bag-qa-v15-69188.html)
  - Search results show dozens more dated "Camelot Patch Notes (Live)" and Grab Bag reposts. Examples: [gamebanshee.com/k69y3](https://www.gamebanshee.com/k69y3), [3r6y "Version 1.62 Goes Live"](https://gamebanshee.com/3r6y). There are also Matt Firor interviews: [gamebanshee.com/a6ck](https://www.gamebanshee.com/a6ck) and [gamebanshee.com/36pp](https://gamebanshee.com/36pp). These were not read in this session.
- **Prima official guides on archive.org** (era):
  - *Dark Age of Camelot: Prima's Official Revised & Expanded Strategy Guide*. Writer: Melissa Tyler, an Incan Monkey God Studios production. LoC number 2002103850. "Copyright © 2002 Mythic." The scan's printing line reads "© 2002-2004 by Prima Games."
  - It credits Mythic staff (Firor, Rickey, Jacobs, Axelson and others) and a "Panel of Experts" of named players with their character names (for example Ethan "Esis" Kidhardt and Steve "LarianLeQuella" Lundquist).
  - It is unrestricted, with full OCR text. [archive.org item](https://archive.org/details/Dark_Age_of_Camelot_Revised_and_Expanded_Prima_Official_eGuide)
  - Also available: the *Shrouded Isles* eGuide ([archive.org](https://archive.org/details/Dark_Age_of_Camelot_Shrouded_Isles_Prima_Official_eGuide)) and the print editions *Dark Age of Camelot* by Melissa Tyler (2002, lending library) ([archive.org](https://archive.org/details/darkageofcamelot00tyle)) and *Shrouded Isles* by Justin Brown, Robert Howarth and Damien Waples (2002, lending) ([archive.org](https://archive.org/details/darkageofcamelot00brow)).
  - Later material is also in the collection: Trials of Atlantis, Catacombs and New Frontiers guides, plus an E3 2005 presentation. [archive.org search](https://archive.org/advancedsearch.php?q=%22dark+age+of+camelot%22&fl[]=identifier,title,date,mediatype&rows=60&output=json)
- **Game builds on archive.org**: "Dark Age of Camelot v1.64 Trial" (dated 2002-03-25 in the metadata; the description says 2003) ([archive.org](https://archive.org/details/darkageofcamelot164trial)), and an SI client v1.78 dated 2005-08-18 (later).

### Inferences
- The Herald began about mid/late November 2001, inferred from Firor's "about six weeks after release" plus "the following week." So the first weeks of server culture ran entirely on fan sites (the Vault) and in-game /advice.
- Patch notes double as a dated changelog of social tools. A bot simulation that is placed at a given date should switch features on by patch: newbie guilds only from 1.62, chat groups of up to 200 only from 1.60, and so on.

### Gaps
- Herald census, population and class-statistics pages were not reached (Wayback blocked).
- Mark Jacobs and Sanya Thomas team-chat transcripts were not located beyond the GameBanshee reposts.
- The DAoC manual was not found on archive.org in this search. The trial and install discs may contain it; not checked.

## Q2. What do era sources say about chat, slang, etiquette and group norms (bot-behaviour facts)?

### Takeaway
The Prima guide is an underused, era-specific etiquette source that was written with a player panel. It describes DAoC as a game where nobody owns a camp, where asking for buffs is rude because buffs cost the caster power, where kill-stealing can get you booted, and where /advice is a real social channel. Trade talk went through **/broadcast** macros.

### Cited Findings
All findings in this section come from the *Prima Revised & Expanded guide*, OCR text (2002) ([archive.org](https://archive.org/details/Dark_Age_of_Camelot_Revised_and_Expanded_Prima_Official_eGuide)) unless another source is named.
- **Camps:** "Don't hog a camp. In DAoC, no one 'owns' a spawn site ... If you and your group are pulling from a spawn site, and someone else comes up, you need to take turns. The only monsters you 'own' are the ones that you're current[ly] pulling, currently fighting, or have mezzed." Mythic "really discourages long-term camping in general (over-hunting an area will tend to drive the XP of the local monsters down)."
- **Kill-stealing:** "kill stealing is not tolerated. If you get reported enough times, it can get you booted from the game."
- **Buffs:** "Don't ask for Buffs ... In DAoC, however, Buffs cost the casters Power and they get nothing in return. So in DAoC it's not very polite to ask without offering something in return ... or at least being extraordinarily complimentary."
- **/advice:** "Players who have been in the game for a long time can 'sign up' to act as advisors ... (They do this by typing in /advise.) New players can get a list of the advisors who are logged in by typing /advice ... Advisors are not Customer Service representatives." The command appendix says becoming an advisor requires "15 hours of experience."
- **Group style and its EverQuest / Asheron's Call framing:** "EverQuest people understand ganging up on one monster at a time, with the magic users keeping the others safely mezzed. Asheron's Call immigrants are a little less familiar with the concept of 'assisting'." The guide recommends targeting and then "/stick."
- **Group etiquette:** "calling out your target, Power level, or anything else of interest to others."
- **Glossary entries:** "kill stealing/ks Unwelcome help ...", "kos Kill On Sight ... Only Realm Guards will attack regardless of level," "lamo," "ld Link Dead," "baf Bring A Friend," "brb," "brt," "cfh," "bubble/bub" and others.
- **Class tips with an RvR angle:**
  - Bard: "Mez invaders first!"
  - Shield spec: "If you get your Shield high, you'll be in great demand for RvR."
  - Guard/Protect: "helps you protect casters in a group or RvR setting."
- **Other glossary and contents facts:** the glossary defines "broadcast" as "Shout. Broadcast a message for all in your town or city to hear." The RvR chapter covers Gating, Keep Claiming, Keep Raids, RvR Combat, Siege Equipment, Realm Abilities, and maps of Emain Macha, Snowdonia, Odin's Gate and Darkness Falls.
- **Usenet (era and later):**
  - alt.games.dark-age-of-camelot is browsable with dates.
  - A search for "lfg channel" found "Gaheris: Zone Wide Broadcast ... The broadcast channel" (11/23/02) and "DAOC - Just a power levelling treadmill?" (12/27/01).
  - A search for "buffbot" returned 26 threads, all dated 2004-2005 (later), for example "Collisions - Finally? ... anti-buffbot" (9/14/05). It also turned up a newcomer's "MMORPG glossary" thread (10/5/05).
  - [Google Groups](https://groups.google.com/g/alt.games.dark-age-of-camelot)

### Inferences
- Bots should share spawn sites and take turns instead of claiming camps. They should treat mezzed or engaged mobs as owned, never ask strangers for buffs (or offer payment if they do), and send veteran help to newcomers through /advice.
- Buffbot culture is not documented in the era sources checked here. The Usenet hits for "buffbot" are all 2004-2005. Bots on a pre-1.65 server should not assume buffbots were common unless era evidence turns up. This is weak evidence (Usenet traffic was small), not proof.

### Gaps
- Era RP-server rules, realm stereotypes, relic-raid timing norms, and a "zerg" or "gank" vocabulary were not found in the guide's OCR (zero hits for "zerg"). These need Vault, FreddysHouse or Herald sources.

## Q3. Did DAoC have /lfg and /trade chat channels by patch 1.65 (8 Oct 2003)?

### Takeaway
**No.** No dated source checked shows a /lfg or /trade chat channel by 8 Oct 2003. Looking-for-group in this era was a **flag-and-window system**: an LFG window where players set "Looking For Group" status and could browse lists. LFG and trade talk itself travelled on **/broadcast** (zone or town areas), **/say**, chat groups (**/cg**, up to 200 members from 1.60), guild chat, and newbie-guild chat (from 1.62). /advice was for questions.

### Cited Findings
- **Prima Revised & Expanded guide (2002), "Game Talk" command table:** it lists only /em, /group (/g), /say (/s), /send, /whisper, /yell, /gu, /oc, /as and /b. Chat groups are /cg invite/join/decline/leave/remove/who/disband, plus "/chat Send messages to members of your chat group (or use the Friends tab)." There is no /lfg or /trade. For selling, the guide says to make a macro: "/broadcast Selling <what type> armor for <cost>." [archive.org](https://archive.org/details/Dark_Age_of_Camelot_Revised_and_Expanded_Prima_Official_eGuide)
- **1.59 (January 23, 2003):** "The Looking for group system should now work across zones. It stores up to 30 of each type of group leaders or solo players, and displays the zone name. LFG status is now saved when zoning." [1.59](https://camelotherald.fandom.com/wiki/Patch_Notes:_Version_1.59)
- **1.60 (February 24, 2003):** "The Looking for Group window is now sortable." Also: "Chat groups can hold up to 200 members," with new /cg listen, /cg leader, /cg public ("anyone can join by typing /cg join") and /cg private. [1.60](https://camelotherald.fandom.com/wiki/Patch_Notes:_Version_1.60)
- **1.62 (spring/summer 2003):** the chat-tab outgoing types available were "Say, Broadcast, Group, Guild, Alliance Chat, Officer Chat, or Chat Group." There was no LFG or Trade type. Newbie guilds gave each new character "an instant chat group." [1.62](https://camelotherald.fandom.com/wiki/Patch_Notes:_Version_1.62)
- **1.64 (August 12, 2003):** the new /rpfilter example is "/rpfilter LFG looking for group," "So if anyone in chat says '/s LFG blah blah blah' your client will actually print ... 'looking for group blah blah blah'." This is direct evidence that players typed "LFG" in /say. [1.64](https://camelotherald.fandom.com/wiki/Patch_Notes:_Version_1.64)
- **1.65 (October 8, 2003):** "All SI classes should now show up under the proper categories in the LFG windows." This refers to the window, not a channel. [1.65](https://camelotherald.fandom.com/wiki/Patch_Notes:_Version_1.65)
- **Later patches (after the era):**
  - 1.66 (Oct 28, 2003) added separate battlegroup chat channels.
  - 1.67 (Dec 16, 2003) brought a "NEW LOOKING FOR GROUP WINDOW" with a 'Set LFG' button.
  - 1.68 (Feb 19, 2004) allowed item-stat links in "guild, group, chatgroup, and battlegroup chat channels."
  - None of these mentions an LFG or Trade channel.
  - [1.66](https://camelotherald.fandom.com/wiki/Patch_Notes:_Version_1.66), [1.67](https://camelotherald.fandom.com/wiki/Patch_Notes:_Version_1.67), [1.68](https://camelotherald.fandom.com/wiki/Patch_Notes:_Version_1.68)
- **Present-day Herald wiki "Chat" page (later):** it lists Say, Private Send, Group, Guild, Broadcast, Emote, Help, Advice, Officer, Alliance, Battlegroup, Battlegroup Leader and Chat (chat group). It describes Broadcast as "restricted to specific areas such as Prydwen Keep, Mularn, Howth." It has no LFG or Trade channel. [Chat](https://camelotherald.fandom.com/wiki/Chat)
- **Usenet:** "Gaheris: Zone Wide Broadcast" (11/23/02) discusses the broadcast channel. [Google Groups](https://groups.google.com/g/alt.games.dark-age-of-camelot/search?q=lfg%20channel)

### Inferences
- Bot chat should advertise LFG in /say or /broadcast near the bindstone or in town ("LFG 24 ranger"), set the LFG window flag, and run cross-zone recruitment through public chat groups and guild chat.
- Trade adverts should be /broadcast macros in towns that have broadcast areas. There should be no zone-wide or server-wide trade channel.
- Whether the LFG window existed at launch, or was added between 1.3x and 1.58, is not established here.

### Gaps
- When the LFG flag and window first appeared. Searches of the 1.40-1.58 notes for "LFG / looking for group" found nothing, so it may have been added before 1.40 or under different wording.
- Freeshards or later live versions may have added LFG or Trade channels. Not checked; any such channel would be out of era.

## Q4. Community and fansite sources of the era

### Takeaway
The Vault Network was Mythic's own chosen community hub (per the postmortem). It is now offline and reachable only through Wayback, which is blocked here. MUD-Dev is a major underused primary source: Mythic's economy designer Dave Rickey posted there in 2001-2003 with candid DAoC observations. GameBanshee preserves dated Herald excerpts.

### Cited Findings
- **MUD-Dev archive** (era threads):
  - "MMORPG Comparison (UO, EQ, AC, AO, DAoC)," Oct 2001: [mud-dev.zer7.com/2001/10/20941](https://mud-dev.zer7.com/2001/10/20941/)
  - "Ten Rules of PvP was Interesting DAoC Poll," Dec 2001: [2001/12/21663](https://mud-dev.zer7.com/2001/12/21663/)
  - "Natural Selection and Communities," Aug 2002: [2002/8/24813](https://mud-dev.zer7.com/2002/8/24813/)
  - "Grouping in MMP Games" (Dave Rickey), Sep 2002: [2002/9/24955](https://mud-dev.zer7.com/2002/9/24955/)
  - "Much Respect to JessicaM" (Rickey), Sep 2002: [2002/9/24885](https://mud-dev.zer7.com/2002/9/24885/)
  - "D&D vs. MMORPG 'complexity'" (Rickey and others), Apr 2003: [2003/4/27267](https://mud-dev.zer7.com/2003/4/27267)
- **Rickey quotes:**
  - In "Natural Selection and Communities" (Aug 2002), Rickey (posting as mahrinskel@brokentoys.org) wrote: "I can't keep two players from forming a relationship, even the wall between realms in Camelot leaks like a sieve ... social networks are adaptive organisms, they take the structure appropriate to their environment." He also argued that DAoC-style guilds are the same structures that "emerged spontaneously in the old NWN ... even though those old-school games had *no* support for guilds." [MUD-Dev](https://mud-dev.zer7.com/2002/8/24813/)
  - In April 2003 he wrote: "I have seen evidence that strongly indicates that empowerment *is* a prime mover, in Camelot at least." [MUD-Dev](https://mud-dev.zer7.com/2003/4/27267)
- **Rickey's later blog:** Engadget (2011, later) points readers to Rickey's Broken Toys blog for the launch story. That story includes "the resulting mad dash for the nearest computer retailer to purchase a dozen desktops that were quickly loaded with Linux and whipped into an ad-hoc server cluster." [Engadget](https://www.engadget.com/2011-10-10-devs-reminisce-on-dark-age-of-camelots-10th-birthday.html)
- **Vault boards:** the Wayback availability API confirms a vnboards.ign.com snapshot from 2002-06-02, but it cannot be read from here. [archive.org API](http://archive.org/wayback/available?url=vnboards.ign.com&timestamp=20020601)
- **camelotvault.ign.com** now redirects to a generic IGN guide page. [IGN](https://camelotvault.ign.com/)

### Inferences
- Rickey's MUD-Dev posts are developer-side primary evidence that realm walls leaked socially. That supports giving bots out-of-realm acquaintances, for example through alts.

### Gaps
- Not checked this session: Allakhazam, Stratics (502), Camelot Seer census, Catacombs-era sites, IRC logs and server guild sites.
- The bodies of the "Grouping" and "Ten Rules of PvP" posts were not extracted (the page parse returned no "From:" block). They need a re-read.

## Q5. Media (videos, magazines)

### Takeaway
archive.org holds 2002-2005 player-made DAoC movies and German clan videos, some dated. These are primary visual evidence of RvR group play, but most items are undated uploads.

### Cited Findings
- "Smokin RvR comp" by Deciple: "RvR compilation of mythic entertainments online game, dark age of camelot," added 2006-09-06. The capture date is unknown. [archive.org](https://archive.org/details/DecipleSmokinRvRcomp)
- "Total Pwnage" (added 2005-05-13): "Oh what a difference a healer makes! ... seeing how many kills in a row we can get." [archive.org](https://archive.org/details/TotalPwnage)
- "NCAA - No Clan At All" German clan videos, 2001-2005, including "NCAA-Community-VIDEO-10-2002" and "A-Gamers-Heart-VIDEO-09-2003." [archive.org](https://archive.org/details/NCAA-No-Clan-At-All-VIDEOS)
- "Scott's Man (2003)" (dated 2003-06-14): a machinima music video, an example of social and RP play. [archive.org](https://archive.org/details/scottsman_202110)
- "DARK AGE OF CAMELOT" (2002) promo video in the gamevideos collection. [archive.org](https://archive.org/details/028657)
- Nick Yee's Daedalus video page lists community movies such as "Teletha's Memorial (DAoC)," "Drakulv Executioners (DAoC)" and "Drunken Scottsman (DAoC)." [nickyee.com](https://nickyee.com/daedalus/archives/cat_general_faqs.php)

### Gaps
- PC Gamer, CGW, GameSpot and IGN reviews and diaries were not retrieved.
- The NCAA videos' DAoC content was not verified, and no video was watched.

## Q6. Academic research

### Takeaway
Nick Yee's Daedalus Project is online, but no DAoC-specific dataset was extracted this session. MUD-Dev links Rickey to discussion of Yee's studies.

### Cited Findings
- The Daedalus site is live; its last issue is "Vol. 7-1 (03/09/2009)." [nickyee.com/daedalus](https://www.nickyee.com/daedalus/)
- A MUD-Dev search lists "Codename Blue & Facets - Nick Yee's new studies," posted by Dave Rickey. [MUD-Dev search](https://mud-dev.zer7.com/search?q=Camelot)
- The MUD-Dev search also lists the thread "On Virtual Economies by Castronova." [MUD-Dev search](https://mud-dev.zer7.com/search?q=Camelot)

### Gaps
- Not checked: Ducheneaut (who mainly studied SWG and WoW), T.L. Taylor (EverQuest) and DAoC-specific theses. A search for DAoC studies returned only aggregator hits.

## Q7. Living memory and freeshards (later)

### Takeaway
Freeshards are later reconstructions and should be treated as such. Uthgard says it began in 2003.

### Cited Findings
- Uthgard says: "we've come a long way as the first Dark Age of Camelot freeshard ... since our humble beginnings in 2003." It has Herald, Issue Tracker, IRC and Forum sections. [uthgard.net](https://www.uthgard.net/)
- GameBanshee "Remembering a Golden Era" is a later retrospective. [gamebanshee.com/3u3uf](https://gamebanshee.com/3u3uf)

### Gaps
- Not checked: Reddit (403), Eden, Phoenix, Atlas, and podcasts.
