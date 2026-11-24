"""The 1.2.2 chat audit's scorers (tools/bot-voices/eval/chat_scorers.py) and its question bench (player_questions.json).

  python -m unittest discover -s tools/bot-voices/tests -v
"""
import json
import subprocess
import sys
import unittest
from pathlib import Path

HERE = Path(__file__).resolve().parent
EVAL = HERE.parent / 'eval'
sys.path.insert(0, str(EVAL))
sys.path.insert(0, str(HERE.parent))

import chat_scorers as cs  # noqa: E402
import score_bench  # noqa: E402

Q = {'text': 'what lvl is vendo caverns?', 'must_any': [r're:\b20\b.{0,12}\b28\b'], 'wrong_any': ['re:\\bspindelhalla\\b']}


class Matching(unittest.TestCase):
    def test_words_match_whole(self):
        self.assertTrue(cs.matches('vendo', 'try Vendo, its good'))
        self.assertFalse(cs.matches('atla', 'atlantis is a rumour'))
        self.assertTrue(cs.matches("nisse's lair", 'nisses lair for 15'))
        self.assertTrue(cs.matches('2,797,375', '2,797,375 rps for rr8'))

    def test_regex(self):
        self.assertTrue(cs.matches(r're:\b20\b.{0,12}\b28\b', 'mostly 20-28'))
        self.assertFalse(cs.matches(r're:\b20\b.{0,12}\b28\b', 'mostly 30-38'))


class Categories(unittest.TestCase):
    def test_answered(self):
        self.assertEqual(cs.score(Q, 'vendo caverns is about 20-28')[0], 'ANSWERED')

    def test_wrong_wins_over_answered(self):
        self.assertEqual(cs.score(Q, '20-28, or spindelhalla')[0], 'WRONG')

    def test_non_answer(self):
        for reply in ('not sure tbh', 'no idea, ask around', 'depends', 'ya?', '?', 'try /advice', 'good question'):
            self.assertEqual(cs.score(Q, reply)[0], 'NON_ANSWER', reply)

    def test_a_deflection_holds_no_fact(self):
        # #477: "re:sure|ask" passed "not sure, try /advice" (the audit's hand judgments: f046, f371, f467).
        q = {'text': 'hey Siobhan can u help a newbie', 'must_any': [r're:sure|ya|yes|what|need|ask|help']}
        for reply in ('not sure, try /advice', 'Not sure, try /advice.', 'no idea tbh', 'no clue, sry', 'dunno, ask around'):
            self.assertEqual(cs.score(q, reply)[0], 'NON_ANSWER', reply)
        self.assertEqual(cs.score(q, 'sure, what do you need?')[0], 'ANSWERED')
        # A fact beside the deflection still counts.
        self.assertEqual(cs.score(Q, 'not sure, but vendo is about 20-28')[0], 'ANSWERED')

    def test_fluff(self):
        for reply in ('lol', 'heh true', 'nice', 'yeah same', 'good times'):
            self.assertEqual(cs.score(Q, reply)[0], 'FLUFF', reply)

    def test_off_topic(self):
        self.assertEqual(cs.score(Q, 'bring a healer to myrkwood')[0], 'OFF_TOPIC')

    def test_silent_and_stub(self):
        self.assertEqual(cs.score(Q, None)[0], 'SILENT')
        self.assertTrue(cs.ok(dict(Q, silence_ok=True), 'SILENT'))
        self.assertFalse(cs.ok(Q, 'SILENT'))
        self.assertEqual(cs.score(Q, '[world: enemy sightings]')[0], 'STUB')


