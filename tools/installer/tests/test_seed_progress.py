"""Tests of seed_progress.py (the update114 install test's seed and check), on a synthetic world.

    python -m unittest discover -s tools/installer/tests -v
"""
import sqlite3
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import seed_progress as sp  # noqa: E402
mtw = sp.mtw


def make_game(root: Path) -> Path:
    """A playable folder with a world holding the tables the seed writes (the server's own column defaults)."""
    game = root / "playable-v0.37"
    (game / "runtime" / "data").mkdir(parents=True)
    (game / "runtime" / "server").mkdir(parents=True)
    (game / "runtime" / "client-opendaoc" / "app").mkdir(parents=True)
    (game / "runtime" / "account.txt").write_text("Account: offline\nPassword: abc\n")
    (game / "runtime" / "server" / "bot-goals.json").write_text("{}")
    (game / "runtime" / "client-opendaoc" / "app" / "game.dll").write_bytes(b"MZ classic client")
    con = sqlite3.connect(game / "runtime" / "data" / "opendaoc.sqlite3.db")
    for ddl in mtw.MINIMAL_SCHEMA.values():
        con.execute(ddl)
    con.execute("INSERT INTO StartupLocation (XPos,YPos,ZPos,Region,RealmID) VALUES (1,2,3,1,1),(4,5,6,100,2),(7,8,9,200,3)")
    cols = ", ".join(f"`{c}` INT NOT NULL DEFAULT 0" for c in sp.CHARACTER_VALUES if c not in ("DOLCharacters_ID", "Name", "AccountName"))
    con.execute(f"CREATE TABLE DOLCharacters (`DOLCharacters_ID` VARCHAR(255) PRIMARY KEY, `Name` TEXT NOT NULL DEFAULT '', "
                f"`AccountName` TEXT NOT NULL DEFAULT '', {cols}, `CreationDate` DATETIME, `LastPlayed` DATETIME, "
                "`LastTimeRowUpdated` DATETIME, `Strength` INT NOT NULL DEFAULT 0)")
    con.execute("CREATE TABLE Inventory (`Inventory_ID` VARCHAR(255) PRIMARY KEY, `OwnerID` TEXT, `ITemplate_Id` TEXT, "
                "`SlotPosition` INT NOT NULL DEFAULT 0, `Count` INT NOT NULL DEFAULT 1, `Condition` INT NOT NULL DEFAULT 0, "
                "`Durability` INT NOT NULL DEFAULT 0, `LastTimeRowUpdated` DATETIME)")
    con.execute("CREATE TABLE ItemTemplate (`Id_nb` TEXT PRIMARY KEY, `Name` TEXT)")
    con.execute("INSERT INTO ItemTemplate VALUES ('rusty_sword', 'Rusty Sword'), ('a_bow', 'Bow')")
    con.execute("CREATE TABLE offline_world_bots (BotId INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT, Realm INT, ClassId INT, "
                "ClassName TEXT, RaceId INT, RaceName TEXT, Gender INT, Level INT, Experience INT, RealmPoints INT, ZoneName TEXT, "
                "Activity TEXT, IsOnline INT, IsAlive INT, LastUpdateUtc TEXT, MoneyCopper INT, InventoryRevision INT, ZoneId INT, "
                "X INT, Y INT, Z INT, RegionId INT, Health INT, Mana INT, Endurance INT, BindRegionId INT, BindX INT, BindY INT, "
                "BindZ INT, CurrentGoal TEXT, ObjectiveProgress TEXT, LastMeaningfulProgressUtc TEXT, IsRetired INT, "
                "SerializedAbilities TEXT DEFAULT '', SerializedSpecs TEXT DEFAULT '', UnspentSpecPoints INT DEFAULT 0)")
    con.execute("CREATE TABLE offline_population_settings (Key TEXT PRIMARY KEY, Value TEXT NOT NULL, Description TEXT)")
    con.execute("INSERT INTO offline_population_settings VALUES ('ActiveTarget', '900', 'bots'), ('PopulationEnabled', 'true', '')")
    con.commit()
    con.close()
    return game


class SeedProgressTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.dir = Path(self.tmp.name)
        self.game = make_game(self.dir)
        self.record = self.dir / "seed.json"
        self.db = sp.world_of(self.game)

    def tearDown(self):
        self.tmp.cleanup()

    def test_a_seeded_game_left_alone_checks_as_kept(self):
        data = sp.seed(self.game, self.record)
        seeded = data["seeded"]
        self.assertEqual(seeded["character"]["Level"], 20)
        self.assertEqual(seeded["character"]["Experience"], mtw.XP_FOR_LEVEL[19])
        self.assertEqual(seeded["items"], [{"OwnerID": sp.CHARACTER_ID, "ITemplate_Id": "a_bow", "SlotPosition": 40, "Count": 1}])
        self.assertEqual(len(data["bots"]), 3)
        self.assertEqual(seeded["settings"], sp.SETTINGS)
        self.assertEqual(seeded["population"], {"ActiveTarget": "123"})
        self.assertEqual(seeded["local_options"], {"MakeMeGM": "False"})
        self.assertEqual(sorted(seeded["files"]), sorted(sp.FILES))
        self.assertEqual(sp.check(self.game, self.record), [])
        self.assertEqual(sp.main(["check", "--game", str(self.game), "--record", str(self.record)]), 0)

    def test_every_kind_of_loss_is_reported(self):
        sp.seed(self.game, self.record)
        con = sqlite3.connect(self.db)
        con.execute("UPDATE DOLCharacters SET Gold=0 WHERE Name=?", (sp.CHARACTER,))
        con.execute("DELETE FROM Inventory")
        con.execute("UPDATE ServerProperty SET Value='2' WHERE `Key`='bot_voices_chat_rate'")
        con.execute("UPDATE offline_population_settings SET Value='900' WHERE Key='ActiveTarget'")
        con.execute("DELETE FROM offline_world_bots WHERE Realm=2")
        con.commit()
        con.close()
        (self.game / "runtime" / "account.txt").write_text("Account: offline\nPassword: changed\n")
        problems = sp.check(self.game, self.record)
        text = "\n".join(problems)
        for expected in ("character Gold", "items:", "settings bot_voices_chat_rate", "population ActiveTarget", "bots Arnulf",
                         "files runtime/account.txt"):
            self.assertIn(expected, text)
        self.assertEqual(sp.main(["check", "--game", str(self.game), "--record", str(self.record)]), 1)

    def test_a_copy_of_the_world_is_checked_without_the_games_files(self):
        sp.seed(self.game, self.record)
        copy = self.dir / "smoke" / "data" / "opendaoc.sqlite3.db"
        copy.parent.mkdir(parents=True)
        copy.write_bytes(self.db.read_bytes())
        (self.game / "runtime" / "account.txt").unlink()
        self.assertEqual(sp.check(self.game, self.record, db=copy), [])

    def test_a_world_with_characters_is_never_seeded(self):
        con = sqlite3.connect(self.db)
        con.execute("INSERT INTO DOLCharacters (DOLCharacters_ID, Name) VALUES ('x', 'Someone')")
        con.commit()
        con.close()
        with self.assertRaises(SystemExit):
            sp.seed(self.game, self.record)
        self.assertFalse(self.record.exists())


if __name__ == "__main__":
    unittest.main()
