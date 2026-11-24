#!/usr/bin/env python3
"""Realism 1b (#477; realism audit 1.2.3, docs/realism-audit-1.2.3/evenings.md): reads the player's evenings
(UT_ChatBudgetSimulation.PlaysThePlayersEvenings writes evening-transcripts/evenings.tsv; EVENING_ROW lines of a CI log work
too) as one player would and counts what breaks the illusion.

  evening_read.py rows.tsv --metrics            the rates per hour per scenario (report)
  evening_read.py rows.tsv --show KEY SEED MODE [--from MIN --to MIN]   one transcript, as the client shows it
  evening_read.py rows.tsv --flags KEY SEED MODE   that transcript's flagged moments with the lines around them
  evening_read.py rows.tsv --ratchet [evening_ratchet.json] [--write] [--github]
      The CI gate: seven counts that need no judgement (counts) may not get worse than the baseline; --write sets it.

Python standard library only. Tests only: the evenings are a model, not a world.
"""
import argparse, collections, json, re, sys
from pathlib import Path

RATCHET = Path(__file__).resolve().parent / "evening_ratchet.json"

FILLER = {"heh", "lol", "yikes", "ya", "haha", "hehe", "rofl", "yep", "yea", "yeah", "ok", "k", "kk", "np", "same", "ha", "lol yeah", "heh yeah",
          "lol same", "lol nice", "ugh", "ouch", "rip", "oof", "true", "nice", "hm", "hmm", "word", "indeed", "yup", "fair", "right", ":)", "lmao"}
EMOTE = re.compile(r"(\s+(lol|heh|haha|hehe|rofl|:\)|:p|:d|;\)|\*\w+\*))+$", re.I)


def norm(text):
    return re.sub(r"\s+", " ", re.sub(r"[^a-z0-9 ]", "", text.lower())).strip()


def bare(text):
    return norm(EMOTE.sub("", text))


def shape(text):
    """The line's shape: numbers and capitalised names masked, the trailing emote dropped."""
    t = EMOTE.sub("", text)
    t = re.sub(r"\d+", "#", t)
    t = re.sub(r"(?<!^)(?<![.!?] )\b[A-Z][a-z']+(?: [A-Z][a-z']+)*", "@", t)
    return norm(t.replace("#", " num ").replace("@", " name "))


def load(path):
    evenings = collections.OrderedDict()
    for raw in open(path, encoding="utf-8", errors="replace"):
        raw = raw.rstrip("\r\n")
        if "EVENING_ROW\t" in raw:
            raw = raw.split("EVENING_ROW\t", 1)[1]
        parts = raw.split("\t")
        if len(parts) < 10 or not parts[1].isdigit():
            continue
        key, seed, mode, at, channel, who, src, ex, i = parts[:9]
        text = "\t".join(parts[9:])
        evenings.setdefault((key, seed, mode), []).append(
            dict(at=int(at), ch=channel, who=who, src=src, ex=int(ex), i=int(i), text=text))
    for rows in evenings.values():
        rows.sort(key=lambda r: r["at"])
        for n, r in enumerate(rows):
            r["n"] = n
    return evenings


def stamp(at):
    return f"{at // 3600}:{at % 3600 // 60:02d}:{at % 60:02d}"


def render(r):
    ch, who, text = r["ch"], r["who"], r["text"]
    if ch == "sys":
        return f"[{stamp(r['at'])}] *** {text}"
    if ch == "meta":
        return f"[{stamp(r['at'])}] ((sim: {text}))"
    if ch == "tell":
        if r["src"].startswith("player>"):
            return f"[{stamp(r['at'])}] You send, \"{text}\" to {r['src'][7:]}"
        return f"[{stamp(r['at'])}] {who} sends, \"{text}\""
    label = {"faction": "[Faction]", "lfg": "[LFG]", "trade": "[Trade]", "say": "", "group": "[Party]", "guild": "[Guild]"}.get(ch, "[" + ch + "]")
    if ch == "say":
        return f"[{stamp(r['at'])}] {who} says, \"{text}\""
    return f"[{stamp(r['at'])}] {label} {who}: \"{text}\""


