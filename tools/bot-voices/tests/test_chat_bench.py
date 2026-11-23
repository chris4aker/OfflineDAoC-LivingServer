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
            self.assertTrue(q['must_any'], q['id'])
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