class Clunky(unittest.TestCase):
    def codes(self, text):
        return {c for c, _ in cs.clunky_issues(text)}

    def test_clean_lines(self):
        for text in ('vendo caverns is about 20-28', 'hit and heal, hit and heal', 'someone to talk to', 'an rvr guild', 'shoot, shoot again',
                     'happy halloween :)', '...i always thought they were afk', 'a unique drop', 'an hour', 'an 8 man group', '2 hours ago'):
            self.assertEqual(self.codes(text), set(), text)

    def test_doubled_word(self):
        self.assertIn('doubled_word', self.codes('go to the the keep'))

    def test_articles(self):
        self.assertIn('a_an', self.codes('get a armorcrafter to make it'))
        self.assertIn('a_an', self.codes('an thane would know'))

    def test_number_agreement(self):
        self.assertIn('number_agreement', self.codes('1 levels to go'))
        self.assertIn('number_agreement', self.codes('i need 3 level.'))

    def test_unfilled_and_markers(self):
        self.assertIn('unfilled_slot', self.codes('hey {player} wanna group'))
        self.assertIn('unfilled_slot', self.codes('null'))

    def test_dangling_and_punctuation(self):
        self.assertIn('dangling_connector', self.codes('try myrkwood and'))
        self.assertIn('dangling_connector', self.codes('bring a group,'))
        self.assertIn('punctuation', self.codes('lost the relic . ugh'))
        self.assertIn('punctuation', self.codes('ok,, sure'))

    def test_repeated_clause(self):
        self.assertIn('repeated_clause', self.codes('bring a group for it, its fine, bring a group for it'))

    def test_tacked_laugh(self):
        self.assertIn('tacked_laugh', self.codes('no clue, sry haha'))
        self.assertIn('tacked_laugh', self.codes('thats a full raid, dont go alone :)'))
        self.assertIn('tacked_laugh', self.codes('whats in the cursed forest? juggernaut and more :P'))
        self.assertNotIn('tacked_laugh', self.codes('try myrkwood, thats where the 20s are :)'))

    def test_own_question(self):
        self.assertIn('own_question', self.codes('who is eraliwyr? the alchemist for the plague'))
        self.assertNotIn('own_question', self.codes('vendo? 20-28'))

    def test_case_mix(self):
        self.assertIn('case_mix', self.codes('The armsman trainer in camelot.'))
        self.assertNotIn('case_mix', self.codes('the armsman trainer in camelot'))
        self.assertNotIn('case_mix', self.codes('The armsman trainer in Camelot.'))

    def test_contradiction_and_tense(self):
        self.assertIn('contradicts_itself', self.codes('yes, its open. no, its not'))
        self.assertIn('tense', self.codes('did you went there'))

    def test_shared_cases(self):
        # tests/clunky_cases.json is also run through the C# port of clunky_issues (UT_ChatStitching), so the server's stitching
        # property test checks what this scorer checks.
        cases = json.loads((HERE / 'clunky_cases.json').read_text(encoding='utf-8'))
        self.assertGreater(len(cases), 40)
        for text, codes in cases:
            self.assertEqual(sorted({c for c, _ in cs.clunky_issues(text)}), codes, text)


class Flags(unittest.TestCase):
    def test_echo(self):
        self.assertTrue(cs.echoes('where should i level at 23?', 'where should i level at 23? myrkwood'))
        self.assertFalse(cs.echoes('where should i level at 23?', 'myrkwood at 23'))

    def test_padded(self):
        self.assertTrue(cs.padded('vendo is 20-28, not sure tho'))
        self.assertFalse(cs.padded('vendo is 20-28'))

    def test_score_flags(self):
        _, flags, _ = cs.score(Q, 'its 20-28, 20-28 is is fine')
        self.assertIn('CLUNKY', flags)