LEVEL = re.compile(r"\b(?:im|i'm|i am|me at|here at)\s+(?:lvl |level )?(\d{1,2})\b(?!\s*(?:%|min|sec|bub|gold|g\b|p\b|s\b|k\b|hours?|hrs?))", re.I)
DING = re.compile(r"\bding(?:ed)?\s+(\d{1,2})\b", re.I)
PLACE = re.compile(r"\b(?:im|i'm|i am)\s+(?:out |over |still |back |down |up )?(?:in|at)\s+((?:the )?[A-Za-z' ]{4,30}?)(?:\s+(?:atm|now|rn|if|and|but|with|for|so)\b|[,.!?]|$)", re.I)
CLASSES = ["armsman", "cleric", "wizard", "friar", "minstrel", "paladin", "sorcerer", "scout", "infiltrator", "theurgist", "cabalist", "mercenary",
           "necromancer", "reaver", "warrior", "healer", "runemaster", "shaman", "skald", "thane", "hunter", "berserker", "spiritmaster", "shadowblade",
           "bonedancer", "savage", "hero", "druid", "eldritch", "bard", "enchanter", "champion", "ranger", "mentalist", "warden", "blademaster",
           "nightshade", "animist"]
MYCLASS = re.compile(r"\b(?:im a|i'm a|i am a|im an|as a|my)\s+(\d{1,2} )?(" + "|".join(CLASSES) + r")\b", re.I)


