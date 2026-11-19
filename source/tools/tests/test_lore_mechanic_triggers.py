"""Tests of tools/lore/mechanic_trigger_coverage.py: every mechanic trigger a lore file uses is announced by its boss's script.

    python -m unittest discover -s source/tools/tests -v
"""
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[3] / 'tools' / 'lore'))
import mechanic_trigger_coverage as cov  # noqa: E402


class MechanicTriggerCoverageTests(unittest.TestCase):
    def test_the_engine_list_is_read(self):
        triggers = cov.mechanic_triggers()
        for name in ('teleport', 'throw', 'jail', 'pull', 'barrier', 'glare', 'stare', 'stun', 'stomp', 'breath',
                     'messengers', 'retreat', 'safeSpots', 'berserk', 'scream', 'healed', 'twinHeal'):
            self.assertIn(name, triggers)

    def test_a_script_fires_what_it_announces(self):
        text = '''
            EncounterDisplacement.Move(p, r, x, y, z, h, DisplacementKind.Jail, Body);
            EncounterSignals.Raise(Body, EncounterSignals.Glare, t, 1000, true, 6000);
            EncounterSignals.Note(Body, EncounterSignals.Berserk);
        '''
        self.assertEqual(cov.fired_by(text), {'jail', 'teleport', 'glare', 'stare', 'berserk'})
        self.assertEqual(cov.fired_by('DragonEncounterThrow.Throw(Body, aggro);'), {'throw', 'teleport'})
        self.assertEqual(cov.fired_by('BroadcastMessage("he teleports you");'), set())

    def test_a_class_is_read_on_its_own(self):
        parts = cov.class_parts('''
    public class Funus : GameEpicBoss
    {
        void Hit() { EncounterSignals.Note(this, EncounterSignals.Healed); }
    }
    public class Fames : GameEpicBoss
    {
    }''')
        self.assertEqual(cov.fired_by(parts['Funus']), {'healed'})
        self.assertEqual(cov.fired_by(parts['Fames']), set())

    def test_every_mechanic_trigger_in_the_lore_files_fires(self):
        rows = cov.build()
        self.assertTrue(rows, 'the lore files use mechanic triggers (teleport, breath, glare, berserk)')
        gaps = [f"{r['file']}: {r['trigger']}" for r in rows if not r['fires'] and not r['kept']]
        self.assertEqual(gaps, [])


if __name__ == '__main__':
    unittest.main()
