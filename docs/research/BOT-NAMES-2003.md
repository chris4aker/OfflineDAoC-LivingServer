# Bot names: what a 2003 server's names looked like

Research for Living Server 1.1's bot names (owner 2026-10-29: "75% of names in 2003 style, rest in lore focused
names. Do research first."). The generator is `OfflineDaoc.Shared.EraBotNames`
(`source/server/GameServer/bots/autonomous/EraBotNames.cs`, linked into the launcher).

## What was looked at

- About 700 real character names from pages that could be opened. About 300 come from sources dated 2001-2004:
  - two full guild rosters with race, class and level (one dated July 2002);
  - forum threads from FreddysHouse, Quarter to Three, AnandTech, the Straight Dope boards and Usenet (via Tom's
    Hardware).
- Naming rules:
  - darkageofcamelot.com's Rules of Conduct (today's policy);
  - the Camelot Herald wiki's role-playing server naming pages;
  - the emulator's own character creation code.
- **Not reached:** the Wayback Machine (blocked from the research session), so no archived Camelot Herald character
  pages, and some guild sites.
- **Skew:** the 2001-2004 sample is about 70% European servers, and US coverage from 2002-2003 is thin.

## What the names were

Hand-tagged sample of 294 names from 2001-2004 (a name can have more than one tag):

| Pattern | Share | Examples |
|---|---|---|
| Made-up single names, short and often harsh | ~49% | Grizlas, Kalgarn, Zarff, Mezzar |
| English words and compounds | ~15% | Souleater, Whitepoison, Grayhawk, Darkcarver |
| Jokes and puns | ~14% | Sofragile, Fancypants, Hotsytotsy, Webmastah |
| Everyday first names | ~9% | Nate, Faye, Jenna, Eva |
| Real myth or history names | ~7% | Ottar, Asgeirr, Aoife, Eldred |
| Pop culture | ~6% | Tolkien, Dragonlance and film names |
| Class or race jokes | ~4% | Puppethealer, Meaniesavage, Champtastic |
| Stretched letters | ~3% | Droooood, Buffffffer |
| xX names | about 0% | |

- **Length:** median six letters; long names were almost always jokes or compounds. About three in ten had a z, x, k or
  q.
- **Rules:** letters only, one word, a capital first and lowercase after. No digits or spaces, which is why there are
  no xX names. A taken name got a letter doubled or added (Jammer, Jammerr).
- **Policy:** Mythic's policy banned pop culture, NPC and lore names, famous and historically significant names,
  titles, phrases and offensive names. Role-playing servers renamed out-of-theme names "aggressively"; normal servers
  had "lots of stupid names".
- **Era:** 2001-2005 jokes were innuendo, food, British slang and class jokes. A 2026 freeshard's names lean on memes,
  famous people and "lol" endings: the thing to keep out.
- **Too late for 2003:** Leeroy Jenkins (2005), Chuck Norris facts (2005), lolcats (2006-07), rickrolls (2007),
  Napoleon Dynamite and Anchorman (2004).

## The mix the generator uses

The 2003 shares, without pop culture and real myth names, scaled to 75. Lore names make up the other 25.

xX and number names were added on the owner's call (2026-10-29: "having no xX or Xx or numbers like 420 is a major
miss"). DAoC's own filter took letters only, so the research found almost none. They are the era's wider gamer names,
the ones a server that let them through would have had.

| Style | Share | Notes |
|---|---|---|
| Made-up | 35% | Short syllable names, light realm flavour |
| Compound | 12% | Two English words, never a word itself |
| Joke | 10% | Written in the 2003 vein, no innuendo; Sir, Mr or Lil plus a silly noun; "Healsalot" |
| First name | 7% | Common 2003 names in the US, the UK and northern Europe |
| Class or race joke | 3% | The bot's own class or its realm's races |
| Stretched | 2% | One letter held, four to six long in all |
| xX | 3% | XxShadowxX, Xxbloodxx, XxEvilFuryXx; never "xxx" (the invalid-names list) |
| Number | 3% | Zerga420, Headshot91, Winterfury2k3, a few letters swapped for digits; never 88, 14, 18 or 69 |
| Lore | 25% | Seven in ten the realm's old syllable names, three in ten real Old English, Norse or Irish names; no famous or Arthurian names |

## Guards

- **No chat words.**
  - A player's chat that names a bot is taken as said to that bot, so a bot called "Axe" would catch "anyone got an
    axe?".
  - So no name is a word the bots say, or an English word at least as common as "hellfire".
  - The list is `EraBotNameWords.cs`, made by `source/tools/make_era_name_words.py`.
- **The server's invalid-names list** (`config/invalidnames.txt`) applies to bots as it does to players.
- **No names copied from the research.** The written lists are the generator's own. A generic compound can still come
  out the same as a 2003 name (Grayhawk).
- **No pop culture, and no jokes from after 2003.**
- **Existing bots keep their names.** Only new bots get the new styles.

## Later: real rosters (planned for 1.2)

- **Precedent:** a fan server, Bindstone (r/daoc, October 2026), runs on bots that are the real Igraine guilds and
  characters from the old Camelot Herald, with their emblems.
- **For 1.2:** a "historical server" option, where the launcher imports a chosen server's guilds and characters from a
  roster file.
- It is opt-in and not bundled in the download, because those are real people's names. Where the data comes from is
  still open; one way is to ask Bindstone's author how he gathered his.
