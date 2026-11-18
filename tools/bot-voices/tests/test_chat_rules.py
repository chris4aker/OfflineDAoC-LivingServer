"""1.2 chat audit: the builder's new checks (chat_rules.py), the question types it infers, and the Python port of the server's
writing styles (voice_style.py, the same cases as UT_ChatAuditVoice.TheStyleCasesMatchThePythonPort).

  python -m unittest discover -s tools/bot-voices/tests -v
"""
import json
import random
import sys
import unittest
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent))

import build_corpus  # noqa: E402
import chat_rules  # noqa: E402
import voice_style  # noqa: E402

VOICES = json.loads((build_corpus.CHAT / 'voices.json').read_text(encoding='utf-8'))['voices']


class Collect:
    def __init__(self, strict=('E3',)):
        self.problems = []
        self.audit = chat_rules.Audit(self.problems, set(strict))


def tags(**extra):
    t = build_corpus.fresh_tags()
    t.update(extra)
    return t


class StyleCases(unittest.TestCase):
    def test_style_cases_match(self):
        cases = json.loads((HERE / 'style_cases.json').read_text(encoding='utf-8'))
        self.assertGreater(len(cases), 10)
        for text, voice, kind, expected in cases:
            self.assertEqual(voice_style.style(text, VOICES[voice], random.Random(1), kind), expected, f'{voice}: {text}')

    def test_no_laugh_on_spam_or_a_laughing_line(self):
        chatty = dict(VOICES['chatty'], emote_chance=1.0)
        for text, kind in (('cheap gold here', 'spam'), ('haha that was close lol', None), ('wts ring, 5g', None)):
            self.assertEqual(voice_style.style(text, chatty, random.Random(2), kind), text)


class Owners(unittest.TestCase):
    def test_owner_of(self):
        self.assertEqual(chat_rules.owner_of('ambient_leveling.lines:3'), 'E3')
        self.assertEqual(chat_rules.owner_of('battleground_lore.lines:3'), 'E3')
        self.assertEqual(chat_rules.owner_of('dunce.lines:3'), 'E4')
        self.assertEqual(chat_rules.owner_of('replies_more.lines:3'), 'E5')
        self.assertEqual(chat_rules.owner_of('lore_albion.lines:3'), 'E6')
        self.assertEqual(chat_rules.owner_of('gen_hib_threads.lines:3'), 'E8')

    def test_strict_owner_fails_others_warn(self):
        c = Collect()
        c.audit('rvr.lines:1', 'x')
        c.audit('dunce.lines:1', 'y')
        c.audit('rvr.lines:2', 'z', unsure=True)
        self.assertEqual(c.problems, ['rvr.lines:1: x'])
        self.assertEqual(sum(len(v) for v in c.audit.warnings.values()), 2)