def analyse(rows):
    """Flags: a list of (kind, row index, detail)."""
    flags = []
    chat = [r for r in rows if r["ch"] not in ("sys", "meta")]
    bot = [r for r in chat if not r["src"].startswith("player")]
    by_ex = collections.defaultdict(list)
    for r in chat:
        if r["ex"] != 0:
            by_ex[r["ex"]].append(r)
    by_channel = collections.defaultdict(list)
    for r in chat:
        by_channel[r["ch"]].append(r)
    position = {id(r): k for rs in by_channel.values() for k, r in enumerate(rs)}
    # 1. replies whose opener is missing, stale, far up the channel, or elsewhere
    for ex, lines in by_ex.items():
        if ex < 0:
            continue
        lines.sort(key=lambda r: (r["at"], r["i"]))
        opener = next((r for r in lines if r["i"] == 0), None)
        for r in lines:
            if r["i"] == 0:
                continue
            if opener is None:
                # not the first minutes (the opener was before the transcript) and not the gold-spam reactions (the sim has no ads)
                if r["at"] > 180 and not r["src"].startswith("spamreact"):
                    flags.append(("reply_no_opener", r["n"], "its opener was never shown"))
                continue
            if opener["ch"] != r["ch"]:
                flags.append(("reply_wrong_channel", r["n"], f"opener in {opener['ch']}"))

            if r["src"].startswith(("debate", "story")):
                continue
            prev = max((x for x in lines if x["at"] <= r["at"] and x is not r), key=lambda x: x["at"], default=opener)
            gap = r["at"] - prev["at"]
            between = position[id(r)] - position[id(prev)] - 1 if prev["ch"] == r["ch"] else 0
            # the asker's own "bump" or "nvm" minutes later is meant; another bot's late answer is not
            if r["who"] != opener["who"] and (gap > 90 or between >= 6):
                flags.append(("reply_stale", r["n"], f"{gap}s and {between} lines after the line it answers"))
    # the answers to the player's own lines
    said = [r for r in rows if r["src"].startswith("player")]
    for r in chat:
        if r["src"].startswith("to-player") and r["ex"] < 0:
            asked = said[-r["ex"] - 1] if -r["ex"] - 1 < len(said) else None
            # ex = -(script index + 1): skipped script lines shift it, so match by time instead
            before = [p for p in said if p["at"] <= r["at"] and p["ch"] == r["ch"]]
            asked = before[-1] if before else None
            if asked is None:
                flags.append(("reply_wrong_channel", r["n"], "an answer to the player in a channel the player did not speak in"))
                continue
            between = position[id(r)] - position[id(asked)] - 1
            gap = r["at"] - asked["at"]
            named = "wynn" in r["text"].lower()
            if (between >= 4 and not named) or gap > 45:
                flags.append(("player_answer_stale", r["n"], f"{gap}s, {between} lines after the player's line, {'named' if named else 'not named'}"))
            if "[world" in r["text"]:
                pass
    for r in rows:
        if r["ch"] == "meta" and r["src"] == "to-player:none":
            flags.append(("player_unanswered", r["n"], r["text"]))
        if r["ch"] == "meta" and r["src"] == "sim-error":
            flags.append(("sim_error", r["n"], r["text"]))
    # 2. one bot's share of a 10-line window, per channel
    for ch, rs in by_channel.items():
        if ch in ("tell",):
            continue
        k = 0
        while k + 10 <= len(rs):
            window = [x for x in rs[k:k + 10] if not x["src"].startswith(("player", "debate", "spam"))]
            if window:
                who, n = collections.Counter(x["who"] for x in window).most_common(1)[0]
                if n >= 3 and not (ch == "group"):
                    flags.append(("hog", rs[k]["n"], f"{who} has {n} of 10 lines in {ch}"))
                    k += 10
                    continue
                if n >= 5 and ch == "group":
                    flags.append(("hog", rs[k]["n"], f"{who} has {n} of 10 lines in {ch}"))
                    k += 10
                    continue
            k += 1
    # 3. threads that end on filler
    for ex, lines in by_ex.items():
        if len(lines) < 2 or lines[0]["src"].startswith(("debate", "spam")):
            continue
        last = max(lines, key=lambda r: (r["at"], r["i"]))
        if bare(last["text"]) in FILLER or norm(last["text"]) in FILLER:
            flags.append(("ends_on_filler", last["n"], f"a {len(lines)}-line thread"))
    for r in bot:
        if (bare(r["text"]) in FILLER or norm(r["text"]) in FILLER) and r["i"] == 0 and not r["src"].startswith("to-player"):
            flags.append(("filler_alone", r["n"], "filler that answers nothing visible"))
    # 4. the same line, or the same shape, within an hour
    seen, shapes = {}, {}
    for r in bot:
        if r["src"].startswith(("spam", "combat", "lead")) and r["ch"] != "group":
            continue
        key = (norm(r["text"]))
        if len(key.split()) >= 3:
            if key in seen and r["at"] - seen[key]["at"] <= 3600:
                flags.append(("same_line", r["n"], f"said {r['at'] - seen[key]['at']}s ago by {seen[key]['who']} in {seen[key]['ch']}"))
            seen[key] = r
        sk = shape(r["text"])
        if len(sk.split()) >= 5 and sk != key:
            if sk in shapes and r["at"] - shapes[sk]["at"] <= 3600 and norm(shapes[sk]["text"]) != key:
                flags.append(("same_shape", r["n"], f"same shape {r['at'] - shapes[sk]['at']}s ago: \"{shapes[sk]['text']}\""))
            shapes[sk] = r
    # short lines (under three words) said again in the same channel within ten minutes
    short = {}
    for r in bot:
        key = (r["ch"], norm(r["text"]))
        if 0 < len(key[1].split()) < 3 and not r["src"].startswith(("combat",)):
            if key in short and r["at"] - short[key] <= 600:
                flags.append(("same_short", r["n"], f"\"{r['text']}\" again after {r['at'] - short[key]}s"))
            short[key] = r["at"]
    # 5. a bot contradicting itself: level, class, place
    levels, classes, places = collections.defaultdict(list), collections.defaultdict(list), collections.defaultdict(list)
    for r in bot:
        for m in LEVEL.finditer(r["text"]):
            levels[r["who"]].append((int(m.group(1)), r))
        for m in DING.finditer(r["text"]):
            levels[r["who"]].append((int(m.group(1)), r))
        for m in MYCLASS.finditer(r["text"]):
            classes[r["who"]].append((m.group(2).lower(), r))
        for m in PLACE.finditer(r["text"]):
            places[r["who"]].append((m.group(1).lower().strip(), r))
    for who, claims in levels.items():
        values = sorted({v for v, _ in claims if 1 <= v <= 50})
        if len(values) > 1 and values[-1] - values[0] > 2:
            flags.append(("contradiction_level", claims[-1][1]["n"], f"{who} gives levels {values}: " + " | ".join(f"\"{r['text']}\"" for _, r in claims[:4])))
    for who, claims in classes.items():
        values = sorted({v for v, _ in claims})
        if len(values) > 1:
            flags.append(("contradiction_class", claims[-1][1]["n"], f"{who} is {values}: " + " | ".join(f"\"{r['text']}\"" for _, r in claims[:4])))
    for who, claims in places.items():
        for (p1, r1), (p2, r2) in zip(claims, claims[1:]):
            if p1 != p2 and r2["at"] - r1["at"] < 300:
                flags.append(("contradiction_place", r2["n"], f"{who}: \"{r1['text']}\" then {r2['at'] - r1['at']}s later \"{r2['text']}\""))
    # 6. events nobody reacts to
    for r in rows:
        if r["ch"] != "sys":
            continue
        after = [x for x in chat if r["at"] <= x["at"] <= r["at"] + 40 and x["ch"] in ("say", "group") and not x["src"].startswith("player")]
        kind = ("death_player" if "You have died" in r["text"] else "ding_player" if "You have reached" in r["text"]
                else "death_bot" if "was just killed" in r["text"] else "ding_bot" if "glows" in r["text"]
                else "join" if "has joined" in r["text"] else "leave" if "has left" in r["text"] else "join_self" if "You join" in r["text"] else None)
        if kind is None:
            continue
        words = {"death_player": r"rez|ouch|rip|release|ugh|nasty|sorry|down|dead|died|oof|hit hard", "ding_player": r"gr[a]?t[sz]|gz|grats|congrat|ding|woot|nice",
                 "death_bot": r"rez|ouch|rip|down|dead|died|oof|lost|ugh", "ding_bot": r"gr[a]?t[sz]|gz|grats|congrat|ding|woot|nice",
                 "join": r"\bhi\b|hey|welcome|wb|hello", "leave": r"bye|cya|later|ld|left|lost|gone|need (a|one)", "join_self": r"welcome|hi\b|hey|hello"}[kind]
        if not any(re.search(words, x["text"], re.I) for x in after):
            flags.append(("event_ignored:" + kind, r["n"], r["text"][:80]))
    # 7. bot questions nobody answers
    for ex, lines in by_ex.items():
        opener = next((r for r in lines if r["i"] == 0), None)
        if opener and len(lines) == 1 and opener["text"].rstrip().rstrip(")(:;Ppd ").endswith("?") and not opener["src"].startswith(("player", "spam", "debate")) \
                and opener["ch"] in ("faction", "say", "group", "guild"):
            flags.append(("question_unanswered", opener["n"], opener["ch"]))
    return flags


