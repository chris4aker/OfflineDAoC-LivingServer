#!/usr/bin/env python3
"""The 1.2.2 chat audit's reports (docs/bot-voices/CHAT-AUDIT-1.2.2.md): bench and lines report; ratchet is the CI gate.

  score_bench.py bench REPLIES.json [--questions player_questions.json] [--json OUT] [--md OUT] [--github] [--worst N]
      Scores the answers UT_ChatQuestionBench wrote (one per question, through the real reply path) with chat_scorers.score:
      totals by category, by intent, by realm and by channel; the flags; the reply routes behind each bad category; the
      /faction flow (how many bots answer, how soon, echoes and twice-said replies); and the worst examples.

  score_bench.py ratchet REPLIES.json [--holdout HOLDOUT_REPLIES.json] [--baseline bench_ratchet.json] [--write] [--github]
      The CI gate (1.2.2 fixes): fails when the good share falls below bench_ratchet.json's floor or a capped category (wrong,
      non-answer, off-topic, fluff, silence where an answer was due, stub, clunky, echo, padded) grows past its cap; reports the
      holdout set (player_questions_holdout.json) beside it. --write sets the baseline from these replies.

  score_bench.py lines [--json OUT] [--worst N]
      Scans every written line (data/chat/*.lines through build_corpus.parse_file, scene beats included) for fluff and
      clunky patterns (spam and beggar lines, broken on purpose, are left out), and fills each template's slots with sample values (a vowel and a consonant first) to find slots
      that break the sentence once filled.

Python standard library only.
"""
import argparse
import collections
import json
import re
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
sys.path.insert(0, str(HERE.parent))

import chat_scorers as cs  # noqa: E402

QUESTIONS = HERE / 'player_questions.json'


def pct(n, d):
    return f'{100.0 * n / d:.1f}%' if d else '-'


# ------------------------------------------------------------------------------------------------------------------ bench

def route_family(route):
    """The component a route comes from: "fact world", "knowledge", "intent banter -> not sure" -> "intent -> not sure"..."""
    r = (route or '').strip()
    r = re.sub(r'^(silent|rescue|short|ack) \((.*)\)$', r'\1 <\2>', r)
    head = r.split(' ')[0]
    if head in ('silent', 'rescue', 'short', 'ack'):
        inner = route_family(r[len(head) + 2:-1]) if r.endswith('>') else ''
        return f'{head} <{inner}>'
    if '-> not sure' in r:
        return 'intent -> not sure'
    if head in ('fact', 'shape', 'live', 'context', 'intent', 'none', 'thread', 'knowledge'):
        parts = r.split(' ')
        return ' '.join(parts[:2]) if len(parts) > 1 else head
    return r or '(none)'