class Checks(unittest.TestCase):
    def run_text(self, text, where='rvr.lines:1', **extra):
        c = Collect()
        build_corpus.AUDIT = c.audit
        try:
            t = tags(**extra)
            build_corpus.check_text(text, t, where, c.problems)
        finally:
            build_corpus.AUDIT = None
        return c

    def test_new_banned_words(self):
        for text in ('hey all o/', 'my toon is lost', 'love to see it', 'so much dps', 'woke up and chose chaos'):
            self.assertTrue(self.run_text(text, kind='ambient').problems, text)
        self.assertFalse(self.run_text('hey all', kind='ambient').problems)

    def test_swearing_is_fine(self):
        # Owner, 2026-11-18 09:14: "Swearing is fine".
        for text in ('damn it, another train', 'what the hell was that', 'oh crap, adds'):
            self.assertIsNone(build_corpus.banned_word(text), text)
            self.assertFalse(self.run_text(text, kind='ambient').problems, text)

    def test_url_only_in_spam(self):
        self.assertTrue(self.run_text('visit www.gold.com now', kind='ambient').problems)
        self.assertFalse([p for p in self.run_text('visit www.gold.com now', kind='spam').problems if 'url' in p])

    def test_enemy_shapes(self):
        self.assertTrue(self.run_text('{enemy} zerg at the bridge', kind='rvr').problems)
        self.assertTrue(self.run_text('saw a {enemy} stealther', kind='rvr').problems)
        self.assertFalse(self.run_text('saw a {enemy_mod} stealther', kind='rvr').problems)
        self.assertFalse(self.run_text('lots of {enemy} at the mile gate', kind='rvr').problems)

    def test_keep_claims_need_a_flag(self):
        self.assertTrue(self.run_text('caer benowyc is under attack again', kind='rvr').problems)
        self.assertFalse(self.run_text('caer benowyc is under attack again', kind='rvr', needs=['defending']).problems)
        self.assertFalse(self.run_text('caer benowyc still ours?', kind='rvr').problems)

    def test_camp_and_capital_slots(self):
        self.assertTrue(self.run_text('{monsters} at {camp} are the best xp', kind='ambient').problems)
        self.assertFalse(self.run_text('{monsters} at {camp} are the best xp', kind='ambient', needs=['at_camp']).problems)
        self.assertTrue(self.run_text('taking a break in {capital}', kind='ambient').problems)

    def test_time_words(self):
        self.assertTrue(self.run_text('gn all', kind='session').problems)
        self.assertFalse(self.run_text('gn all', kind='session', needs=['!day']).problems)

    def test_promises(self):
        self.assertTrue(self.run_text('omw, hold on', kind='reply', intent='help').problems)
        self.assertFalse(self.run_text('on my way to {destination}, long walk', kind='ambient').problems)

    def test_length_with_real_slots(self):
        self.assertTrue(self.run_text('{title} here, {skill} {recipe}. i make {item} up to lvl {level}. {stock} and more', kind='ambient').problems)

    def test_class_sanity(self):
        self.assertTrue(self.run_text('my minstrel needs new boots', kind='ambient').problems)
        self.assertFalse(self.run_text('my minstrel needs new boots', kind='ambient', realm='alb').problems)

    def test_scene_beats(self):
        self.assertTrue(self.run_text('do not listen to E, red means run', kind='debate').problems)
        self.assertFalse(self.run_text('A healer would help here', kind='debate').problems)
        self.assertTrue(self.run_text('{q} our gl quit tonight, no warning', kind='debate').problems)
        self.assertTrue(self.run_text('{q} serious question, is it cheating?', kind='debate').problems)
        self.assertFalse(self.run_text('{q} is running a buffbot cheating?', kind='debate').problems)

    def test_near_duplicate_scene_beats(self):
        c = Collect(strict=('E8',))
        scenes = [dict(id='s1', beats=[dict(where='gen_alb_threads.lines:1', variants=['housewarming saturday at my place, bring furniture'])]),
                  dict(id='s2', beats=[dict(where='gen_social_threads.lines:9', variants=['housewarming saturday at my place, bring some furniture'])])]
        chat_rules.scene_duplicates_audit(scenes, c.audit)
        self.assertEqual(len(c.problems), 1)


class Invites(unittest.TestCase):
    # CORPUS-C-005: three question types were inferred too widely.
    def test_amounts_are_not_choices(self):
        self.assertNotEqual(build_corpus.infer_invites('anyone sell a ring or two for a 30?', 'ambient'), 'ask_choice')
        self.assertEqual(build_corpus.infer_invites('barrows or avalon isle?', 'ambient'), 'ask_choice')

    def test_who_is_best_is_not_an_opinion(self):
        self.assertNotEqual(build_corpus.infer_invites('who is our best weaponcrafter these days?', 'guild'), 'ask_opinion')
        self.assertEqual(build_corpus.infer_invites('is it worth going to df?', 'ambient'), 'ask_opinion')

    def test_anyone_know_a_camp_is_advice(self):
        self.assertEqual(build_corpus.infer_invites('anyone know a good camp around 20?', 'guild'), 'ask_advice')
        self.assertEqual(build_corpus.infer_invites('anyone know if df is ours?', 'ambient'), 'ask_know')
        self.assertEqual(build_corpus.infer_invites('anyone seen the dragon today?', 'ambient'), 'ask_know')


if __name__ == '__main__':
    unittest.main()