def metrics(evenings):
    out = []
    kinds = ["reply_no_opener", "reply_wrong_channel", "reply_stale", "player_answer_stale", "player_unanswered", "hog", "ends_on_filler", "filler_alone",
             "same_line", "same_shape", "same_short", "contradiction_level", "contradiction_class", "contradiction_place", "question_unanswered",
             "event_ignored:death_player", "event_ignored:ding_player", "event_ignored:death_bot", "event_ignored:ding_bot", "event_ignored:join",
             "event_ignored:leave", "event_ignored:join_self", "sim_error"]
    groups = collections.defaultdict(list)
    for (key, seed, mode), rows in evenings.items():
        groups[(re.sub(r"-(alb|mid|hib)$", "", key), mode)].append((key, seed, rows))
    for (scenario, mode), runs in sorted(groups.items()):
        hours = sum(max(r["at"] for r in rows) / 3600.0 for _, _, rows in runs if rows)
        out.append(f"\n## {scenario}, player {mode}: {len(runs)} evenings, {hours:.1f} h")
        per_channel = collections.Counter()
        speakers = collections.defaultdict(set)
        total = collections.Counter()
        to_player = 0
        multi = 0
        for key, seed, rows in runs:
            for r in rows:
                if r["ch"] in ("sys", "meta") or r["src"].startswith("player"):
                    continue
                per_channel[r["ch"]] += 1
                speakers[r["ch"]].add((key, seed, r["who"]))
                if r["src"].startswith(("to-player", "tell", "engage", "lead")) or "wynn" in r["text"].lower():
                    to_player += 1
            for kind, _, _ in analyse(rows):
                total[kind] += 1
            ex = collections.Counter(r["ex"] for r in rows if r["ex"] > 0)
            multi += sum(1 for n in ex.values() if n >= 2)
        out.append("lines a minute: " + ", ".join(f"{ch} {n / hours / 60:.2f}" for ch, n in sorted(per_channel.items())) +
                   f"; all {sum(per_channel.values()) / hours / 60:.2f}")
        out.append("lines an hour: " + ", ".join(f"{ch} {n / hours:.0f}" for ch, n in sorted(per_channel.items())))
        out.append(f"bot-to-bot threads (2+ lines) an hour: {multi / hours:.1f}; lines to or about the player an hour: {to_player / hours:.1f}")
        out.append("per hour: " + ", ".join(f"{k} {total[k] / hours:.2f}" for k in kinds if total[k]))
    return "\n".join(out)