class Bench(unittest.TestCase):
    def test_questions_file_is_up_to_date_and_large_enough(self):
        out = subprocess.run([sys.executable, str(EVAL / 'make_player_questions.py'), '--check'], capture_output=True, text=True)
        self.assertEqual(out.returncode, 0, out.stdout + out.stderr)
        data = json.loads((EVAL / 'player_questions.json').read_text(encoding='utf-8'))
        questions = data['questions']
        self.assertGreaterEqual(len(questions), 300)
        self.assertEqual(len({q['id'] for q in questions}), len(questions))
        for q in questions:
            self.assertTrue(q['must_any'] or q.get('silence_ok'), q['id'])
            self.assertIn(q['kind'], ('question', 'statement', 'followup'), q['id'])
            self.assertIn(q['set'], ('code', 'fresh'), q['id'])
            self.assertIn(q['channel'], ('faction', 'say', 'group', 'tell'))
            self.assertIn(q['realm'], ('alb', 'mid', 'hib'))
        intents = {q['intent'] for q in questions}
        for needed in ('level_where', 'buy_where', 'travel', 'trainer', 'quest', 'epic', 'raid', 'rvr_status', 'crafting', 'items', 'faction',
                       'bot_plans', 'small_talk'):
            self.assertIn(needed, intents)

    def test_level_facts_follow_places_json(self):
        data = json.loads((EVAL / 'player_questions.json').read_text(encoding='utf-8'))
        q = next(q for q in data['questions'] if q['intent'] == 'level_where' and q['realm'] == 'mid' and q['level'] == 24)
        self.assertIn('vendo caverns', q['must_any'])
        self.assertIn('vale of mularn', q['wrong_any'])
        self.assertNotIn('tuscaran glacier', q['must_any'])

    def test_bench_report_scores_a_sample(self):
        import tempfile
        data = json.loads((EVAL / 'player_questions.json').read_text(encoding='utf-8'))
        first = data['questions'][0]
        replies = [dict(id=first['id'], route='live ask_hunt', reply=None, answerers=0)]
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / 'r.json'
            path.write_text(json.dumps(replies), encoding='utf-8')
            out = Path(tmp) / 'o.json'
            import contextlib
            import io
            with contextlib.redirect_stdout(io.StringIO()):
                score_bench.main(['bench', str(path), '--json', str(out)])
            report = json.loads(out.read_text(encoding='utf-8'))
        self.assertEqual(report['total'], 1)
        self.assertEqual(report['categories']['SILENT'], 1)

    def test_route_family(self):
        self.assertEqual(score_bench.route_family('rescue (intent banter -> not sure)'), 'rescue <intent -> not sure>')
        self.assertEqual(score_bench.route_family('fact world'), 'fact world')
        self.assertEqual(score_bench.route_family('knowledge'), 'knowledge')


if __name__ == '__main__':
    unittest.main()


class SideBenches(unittest.TestCase):
    """#477: the per-channel bench, the follow-ups, the lines of the moment and the two-line bench (context_lines.json)."""

    def test_context_file(self):
        ctx = json.loads((EVAL / 'context_lines.json').read_text(encoding='utf-8'))
        self.assertEqual(len(ctx['followups']), 54)
        self.assertEqual(len(ctx['moments']), 40)
        self.assertTrue(ctx['twoline'])
        for side in ('followups', 'moments', 'twoline'):
            ids = [x['id'] for x in ctx[side]]
            self.assertEqual(len(ids), len(set(ids)), side)
            for x in ctx[side]:
                self.assertTrue(x['must_any'] or x.get('silence_ok'), x['id'])
                for fact in x['must_any'] + x.get('wrong_any', []):
                    cs.matches(fact, 'a line')    # every pattern compiles

    def test_moments_are_quiet_where_the_file_says(self):
        import tempfile
        rows = [dict(ch='faction', id='m01', text='im by camp station', reply=None, route='silent', stub=False),
                dict(ch='group', id='m01', text='im by camp station', reply=None, route='silent', stub=False),
                dict(ch='group', id='m33', text='what lvl are you?', reply='32 here', route='fact level', stub=False)]
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / 'm.json'
            path.write_text(json.dumps(rows), encoding='utf-8')
            tallies, scored = score_bench.score_side('moments', path)
        self.assertEqual(tallies['faction']['ok'], 1)
        self.assertEqual(tallies['group']['ok'], 1)
        self.assertEqual(tallies['group']['counts']['SILENT'], 1)

    def test_a_stubbed_reply_without_the_fact_is_a_stub(self):
        cat, _, _ = score_bench.stubbed(('OFF_TOPIC', [], 'none of'), {'stub': True})
        self.assertEqual(cat, 'STUB')
        self.assertEqual(score_bench.stubbed(('ANSWERED', [], 'holds'), {'stub': True})[0], 'ANSWERED')