def score(questions_path, replies_path, worst=30):
    """Scores a bench replies file against its questions: the totals, groups, flow, worst examples and rows (pure)."""
    data = json.loads(Path(questions_path).read_text(encoding='utf-8'))
    questions = {q['id']: q for q in data['questions']}
    replies = json.loads(Path(replies_path).read_text(encoding='utf-8'))
    rows = []
    for r in replies:
        q = questions.get(r['id'])
        if q is None:
            continue
        cat, flags, why = cs.score(q, r.get('reply'))
        rows.append(dict(q=q, r=r, cat=cat, flags=flags, why=why, ok=cs.ok(q, cat),
                         issues=cs.clunky_issues(r.get('reply') or '')))
    missing = sorted(set(questions) - {row['q']['id'] for row in rows})
    total = len(rows)
    out = {'total': total, 'missing': missing, 'categories': {}, 'flags': {}, 'by_intent': {}, 'by_realm': {}, 'by_channel': {},
           'routes_by_category': {}, 'flow': {}, 'worst': {}, 'stubbed': sum(1 for row in rows if row['r'].get('stub'))}
    cats = collections.Counter(row['cat'] for row in rows)
    out['categories'] = {c: cats.get(c, 0) for c in cs.CATEGORIES}
    out['ok'] = sum(row['ok'] for row in rows)
    out['silence_ok'] = sum(1 for row in rows if row['cat'] == 'SILENT' and row['q'].get('silence_ok'))
    flags = collections.Counter(f for row in rows for f in row['flags'])
    out['flags'] = {f: flags.get(f, 0) for f in cs.FLAGS}
    # Over the replies the scorer judged (a "[world: ...]" stub marker is no reply).
    out['clunky_codes'] = dict(collections.Counter(code for row in rows if 'CLUNKY' in row['flags'] for code, _ in row['issues']).most_common())
    for key, field in (('by_intent', 'intent'), ('by_realm', 'realm'), ('by_channel', 'channel')):
        groups = collections.defaultdict(list)
        for row in rows:
            groups[row['q'][field]].append(row)
        out[key] = {g: dict(n=len(v), ok=sum(x['ok'] for x in v), **{c: sum(1 for x in v if x['cat'] == c) for c in cs.CATEGORIES},
                            CLUNKY=sum(1 for x in v if 'CLUNKY' in x['flags']))
                    for g, v in sorted(groups.items())}
    for c in cs.CATEGORIES:
        out['routes_by_category'][c] = dict(collections.Counter(route_family(row['r'].get('route')) for row in rows if row['cat'] == c).most_common(12))
    out['routes_by_category']['CLUNKY'] = dict(collections.Counter(route_family(row['r'].get('route')) for row in rows if 'CLUNKY' in row['flags']).most_common(12))
    # The /faction flow: the plan (how many bots answer), the first reply's delay, pile-ons said twice, echoes.
    fac = [row for row in rows if row['q']['channel'] == 'faction']
    answerers = collections.Counter(row['r'].get('answerers', 0) for row in fac)
    delays = sorted(row['r']['delay_ms'] for row in fac if row['r'].get('delay_ms'))
    out['flow'] = dict(
        faction_lines=len(fac), answerers=dict(sorted(answerers.items())),
        questions_with_more_than_one_answer=sum(1 for row in fac if row['r'].get('answerers', 0) > 1 and row['q']['intent'] != 'small_talk'),
        delay_ms_median=delays[len(delays) // 2] if delays else None, delay_ms_p90=delays[int(len(delays) * 0.9)] if delays else None,
        delay_ms_max=delays[-1] if delays else None,
        twice_said=sum(1 for row in fac if row['r'].get('twice')), echo=sum(1 for row in rows if 'ECHO' in row['flags']),
        silent_questions=sum(1 for row in fac if row['cat'] == 'SILENT' and not row['q'].get('silence_ok')),
    )
    for c in ('WRONG', 'NON_ANSWER', 'OFF_TOPIC', 'FLUFF', 'SILENT'):
        bad = [row for row in rows if row['cat'] == c and not row['ok']]
        out['worst'][c] = [example(row) for row in bad[:worst]]
    clunky = [row for row in rows if 'CLUNKY' in row['flags']]
    out['worst']['CLUNKY'] = [example(row) for row in clunky[:worst]]
    out['rows'] = [dict(id=row['q']['id'], intent=row['q']['intent'], realm=row['q']['realm'], channel=row['q']['channel'],
                        text=row['q']['text'], reply=row['r'].get('reply'), route=row['r'].get('route'), cat=row['cat'],
                        flags=row['flags'], why=row['why'], stub=row['r'].get('stub', False)) for row in rows]
    return out


def bench(args):
    out = score(args.questions, args.replies, args.worst)
    total = out['total']
    if args.json:
        Path(args.json).write_text(json.dumps(out, indent=1, ensure_ascii=False) + '\n', encoding='utf-8')
    text = render(out)
    if args.md:
        Path(args.md).write_text(text, encoding='utf-8')
    print(text)
    if args.github:
        c = out['categories']
        print(f"::notice title=Chat question bench (report only)::{total} questions: answered {c['ANSWERED']} ({pct(c['ANSWERED'], total)}), "
              f"wrong {c['WRONG']}, non-answer {c['NON_ANSWER']}, off-topic {c['OFF_TOPIC']}, fluff {c['FLUFF']}, silent {c['SILENT']} "
              f"({out['silence_ok']} fine), stub {c['STUB']}; clunky {out['flags']['CLUNKY']}")
    # The rows as one line each, between markers, so the CI log holds them (artifacts may not be readable).
    print('CHAT_BENCH_ROWS_BEGIN')
    for row in out['rows']:
        print('CHAT_BENCH_ROW ' + json.dumps(row, ensure_ascii=False))
    print('CHAT_BENCH_ROWS_END')
    return 0


def example(row):
    return dict(id=row['q']['id'], intent=row['q']['intent'], realm=row['q']['realm'], channel=row['q']['channel'], question=row['q']['text'],
                reply=row['r'].get('reply'), route=row['r'].get('route'), why=row['why'] if row['cat'] != 'CLUNKY' else '',
                issues=[f'{c}: {d}' for c, d in row['issues']])


def render(out):
    t = out['total']
    lines = [f"# Chat question bench: {t} questions", '']
    lines.append(f"Good outcomes (answered, or silent where silence is fine): {out['ok']} ({pct(out['ok'], t)}). "
                 f"Stubbed world answers: {out['stubbed']}.")
    lines.append('')
    lines.append('| category | n | % |')
    lines.append('|---|---:|---:|')
    for c, n in out['categories'].items():
        lines.append(f'| {c} | {n} | {pct(n, t)} |')
    for f, n in out['flags'].items():
        lines.append(f'| flag {f} | {n} | {pct(n, t)} |')
    lines.append('')
    for key in ('by_intent', 'by_realm', 'by_channel'):
        lines.append(f'## {key.replace("_", " ")}')
        lines.append('')
        lines.append('| group | n | ok | answered | wrong | non-answer | off-topic | fluff | silent | stub | clunky |')
        lines.append('|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|')
        for g, v in out[key].items():
            n = v['n']
            lines.append(f"| {g} | {n} | {pct(v['ok'], n)} | {v['ANSWERED']} | {v['WRONG']} | {v['NON_ANSWER']} | {v['OFF_TOPIC']} | "
                         f"{v['FLUFF']} | {v['SILENT']} | {v['STUB']} | {v['CLUNKY']} |")
        lines.append('')
    lines.append('## Routes behind each category')
    lines.append('')
    for c, routes in out['routes_by_category'].items():
        if routes:
            lines.append(f"- **{c}**: " + ', '.join(f'{r} {n}' for r, n in routes.items()))
    lines.append('')
    lines.append('## /faction flow')
    lines.append('')
    for k, v in out['flow'].items():
        lines.append(f'- {k}: {v}')
    lines.append('')
    lines.append('## Clunky issue codes')
    lines.append('')
    lines.append(', '.join(f'{k} {v}' for k, v in out['clunky_codes'].items()) or 'none')
    lines.append('')
    for c, rows in out['worst'].items():
        if not rows:
            continue
        lines.append(f'## Worst: {c}')
        lines.append('')
        for e in rows:
            why = e['why'] or '; '.join(e['issues'])
            lines.append(f"- [{e['realm']} {e['channel']} {e['intent']}] \"{e['question']}\" -> \"{e['reply'] or '(nothing)'}\" ({e['route']}; {why})")
        lines.append('')
    return '\n'.join(lines)


# ------------------------------------------------------------------------------------------------------------------ lines

SAMPLES = {
    'player': ('Wynn', 'Aeric'), 'target': ('ogre', 'large frost giant'), 'site': ('avalon marsh', 'myrkwood'), 'keep': ('caer erasleigh', 'bledmeer faste'),
    'enemy': ('albs', 'mids'), 'count': ('3', '12'), 'place': ('emain', 'vendo caverns'), 'level': ('8', '30'), 'class': ('armsman', 'thane'),
    'capital': ('jordheim', 'camelot'), 'player_class': ('eldritch', 'bard'), 'coin': ('80 gold', '5 silver'), 'item': ('axe', 'sword'),
    'other': ('Eira', 'Brannoc'), 'boss': ('Olcasgean', 'Legion'), 'hoard': ('11 plat', '4 gold'), 'nickname': ('ace', 'tank'),
    'camp': ('undead druids', 'bears'), 'npc': ('Elder Mabon', 'Ana'), 'zone': ('avalon marsh', 'myrkwood forest'), 'relic': ('relic of odin', 'scabbard'),
    'price': ('80g', '1p'), 'winner': ('Albion', 'Midgard'), 'bubbles': ('8', '3'), 'need': ('healer', 'enchanter'), 'monsters': ('ogres', 'trolls'),
    'other_class': ('infiltrator', 'skald'), 'destination': ('emain', 'muire tomb'), 'raid': ('Avalon City', 'Galladoria'), 'loser': ('Hibernia', 'Midgard'),
    'dungeon': ('Avalon City', 'Muire Tomb'), 'when': ('8 pm', 'tonight'), 'stock': ('11', '2'), 'enemy_mod': ('hib', 'alb'), 'recipe': ('arrows', 'helm'),
    'quality': ('81', '99'), 'monster': ('ogre', 'troll'), 'skill': ('alchemy', 'tailoring'), 'terms': ('80 for both', 'half up front'),
    'q': ('random question but', 'ok so'), 'frontier': ('emain', 'hadrians wall'), 'killer_class': ('infiltrator', 'skald'), 'size': ('8', '24'),
    'budget': ('80 gold', '5 gold'), 'killer': ('Eira', 'Brannoc'), 'title': ('elder', 'lord'), 'what': ('arrows', 'helm'), 'realm': ('albion', 'midgard'),
    'classes': ('eldritches', 'thanes'), 'name_b': ('Eira', 'Brannoc'), 'name_a': ('Aeric', 'Brannoc'),
}
SLOT = re.compile(r'\{([a-z_]+)\}')
RUNTIME_CODES = ('tacked_laugh', 'own_question', 'case_mix')
ARTICLE = re.compile(r'\b([Aa]) ([A-Za-z0-9][\w\']*)')


def fix_articles(text):
    """AutonomousBotVoiceCorpus.FixArticles: "a" made "an" before a word that takes it (the server does this to every line)."""
    return ARTICLE.sub(lambda m: m.group(1) + 'n ' + m.group(2) if cs.takes_an(m.group(2)) and m.group(2).lower() not in cs._NOT_NOUN else m.group(0), text)


def fill(template, which):
    return SLOT.sub(lambda m: SAMPLES.get(m.group(1), ('x', 'x'))[which], template)


def written_lines():
    import build_corpus
    problems = []
    for path in sorted(build_corpus.CHAT.glob('*.lines')):
        lines, scenes = build_corpus.parse_file(path, problems)
        for line in lines:
            yield line['where'], line['kind'], line['text'], answers_a_question(line)
        for scene in scenes:
            for beat in scene['beats']:
                for v in beat.get('variants', []):
                    yield beat['where'], 'scene', v, False


# The intents of a player line that asks for something (not a hello, a thanks or a laugh).
ASKING_INTENTS = {'help', 'dungeon', 'rvr', 'classtalk', 'trading', 'grouping', 'unknown', 'crafting', 'quest', 'leveling'}


def answers_a_question(line):
    """Whether a written reply is said to a question: its @answers names a question type or a follow-up, or its @intent asks."""
    if line.get('kind') not in ('reply', 'craft_reply', 'trade_reply', 'tell', 'rumour_reply', 'news_reply'):
        return False
    answers = line.get('answers') or []
    return any(a.startswith(('ask', 'followup')) or a == 'trade' for a in answers) or (line.get('intent') or '') in ASKING_INTENTS


def lines_report(args):
    total = 0
    by_kind = collections.Counter()
    fluff = collections.defaultdict(list)
    nonanswer = collections.defaultdict(list)
    clunky = collections.defaultdict(list)
    filled = collections.defaultdict(list)
    codes = collections.Counter()
    fill_codes = collections.Counter()
    per_file = collections.defaultdict(lambda: collections.Counter())
    seen = set()
    for where, kind, text, asked in written_lines():
        # Spam and beggar lines are broken on purpose ("W E L C O M E", "clink clink").
        if kind in ('spam', 'beggar') or (kind, text) in seen:
            continue
        seen.add((kind, text))
        total += 1
        by_kind[kind] += 1
        if asked:
            by_kind['(replies to a question)'] += 1
        f = where.split(':')[0]
        per_file[f]['lines'] += 1
        names = iter(range(1000))
        bare = SLOT.sub(lambda m: f'slot{next(names)}', text)
        # Replies and closings are the lines said to a player; a "lol" there is the fluff the owner means.
        if asked and cs.is_fluff(bare):
            fluff[kind].append((where, text))
            per_file[f]['fluff'] += 1
        elif asked and cs.is_non_answer(bare):
            nonanswer[kind].append((where, text))
            per_file[f]['non_answer'] += 1
        issues = [(c, d) for c, d in cs.clunky_issues(fix_articles(bare)) if c != 'unfilled_slot']
        # A written line may laugh, open with its own question or keep a name lower case on purpose; those codes judge a reply
        # stitched at run time (the bench), so here they are only counted.
        for c, _ in issues:
            if c in RUNTIME_CODES:
                codes[c + ' (info)'] += 1
        issues = [(c, d) for c, d in issues if c not in RUNTIME_CODES]
        if issues:
            clunky[kind].append((where, text, issues))
            per_file[f]['clunky'] += 1
            codes.update(c for c, _ in issues)
        if SLOT.search(text):
            before = {c for c, _ in issues}
            for which in (0, 1):
                said = fix_articles(fill(text, which))
                values = ' '.join(SAMPLES.get(n, ('x', 'x'))[which] for n in SLOT.findall(text)).lower()
                # A slot said twice by design ("heals on {target}") is not a clause repeated by the stitching.
                new = [(c, d) for c, d in cs.clunky_issues(said) if c not in before and c not in RUNTIME_CODES and not (c in ('repeated_clause', 'doubled_word') and
                                                                                          all(w in values for w in d.split()))]
                if new:
                    filled[kind].append((where, text, said, new))
                    per_file[f]['fill'] += 1
                    fill_codes.update(c for c, _ in new)
                    break
    out = dict(total=total, by_kind=dict(by_kind.most_common()), clunky_codes=dict(codes.most_common()), fill_codes=dict(fill_codes.most_common()),
               fluff={k: len(v) for k, v in fluff.items()}, non_answer={k: len(v) for k, v in nonanswer.items()}, clunky={k: len(v) for k, v in clunky.items()}, filled={k: len(v) for k, v in filled.items()},
               worst_files=sorted(((f, dict(c)) for f, c in per_file.items() if c['clunky'] + c['fill'] + c['fluff'] + c['non_answer']),
                                  key=lambda x: -(x[1].get('clunky', 0) + x[1].get('fill', 0) + x[1].get('fluff', 0) + x[1].get('non_answer', 0)))[:15],
               examples=dict(fluff=[w + ' ' + t for k in fluff for w, t in fluff[k][:args.worst // 3]][:args.worst],
                             non_answer=[w + ' ' + t for k in nonanswer for w, t in nonanswer[k][:args.worst // 2]][:args.worst],
                             clunky=[f'{w} {t} ({"; ".join(c + ": " + d for c, d in i)})' for k in clunky for w, t, i in clunky[k][:6]][:args.worst],
                             filled=[f'{w} {t} -> {s} ({"; ".join(c + ": " + d for c, d in i)})' for k in filled for w, t, s, i in filled[k][:8]][:args.worst]))
    if args.json:
        Path(args.json).write_text(json.dumps(out, indent=1, ensure_ascii=False) + '\n', encoding='utf-8')
    print(f"{total} written lines")
    print('replies to a question:', out['by_kind'].get('(replies to a question)'))
    print('fluff (said to a question):', sum(out['fluff'].values()), out['fluff'])
    print('non-answers (said to a question):', sum(out['non_answer'].values()), out['non_answer'])
    print('clunky:', sum(out['clunky'].values()), out['clunky_codes'])
    print('broken once filled:', sum(out['filled'].values()), out['fill_codes'])
    print('files with the most:', out['worst_files'][:8])
    for k, v in out['examples'].items():
        print(f'--- {k}')
        for e in v:
            print('  ' + e)
    return 0

RATCHET = HERE / 'bench_ratchet.json'
HOLDOUT = HERE / 'player_questions_holdout.json'
# The categories and flags the ratchet caps: none may grow. SILENT counts only silence where an answer was due.
CAPPED = ('WRONG', 'NON_ANSWER', 'OFF_TOPIC', 'FLUFF', 'SILENT', 'STUB', 'CLUNKY', 'ECHO', 'PADDED')


def ratchet_counts(out):
    """What the ratchet holds of a scored bench: the good count and share, and each capped category (bad silence only)."""
    c = dict(out['categories'])
    c['SILENT'] = c.get('SILENT', 0) - out['silence_ok']
    c.update(out['flags'])
    return dict(total=out['total'], ok=out['ok'], ok_share=round(out['ok'] / out['total'], 4) if out['total'] else 0.0,
                counts={k: c.get(k, 0) for k in CAPPED},
                by_intent={g: dict(n=v['n'], ok=v['ok']) for g, v in out['by_intent'].items()})


def check_ratchet(now, base):
    """The ratchet's verdict (pure): the problems (a capped count above its cap, the good share below its floor) and the slack
    (where the bench is now better than the baseline, so the baseline may be tightened with --write)."""
    problems, slack = [], []
    if now['ok_share'] + 1e-9 < base['ok_share']:
        problems.append(f"good share {now['ok_share']:.1%} fell below the floor {base['ok_share']:.1%} ({now['ok']} of {now['total']})")
    elif now['ok_share'] > base['ok_share'] + 1e-9:
        slack.append(f"good share {now['ok_share']:.1%} (floor {base['ok_share']:.1%})")
    for k in CAPPED:
        cap, n = base['counts'].get(k, 0), now['counts'].get(k, 0)
        if n > cap:
            problems.append(f'{k} {n} is over its cap {cap}')
        elif n < cap:
            slack.append(f'{k} {n} (cap {cap})')
    return problems, slack


def ratchet(args):
    out = score(args.questions, args.replies, args.worst)
    now = ratchet_counts(out)
    print(render(out))
    held = None
    if args.holdout and Path(args.holdout).exists():
        hout = score(args.holdout_questions, args.holdout, args.worst)
        held = ratchet_counts(hout)
        print('## Holdout set (written after the fixes, never used to tune them; reported, not capped)\n')
        print(render(hout))
        print('CHAT_HOLDOUT_ROWS_BEGIN')
        for row in hout['rows']:
            print('CHAT_HOLDOUT_ROW ' + json.dumps(row, ensure_ascii=False))
        print('CHAT_HOLDOUT_ROWS_END')
    print('CHAT_BENCH_ROWS_BEGIN')
    for row in out['rows']:
        print('CHAT_BENCH_ROW ' + json.dumps(row, ensure_ascii=False))
    print('CHAT_BENCH_ROWS_END')
    if args.json:
        Path(args.json).write_text(json.dumps(dict(bench=now, holdout=held), indent=1) + '\n', encoding='utf-8')
    if args.write:
        base = dict(note='The chat bench ratchet (score_bench.py ratchet): the good share may only rise and no capped count may grow. '
                         'Tighten it with score_bench.py ratchet REPLIES --write after a fix; never loosen it to pass.',
                    ok_share=now['ok_share'], ok=now['ok'], total=now['total'], counts=now['counts'],
                    holdout=None if held is None else dict(ok=held['ok'], total=held['total'], ok_share=held['ok_share'], counts=held['counts']))
        Path(args.baseline).write_text(json.dumps(base, indent=1) + '\n', encoding='utf-8')
        print(f'wrote {args.baseline}')
        return 0
    base = json.loads(Path(args.baseline).read_text(encoding='utf-8'))
    problems, slack = check_ratchet(now, base)
    line = (f"{now['total']} questions, good {now['ok']} ({now['ok_share']:.1%}); " +
            ', '.join(f'{k} {v}' for k, v in now['counts'].items()) +
            ('' if held is None else f"; holdout good {held['ok']} of {held['total']} ({held['ok_share']:.1%})"))
    if args.github:
        print(f'::notice title=Chat question bench ratchet::{line}')
        for p in problems:
            print(f'::error title=Chat question bench ratchet::{p}')
        if slack and not problems:
            print('::notice title=Chat question bench ratchet::better than the baseline (' + '; '.join(slack) +
                  '): tighten tools/bot-voices/eval/bench_ratchet.json with score_bench.py ratchet --write')
    print(line)
    for p in problems:
        print('RATCHET FAIL: ' + p)
    return 1 if problems else 0


def main(argv=None):
    p = argparse.ArgumentParser(description=__doc__.split('\n')[0])
    sub = p.add_subparsers(dest='cmd', required=True)
    b = sub.add_parser('bench')
    b.add_argument('replies')
    b.add_argument('--questions', default=str(QUESTIONS))
    b.add_argument('--json')
    b.add_argument('--md')
    b.add_argument('--github', action='store_true')
    b.add_argument('--worst', type=int, default=30)
    r = sub.add_parser('ratchet')
    r.add_argument('replies')
    r.add_argument('--questions', default=str(QUESTIONS))
    r.add_argument('--holdout', help="the holdout set's replies (bench-holdout.json)")
    r.add_argument('--holdout-questions', default=str(HOLDOUT))
    r.add_argument('--baseline', default=str(RATCHET))
    r.add_argument('--json')
    r.add_argument('--write', action='store_true', help='write the baseline from these replies (after a fix only)')
    r.add_argument('--github', action='store_true')
    r.add_argument('--worst', type=int, default=30)
    li = sub.add_parser('lines')
    li.add_argument('--json')
    li.add_argument('--worst', type=int, default=30)
    args = p.parse_args(argv)
    return bench(args) if args.cmd == 'bench' else ratchet(args) if args.cmd == 'ratchet' else lines_report(args)


if __name__ == '__main__':
    sys.exit(main())
