#!/usr/bin/env python3
"""Realism audit 1.2.3: reads UT_EveningTranscripts' rows (EVENING_ROW lines of a CI log, or evenings.tsv) as one player
would and counts what breaks the illusion. Report only.

  evening_read.py rows.tsv --metrics            the rates per hour per scenario
  evening_read.py rows.tsv --show KEY SEED MODE [--from MIN --to MIN]   one transcript, as the client shows it
  evening_read.py rows.tsv --flags KEY SEED MODE   that transcript's flagged moments with the lines around them
"""
import argparse, collections, re, sys

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
                flags.append(("reply_no_opener", r["n"], "its opener was never shown"))
                continue
            if opener["ch"] != r["ch"]:
                flags.append(("reply_wrong_channel", r["n"], f"opener in {opener['ch']}"))
            if opener["at"] > r["at"]:
                flags.append(("reply_before_opener", r["n"], ""))
            if r["src"].startswith(("debate", "story")):
                continue
            prev = max((x for x in lines if x["at"] <= r["at"] and x is not r), key=lambda x: x["at"], default=opener)
            gap = r["at"] - prev["at"]
            between = position[id(r)] - position[id(prev)] - 1 if prev["ch"] == r["ch"] else 0
            if gap > 90 or between >= 6:
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


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("rows")
    parser.add_argument("--metrics", action="store_true")
    parser.add_argument("--show", nargs=3)
    parser.add_argument("--flags", nargs=3)
    parser.add_argument("--from", dest="lo", type=float, default=0)
    parser.add_argument("--to", dest="hi", type=float, default=1e9)
    parser.add_argument("--src", action="store_true", help="with each line's source tag")
    parser.add_argument("--kind", default=None)
    args = parser.parse_args()
    evenings = load(args.rows)
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
