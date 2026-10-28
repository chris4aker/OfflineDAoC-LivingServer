# The talking update: new chat content

This is the brief for the corpus lines that go with the talking update (Living Server Beta 1.1). The code that asks for them
is described in docs/bot-voices/TALKING-UPDATE.md. Read docs/bot-voices/DESIGN.md section 3 for the `.lines` format and look
at the existing files in `source/server/GameServer/bots/autonomous/data/chat/` before writing anything.

Check your files with `python tools/bot-voices/build_corpus.py --check`. It must report no problems.

## Rules for every line

- **Era:** chat as players typed it in 2002-2004 Dark Age of Camelot (classic and Shrouded Isles). Mostly lower case, short,
  common abbreviations (lfg, lfm, inc, oom, rez, pst, gz, ty, np, brb, afk, omw, imo, ya). No later internet phrases; the build
  bans the worst (see `BANNED` in build_corpus.py). Nothing from Trials of Atlantis or later.
- **Length:** 140 characters at most; most lines 20-70.
- **Truth:** a line may only claim what is true. Anything about the speaker's own state (where it is, what it is doing, how its
  xp is going, what just happened) needs the flag that makes it true (`@needs ...`). Names of places must be real places of the
  line's realm (see `places.json`); a line that names a place either claims the speaker is there or on the way
  (`@needs at:<place>`, `going:<place>`, `near:<place>`) or says it is only talking about it (`@about place`).
- **Slots** fill real values: `{keep}`, `{relic}`, `{raid}`, `{boss}`, `{enemy}` (a realm name), `{zone}`, `{winner}`, `{loser}`,
  `{player}`, `{place}`, `{level}`, `{class}`, `{bubbles}` (0-9), `{need}` ("a healer", "a tank", "a puller", "dps"), `{camp}`
  (a zone), `{monster}`/`{monsters}`, `{size}` (a number). A line is only said when every slot it uses has a value.
- **Variety:** different openings, lengths and moods. Write for the six personalities where it fits (`@who Socialite` etc.).
  No two lines that say the same thing in different words.
- **Realms:** `@realm *` for lines any realm can say. A realm-specific joke or place gets `@realm alb|mid|hib`.

## New vocabulary

Kinds: `news` (a bot tells /faction about something that happened), `tell` (a private message a bot starts).

Flags (for `@needs`):

| Flag | True when |
|---|---|
| `defending` | one of the realm's keeps is under siege now (`{keep}` is that keep, `{enemy}` the attacker) |
| `attacking` | the realm's army is besieging an enemy keep now (`{keep}` is the target) |
| `keep_news` | a frontier keep changed hands in the last hour |
| `keep_lost_recent` / `keep_won_recent` | the realm lost / took `{keep}` in the last hour |
| `relic_lost_recent` / `relic_won_recent` | the realm lost / took `{relic}` in the last few hours |
| `raid_won_recent` / `raid_wiped_recent` | a realm raid beat / wiped on `{boss}` in `{raid}` in the last hour |
| `df_ours`, `df_changed_recent` | the realm holds Darkness Falls; it changed hands in the last hour |
| `witness` | (news) the speaker was there when it happened |
| `recruiting` | (lfg) the speaker's group is really looking for `{need}` at `{camp}` |
| `xp_low`, `xp_mid`, `xp_high` | under 2, 2-7, 8 or more bubbles into the level (`{bubbles}`) |
| `is_healer`, `is_tank`, `is_caster`, `is_support` | the speaker's group role |
| `newbie`, `veteran` | level 1-14, level 40+ |
| `in_combat` | fighting now |

Answer sets (`@answers` on `reply` lines): `news_<topic>` answers that news, then `news` answers any news;
`followup_why`, `followup_really`, `followup_how`, `followup_what`, `followup_who` answer a player who asks that right after the
speaker's own line. A follow-up line may set `@topic` to the family of the line it follows up (`boast`, `complaint`, `joke`,
`news`, `chat`, `ask`, `lfg`, `rvr`); without a topic it fits any.

## What to write

### File `news.lines` (kind `news`, `@invites news_<topic>`)