class Evenings(unittest.TestCase):
    """#477: evening_read.py's seven counts and their ratchet."""

    def test_counts_and_check(self):
        import evening_read as er
        rows = [dict(at=10, ch='say', who='Ros', src='field:say', ex=1, i=0, text='sry ks'),
                dict(at=20, ch='say', who='Zan', src='field:say', ex=2, i=0, text='nice weather'),
                dict(at=30, ch='trade', who='Orm', src='market:reply', ex=3, i=1, text='how much u paying?'),
                dict(at=40, ch='group', who='Per', src='combat:pull', ex=0, i=0, text='pulling lol'),
                dict(at=3700, ch='group', who='Ros', src='group:ambient/chat', ex=4, i=0, text='good group'),
                dict(at=50, ch='faction', who='Fen', src='field:ask', ex=5, i=0, text='any1 up for mithra?'),
                dict(at=55, ch='faction', who='Yor', src='thread:1', ex=5, i=1, text='Fen, x2')]
        for n, r in enumerate(rows):
            r['n'] = n
        now = er.counts({('L24-dungeon-alb', '1', 'silent'): rows})
        self.assertEqual(now['say_stock_share'], 0.5)
        self.assertEqual(now['party_banter_after_60'], 1)
        self.assertEqual(now['laughing_calls_share'], 1.0)
        self.assertEqual(now['name_first_share'], 1.0)
        problems, _ = er.check(now, dict(now, say_stock_share=0.4, party_banter_after_60=2))
        self.assertEqual(len(problems), 2)
        self.assertEqual(er.check(now, now), ([], []))

    def test_baseline_exists(self):
        import evening_read as er
        base = json.loads((EVAL / 'evening_ratchet.json').read_text(encoding='utf-8'))
        self.assertTrue(set(er.COUNTS) <= set(base))


class Ratchet(unittest.TestCase):
    """The 1.2.2 ratchet (score_bench.py ratchet): the good share may only rise, no capped count may grow."""

    BASE = dict(ok_share=0.80, counts={k: 5 for k in score_bench.CAPPED})

    def now(self, share=0.80, **counts):
        c = {k: 5 for k in score_bench.CAPPED}
        c.update(counts)
        return dict(total=100, ok=int(share * 100), ok_share=share, counts=c)

    def test_holds_at_the_baseline(self):
        self.assertEqual(score_bench.check_ratchet(self.now(), self.BASE), ([], []))

    def test_fails_when_a_bad_category_grows(self):
        problems, _ = score_bench.check_ratchet(self.now(WRONG=6), self.BASE)
        self.assertTrue(any('WRONG' in p for p in problems))

    def test_fails_when_the_good_share_falls(self):
        problems, _ = score_bench.check_ratchet(self.now(share=0.79), self.BASE)
        self.assertTrue(any('good share' in p for p in problems))

    def test_reports_slack_to_tighten(self):
        problems, slack = score_bench.check_ratchet(self.now(share=0.85, OFF_TOPIC=2), self.BASE)
        self.assertEqual(problems, [])
        self.assertEqual(len(slack), 2)

    def test_counts_only_bad_silence(self):
        out = dict(total=10, ok=6, silence_ok=2, categories={c: 0 for c in cs.CATEGORIES} | {'SILENT': 3, 'ANSWERED': 4},
                   flags={f: 0 for f in cs.FLAGS}, by_intent={})
        self.assertEqual(score_bench.ratchet_counts(out)['counts']['SILENT'], 1)

    def test_baseline_and_holdout_exist(self):
        base = json.loads((EVAL / 'bench_ratchet.json').read_text(encoding='utf-8'))
        self.assertTrue(set(score_bench.CAPPED) <= set(base['counts']))
        self.assertEqual(base['headline'], 'set:fresh')
        for group in ('set:fresh', 'set:code', 'kind:question', 'kind:statement', 'kind:followup'):
            self.assertIn(group, base['groups'])
        for side in score_bench.SIDES:
            self.assertTrue(base[side], side)
        self.assertIn('companion_say', base['channels'])
        held = json.loads((EVAL / 'player_questions_holdout.json').read_text(encoding='utf-8'))['questions']
        # #477: the realism audit's fresh lines held out (written by someone who did not write the fixes).
        self.assertEqual(len(held), 78)
        self.assertTrue(all(q['id'].startswith('f') and (q['must_any'] or q.get('silence_ok')) and q['set'] == 'fresh' for q in held))
        bench_texts = {q['text'] for q in json.loads((EVAL / 'player_questions.json').read_text(encoding='utf-8'))['questions']}
        self.assertFalse({q['text'] for q in held} & bench_texts, 'the holdout holds no bench question')