# ------------------------------------------------------------------------------------------------ the ratchet (#477)

STOCK_SAY = {"sry ks", "rdy", "brb", "1 sec"}
# The seven counts evenings.md names, each with the way it may move: "max" may not rise, "min" may not fall.
COUNTS = {
    "party_banter_after_60": ("min", "party banter lines after minute 60, all party evenings (the pool ran dry at 30-39)"),
    "say_stock_share": ("max", "share of bot /say lines that are \"sry ks\", \"rdy\", \"brb\" or \"1 sec\""),
    "faction_top10_share": ("max", "the ten busiest bots' share of /faction, the worst evening"),
    "how_much_paying_per_hour": ("max", "\"how much u paying?\" in /trade an hour"),
    "name_first_share": ("max", "share of another bot's /faction thread answers that open with the asker's name"),
    "laughing_calls_share": ("max", "share of fight calls and lead lines that end on a laugh or smiley"),
    "party_unanswered": ("max", "the player's scripted /group lines with no answer, a party evening on average"),
}


def counts(evenings):
    """The seven counts over every evening in the file (pure)."""
    banter = 0
    say_all = say_stock = 0
    top10 = 0.0
    paying = 0
    hours = 0.0
    answers = named = 0
    calls = laughing = 0
    unanswered, party_evenings = 0, 0
    for (key, seed, mode), rows in evenings.items():
        if rows:
            hours += max(r["at"] for r in rows) / 3600.0
        chat = [r for r in rows if r["ch"] not in ("sys", "meta")]
        bot = [r for r in chat if not r["src"].startswith("player")]
        for r in bot:
            if r["ch"] == "group" and r["src"].startswith("group:") and r["at"] >= 3600:
                banter += 1
            if r["ch"] == "say":
                say_all += 1
                say_stock += bare(r["text"]) in STOCK_SAY
            if r["ch"] == "trade" and norm(r["text"]) == "how much u paying":
                paying += 1
            if r["src"].startswith(("combat:", "lead:")):
                calls += 1
                laughing += bool(EMOTE.search(r["text"]))
        speakers = collections.Counter(r["who"] for r in bot if r["ch"] == "faction" and not r["src"].startswith(("spam", "debate")))
        if speakers:
            top10 = max(top10, sum(n for _, n in speakers.most_common(10)) / sum(speakers.values()))
        openers = {r["ex"]: r for r in chat if r["i"] == 0 and r["ex"] > 0}
        for r in bot:
            if r["ch"] == "faction" and r["src"].startswith("thread:") and r["i"] > 0 and r["ex"] in openers and openers[r["ex"]]["who"] != r["who"]:
                answers += 1
                named += r["text"].lower().startswith(openers[r["ex"]]["who"].lower() + ",")
        if "dungeon" in key and mode == "speaks":
            party_evenings += 1
            said = [r for r in rows if r["ch"] == "group" and r["src"] == "player"]
            for k, p in enumerate(said):
                until = said[k + 1]["at"] if k + 1 < len(said) else p["at"] + 120
                if not any(r["ch"] == "group" and r["src"].startswith("to-player") and p["at"] <= r["at"] <= min(until, p["at"] + 120) for r in rows):
                    unanswered += 1
    share = lambda a, b: round(a / b, 4) if b else 0.0
    return dict(party_banter_after_60=banter, say_stock_share=share(say_stock, say_all), faction_top10_share=round(top10, 4),
                how_much_paying_per_hour=round(paying / hours, 2) if hours else 0.0, name_first_share=share(named, answers),
                laughing_calls_share=share(laughing, calls), party_unanswered=round(unanswered / party_evenings, 2) if party_evenings else 0.0)