One block per topic. Each topic needs at least 8 lines: some `@needs witness` ("we just lost the gate at {keep}, they had
rams"), most `@needs !witness` ("heard we lost {keep}"). Slots as listed.

| Topic | What happened | Slots |
|---|---|---|
| `siege_defend` | `{enemy}` is attacking our `{keep}` now (call for defenders) | keep, enemy |
| `siege_attack` | our army is marching on / hitting `{keep}`, an `{enemy}` keep | keep, enemy |
| `siege_held` | we held `{keep}` against `{enemy}` | keep, enemy |
| `siege_failed` | our attack on `{keep}` failed | keep, enemy |
| `keep_won` | we took `{keep}` from `{loser}` | keep, loser |
| `keep_lost` | `{winner}` took our `{keep}` | keep, winner |
| `keep_other` | `{winner}` took `{keep}` from `{loser}` (two other realms) | keep, winner, loser |
| `relic_lost` | `{enemy}` took our relic `{relic}` | relic, enemy |
| `relic_won` | we took `{relic}` from `{loser}` | relic, loser |
| `relic_back` | our `{relic}` is back home | relic |
| `df_won` | Darkness Falls is ours | (none) |
| `df_lost` | `{winner}` has Darkness Falls now | winner |
| `raid_forming` | a realm raid is forming for `{raid}` | raid |
| `raid_won` | the realm raid killed `{boss}` in `{raid}` | raid, boss |
| `raid_wiped` | the raid wiped on `{boss}` in `{raid}` | raid, boss |
| `enemy_seen` | a `{size}`-strong `{enemy}` group seen in `{zone}` | enemy, zone, size |

### File `news_replies.lines` (kind `reply`)

Reactions in /faction, 6-10 per topic (`@answers news_<topic>`) plus 25 that fit any news (`@answers news`): "again??",
"who was on?", "gz all", "omw", "lol of course", "how many were there?". A reaction that offers to go somewhere only says
so with `@needs in_frontier` or similar.

### File `tells.lines` (kind `tell`, `@needs knows_player` unless said otherwise)

| Topic | When | Count |
|---|---|---|
| `hello_friend` | a bot that knows the player sees them log in or come near: "hey {player}, back for more?" | 15 |
| `after_group` | grouped with the player before: "ty for the group the other night" | 10 |
| `group_offer` | close in level, same zone (`@needs !knows_player` allowed): "hey, want to duo here? im {level} {class}" | 15 |
| `help_offer` | the player is a low level near the bot: "need a hand with anything?" | 10 |
| `news_tell` | shares news privately; needs the news flag: "did you see we lost {keep}?" | 10 |
| `guildmate` | a guildmate (`@needs same_guild` is not a flag; use topic only): small talk | 15 |

### File `memory_more.lines` (kind `greet`, `@needs knows_player`)

25 greetings that recall the last place or the last group with the player: "{player}! how did {place} go?",
"hey {player}, still hunting {place}?". `{place}` is where the bot last saw them.

## Combat, groups and follow-ups (second file set)

### File `combat_calls.lines` (kind `combat`, `@needs in_player_group`)

Group calls, one block per topic, 6-12 lines each, very short:

`pull` (the bot pulls or sees the pull coming), `add` (another monster joined), `oom` (out of power; `@needs is_caster` or
`is_healer`), `low_hp` (the bot is hurt), `heal_big` (`@needs is_healer`: a group heal), `mez` / `mez_broke` (`@needs is_support`
or `is_caster`), `root`, `stun`, `taunt` (`@needs is_tank`), `kill` (a fight just ended well), `wipe` (the group is dying: run),
`rest` (after the fight: med, rebuff), `rez` (`@needs can_rez`: rezzing someone).

### File `lfg_real.lines` (kind `lfg`)

- `@topic lfg_self`: 30 lines, a solo bot looking for a group, real values: "lfg {level} {class}, anything", "{class} {level} lfg
  {zone} or close".
- `@topic lfm` and `@needs recruiting`: 40 lines, a bot group looking for `{need}` at `{camp}`: "lfm {need}, {camp}, pst".
- `@topic join`: 15 lines in /group when the bot joins the player's group: "hi all, ty for the inv".

### File `followups.lines` (kind `reply`)

25 lines each for `followup_why`, `followup_really`, `followup_how`, `followup_what`, 10 for `followup_who`. Many generic
("dunno, just feels that way", "swear", "yep, no joke"), some with `@topic boast|complaint|joke|news|lfg|rvr` that only fit a
line of that family.

### File `closings_more.lines` (kind `closing`)

40 closings a bot says when a player answers its question: `@answers ask` ("ty", "oh nice ty"), `ask_where` ("ty, heading
there"), `ask_opinion` ("fair point"), `ask_anyone` ("ok cool"), `lfg` ("np, maybe later").

### File `camp_more.lines` (kind `camp`, `@needs at_camp`) and `guild_more.lines` (kind `guild`)

40 camp lines (said in /say at a camp: the monsters, the pulls, the spawn) and 60 guild lines (guild chat: plans, help,
crafting, jokes, real-life breaks).

### File `experience.lines` (kind `ambient`)

40 lines `@needs newbie` (new player questions and wonder: "how do i get my spells?", "first time in a dungeon, so dark") and
40 `@needs veteran` (seasoned talk: templates, realm abilities, old stories).
