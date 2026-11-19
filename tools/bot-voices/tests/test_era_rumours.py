"""The corpus builder's one era exception (owner, 2026-11-15): Trials of Atlantis and Catacombs as rumours only.

  python -m unittest discover -s tools/bot-voices/tests -v
"""
import importlib.util
import unittest
from pathlib import Path

SPEC = importlib.util.spec_from_file_location('build_corpus', Path(__file__).resolve().parents[1] / 'build_corpus.py')
corpus = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(corpus)


def problems(text, topic, kind='reply'):
    tags = corpus.fresh_tags() | dict(kind=kind, topic=topic, answers=['ask_atlantis'], about=True)
    found = []
    corpus.check_text(text, tags, 'test', found)
    return found


class EraRumourException(unittest.TestCase):
    def test_a_rumour_line_may_name_atlantis_and_catacombs(self):
        for topic in sorted(corpus.ERA_RUMOUR_TOPICS):
            self.assertEqual(problems('sailors call it atlantis. nobody has been', topic), [], topic)
            self.assertEqual(problems('theres catacombs under the city, bricked up', topic), [], topic)

    def test_every_other_line_still_may_not(self):
        for topic in ('general', 'lore', 'tale_atlantis', 'rumour', 'named_up', 'rumour_atlantis_more'):
            self.assertTrue(problems('sailors call it atlantis', topic), topic)
            self.assertTrue(problems('the catacombs under the city', topic), topic)

    def test_the_rest_of_the_expansion_stays_banned_in_a_rumour(self):
        for text in ('atlantis has artifacts they say', 'my uncle has master levels from atlantis', 'atlantis gives ml1',
                     'heard toa is coming', 'a heretic came up from the catacombs', 'the shar know the way to atlantis'):
            self.assertTrue(problems(text, 'rumour_atlantis'), text)

    def test_no_rumour_line_claims_a_trip_there(self):
        for text in ('i went to atlantis last week', 'we sailed out to atlantis', 'been to atlantis twice',
                     'lets go find it', 'who wants to sail out there', 'my guild went into the catacombs'):
            self.assertTrue(problems(text, 'rumour_atlantis'), text)
        self.assertEqual(problems('nobody has sailed there and come back', 'rumour_atlantis'), [])

    def test_banned_word_is_pure(self):
        self.assertIsNone(corpus.banned_word('atlantis, or so they say', 'rumour_atlantis'))
        self.assertEqual(corpus.banned_word('atlantis, or so they say', 'general'), 'atlantis')
        self.assertEqual(corpus.banned_word('atlantis artifact', 'rumour_atlantis'), 'artifact')


if __name__ == '__main__':
    unittest.main()