def check(now, base):
    """(problems, slack): a count worse than its baseline, or better (pure)."""
    problems, slack = [], []
    for name, (way, what) in COUNTS.items():
        if name not in base:
            continue
        n, b = now[name], base[name]
        worse = n > b + 1e-9 if way == "max" else n < b - 1e-9
        better = n < b - 1e-9 if way == "max" else n > b + 1e-9
        if worse:
            problems.append(f"{name} {n} is worse than its baseline {b} ({what})")
        elif better:
            slack.append(f"{name} {n} (baseline {b})")
    return problems, slack


def ratchet(evenings, path, write, github):
    now = counts(evenings)
    line = f"{len(evenings)} evenings: " + ", ".join(f"{k} {v}" for k, v in now.items())
    if write:
        Path(path).write_text(json.dumps(dict(note="The evening ratchet (evening_read.py --ratchet): seven counts of the player's evenings "
                                                    "(UT_ChatBudgetSimulation.PlayerEvenings) that need no judgement may not get worse. "
                                                    "Tighten with --write after a fix; never loosen it to pass. Tests only.",
                                               evenings=len(evenings), **now), indent=1) + "\n", encoding="utf-8")
        print(line)
        print(f"wrote {path}")
        return 0
    base = json.loads(Path(path).read_text(encoding="utf-8"))
    problems, slack = check(now, base)
    if base.get("evenings") and base["evenings"] != len(evenings):
        problems.append(f"{len(evenings)} evenings, the baseline has {base['evenings']}")
    if github:
        print(f"::notice title=Evening transcripts ratchet::{line}")
        for p in problems:
            print(f"::error title=Evening transcripts ratchet::{p}")
        if slack and not problems:
            print("::notice title=Evening transcripts ratchet::better than the baseline (" + "; ".join(slack) +
                  "): tighten tools/bot-voices/eval/evening_ratchet.json with evening_read.py --ratchet --write")
    print(line)
    for p in problems:
        print("RATCHET FAIL: " + p)
    return 1 if problems else 0


def main(argv=None):
    parser = argparse.ArgumentParser()
    parser.add_argument("rows")
    parser.add_argument("--metrics", action="store_true")
    parser.add_argument("--show", nargs=3)
    parser.add_argument("--flags", nargs=3)
    parser.add_argument("--from", dest="lo", type=float, default=0)
    parser.add_argument("--to", dest="hi", type=float, default=1e9)
    parser.add_argument("--src", action="store_true", help="with each line's source tag")
    parser.add_argument("--kind", default=None)
    parser.add_argument("--ratchet", nargs="?", const=str(RATCHET), default=None, help="the baseline (evening_ratchet.json)")
    parser.add_argument("--write", action="store_true", help="with --ratchet: write the baseline from these rows")
    parser.add_argument("--github", action="store_true")
    args = parser.parse_args(argv)
    evenings = load(args.rows)
    if args.ratchet:
        if args.metrics:
            print(metrics(evenings))
        return ratchet(evenings, args.ratchet, args.write, args.github)
    if args.metrics:
        print(f"{len(evenings)} evenings: " + ", ".join(sorted({'/'.join(k) for k in evenings})))
        print(metrics(evenings))
    if args.show:
        rows = evenings[tuple(args.show)]
        flagged = collections.defaultdict(list)
        for kind, n, detail in analyse(rows):
            flagged[n].append(kind)
        for r in rows:
            if args.lo * 60 <= r["at"] <= args.hi * 60:
                tail = (f"    <{r['src']} ex={r['ex']}.{r['i']}>" if args.src else "") + ("   !! " + ",".join(flagged[r["n"]]) if flagged[r["n"]] and args.src else "")
                print(render(r) + tail)
    if args.flags:
        rows = evenings[tuple(args.flags)]
        for kind, n, detail in sorted(analyse(rows), key=lambda f: (f[0], f[1])):
            if args.kind and not kind.startswith(args.kind):
                continue
            print(f"--- {kind}: {detail}")
            for r in rows[max(0, n - 4):n + 3]:
                print(("  > " if r["n"] == n else "    ") + render(r))


if __name__ == "__main__":
    sys.exit(main())
