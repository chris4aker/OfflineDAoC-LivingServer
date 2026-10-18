"""set_bot_guilds.py --purge on a small temporary database. No real install is read or written.

    python tools/claude-version/tests/test_set_bot_guilds.py
"""
import contextlib
import hashlib
import io
import sqlite3
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock

sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import set_bot_guilds as tool  # noqa: E402

# The columns the purge reads or writes, declared as the server declares them (COLLATE NOCASE text).
SCHEMA = """
CREATE TABLE `ServerProperty` (`Category` TEXT NOT NULL DEFAULT '' COLLATE NOCASE, `Key` VARCHAR(255) NOT NULL DEFAULT '' COLLATE NOCASE,
  `Description` TEXT NOT NULL DEFAULT '' COLLATE NOCASE, `DefaultValue` TEXT NOT NULL DEFAULT '' COLLATE NOCASE,
  `Value` TEXT NOT NULL DEFAULT '' COLLATE NOCASE, `LastTimeRowUpdated` DATETIME NOT NULL DEFAULT '2000-01-01 00:00:00',
  `ServerProperty_ID` VARCHAR(255) DEFAULT NULL COLLATE NOCASE, PRIMARY KEY (`Key`));
CREATE TABLE `Guild` (`GuildID` VARCHAR(255) DEFAULT NULL COLLATE NOCASE, `GuildName` VARCHAR(255) DEFAULT NULL COLLATE NOCASE,
  `Realm` UNSIGNED TINYINT(3) NOT NULL DEFAULT 0, `AllianceID` VARCHAR(255) DEFAULT NULL COLLATE NOCASE,
  `Dues` TINYINT(1) NOT NULL DEFAULT 0, `Bank` DOUBLE NOT NULL DEFAULT 0, `DuesPercent` BIGINT(20) NOT NULL DEFAULT 0,
  `IsStartingGuild` TINYINT(1) NOT NULL DEFAULT 0, `Guild_ID` VARCHAR(255) NOT NULL DEFAULT '' COLLATE NOCASE, PRIMARY KEY (`Guild_ID`));
CREATE TABLE `GuildRank` (`GuildID` VARCHAR(255) DEFAULT NULL COLLATE NOCASE, `Title` TEXT DEFAULT NULL COLLATE NOCASE,
  `RankLevel` UNSIGNED TINYINT(3) NOT NULL DEFAULT 0, `AcHear` TINYINT(1) NOT NULL DEFAULT 0,
  `GuildRank_ID` VARCHAR(255) NOT NULL DEFAULT '' COLLATE NOCASE, PRIMARY KEY (`GuildRank_ID`));
CREATE TABLE `GuildAlliance` (`AllianceName` TEXT DEFAULT NULL COLLATE NOCASE, `Motd` TEXT DEFAULT NULL COLLATE NOCASE,
  `LeaderGuildID` VARCHAR(255) DEFAULT NULL COLLATE NOCASE, `GuildAlliance_ID` VARCHAR(255) NOT NULL DEFAULT '' COLLATE NOCASE,
  PRIMARY KEY (`GuildAlliance_ID`));
CREATE TABLE `Keep` (`KeepID` INT(11) NOT NULL DEFAULT 0, `Name` TEXT NOT NULL DEFAULT '' COLLATE NOCASE,
  `Realm` UNSIGNED TINYINT(3) NOT NULL DEFAULT 0, `Level` UNSIGNED TINYINT(3) NOT NULL DEFAULT 0,
  `ClaimedGuildName` TEXT DEFAULT NULL COLLATE NOCASE, `OriginalRealm` INT(11) NOT NULL DEFAULT 0,
  `Keep_ID` VARCHAR(255) NOT NULL DEFAULT '' COLLATE NOCASE, PRIMARY KEY (`Keep_ID`));
CREATE TABLE `DOLCharacters` (`AccountName` VARCHAR(255) NOT NULL DEFAULT '' COLLATE NOCASE, `Name` VARCHAR(255) NOT NULL DEFAULT '' COLLATE NOCASE,
  `GuildID` VARCHAR(255) DEFAULT NULL COLLATE NOCASE, `Realm` INT(11) NOT NULL DEFAULT 0, `GuildRank` UNSIGNED SMALLINT(5) NOT NULL DEFAULT 0,
  `GuildNote` TEXT DEFAULT NULL COLLATE NOCASE, `DOLCharacters_ID` VARCHAR(255) NOT NULL DEFAULT '' COLLATE NOCASE,
  PRIMARY KEY (`DOLCharacters_ID`));
CREATE TABLE `DBHouse` (`HouseNumber` INT(11) NOT NULL DEFAULT 0, `GuildName` TEXT DEFAULT NULL COLLATE NOCASE,
  `GuildHouse` TINYINT(1) NOT NULL DEFAULT 0, `DBHouse_ID` VARCHAR(255) NOT NULL DEFAULT '' COLLATE NOCASE, PRIMARY KEY (`DBHouse_ID`));
"""
BOT_TABLES = """
CREATE TABLE `offline_bot_guilds` (`GuildId` VARCHAR(64) NOT NULL DEFAULT '' COLLATE NOCASE, `Realm` INT(11) NOT NULL DEFAULT 0,
  `Charter` VARCHAR(32) NOT NULL DEFAULT '' COLLATE NOCASE, `Ordinal` INT(11) NOT NULL DEFAULT 0,
  `FounderBotId` BIGINT(20) NOT NULL DEFAULT 0, `CreatedUtc` TEXT NOT NULL DEFAULT '' COLLATE NOCASE,
  `offline_bot_guilds_ID` VARCHAR(255) NOT NULL DEFAULT '' COLLATE NOCASE, PRIMARY KEY (`offline_bot_guilds_ID`));
CREATE TABLE `offline_bot_guild_members` (`BotId` BIGINT(20) NOT NULL DEFAULT 0, `GuildId` VARCHAR(64) NOT NULL DEFAULT '' COLLATE NOCASE,
  `RankLevel` INT(11) NOT NULL DEFAULT 0, `JoinedUtc` TEXT NOT NULL DEFAULT '' COLLATE NOCASE,
  `offline_bot_guild_members_ID` VARCHAR(255) DEFAULT NULL COLLATE NOCASE, PRIMARY KEY (`BotId`));
"""

ALB, MID, HIB = 1, 2, 3
GUILDS = [  # GuildID, name, realm, alliance, starting
    ('start-alb', 'Clan Cotswold', ALB, None, 1),
    ('start-mid', 'Mularn Protectors', MID, None, 1),
    ('bot-rvr', 'Order of the Oaken Shield', ALB, 'ally-bot', 0),       # leads the bot alliance, claims a keep
    ('bot-raid', 'Knights of the Golden Chalice', ALB, 'ally-bot', 0),
    ('bot-hib', 'Corpse Run Club', HIB, '', 0),                          # claims a tower; Hibernia has no starting guild
    ('bot-mid', 'Uppland Kin', MID, 'ally-player', 0),                   # in the player's alliance
    ('player-alb', 'My Own Guild', ALB, 'ally-bot', 0),                  # the player's guild, allied with bot guilds
    ('player-mid', 'Frost Friends', MID, 'ally-player', 0),              # the player's guild, leads its own alliance
]
BOT_GUILDS = [('bot-rvr', ALB, 'Rvr'), ('bot-raid', ALB, 'Raiding'), ('bot-hib', HIB, 'Social'), ('bot-mid', MID, 'Leveling'),
              ('bot-gone', MID, 'Social')]   # a leftover mark whose guild row is already gone
KEEPS = [  # KeepID, name, realm, level, claim
    (50, 'Caer Benowyc', ALB, 5, 'Order of the Oaken Shield'),
    (51, 'Dun Crauchon Tower', HIB, 5, 'Corpse Run Club'),
    (52, 'Bledmeer Faste', MID, 5, 'Frost Friends'),
    (53, 'Caer Sursbrooke', ALB, 5, 'order of the oaken shield'),   # not the exact name: the server would not match it either
    (54, 'Dun Ailinne', HIB, 4, ''),
]
CHARACTERS = [  # id, name, realm, guild, rank, note
    ('c1', 'Claudecheck', ALB, 'bot-rvr', 8, 'my note'),
    ('c2', 'Hibbie', HIB, 'bot-hib', 3, 'officer'),
    ('c3', 'Midder', MID, 'player-mid', 0, 'leader'),
    ('c4', 'Newbie', ALB, 'start-alb', 8, ''),
]
MEMBERS = [(1, 'bot-rvr'), (2, 'bot-raid'), (3, 'player-alb'), (4, 'bot-gone'), (5, 'BOT-MID')]


def build(path, bot_tables=True, starting_guild='True'):
    con = sqlite3.connect(path)
    con.executescript(SCHEMA + (BOT_TABLES if bot_tables else ''))
    for key, value in (('bot_guilds_enabled', 'True'), ('starting_keep_level', '4'), ('starting_guild', starting_guild)):
        con.execute('INSERT INTO ServerProperty (Category, `Key`, Value, ServerProperty_ID) VALUES (?,?,?,?)', ('x', key, value, key))
    for gid, name, realm, alliance, starting in GUILDS:
        con.execute('INSERT INTO Guild (GuildID, GuildName, Realm, AllianceID, IsStartingGuild, Bank, Guild_ID) VALUES (?,?,?,?,?,?,?)',
                    (gid, name, realm, alliance, starting, 1234.0, 'row-' + gid))
        for level in range(10):
            con.execute('INSERT INTO GuildRank (GuildID, Title, RankLevel, GuildRank_ID) VALUES (?,?,?,?)',
                        (gid, f'Rank {level}', level, f'{gid}-{level}'))
    con.execute("INSERT INTO GuildAlliance VALUES ('Order of the Oaken Shield', '', 'bot-rvr', 'ally-bot')")
    con.execute("INSERT INTO GuildAlliance VALUES ('Frost Friends', '', 'player-mid', 'ally-player')")
    for keep_id, name, realm, level, claim in KEEPS:
        con.execute('INSERT INTO Keep (KeepID, Name, Realm, Level, ClaimedGuildName, OriginalRealm, Keep_ID) VALUES (?,?,?,?,?,?,?)',
                    (keep_id, name, realm, level, claim, realm, str(keep_id)))
    for cid, name, realm, guild, rank, note in CHARACTERS:
        con.execute('INSERT INTO DOLCharacters (AccountName, Name, Realm, GuildID, GuildRank, GuildNote, DOLCharacters_ID) '
                    'VALUES (?,?,?,?,?,?,?)', ('me', name, realm, guild, rank, note, cid))
    con.execute("INSERT INTO DBHouse VALUES (7, 'Order of the Oaken Shield', 1, 'h7')")
    if bot_tables:
        for ordinal, (gid, realm, charter) in enumerate(BOT_GUILDS):
            con.execute('INSERT INTO offline_bot_guilds (GuildId, Realm, Charter, Ordinal, offline_bot_guilds_ID) VALUES (?,?,?,?,?)',
                        (gid, realm, charter, ordinal, 'mark-' + gid))
        for bot, gid in MEMBERS:
            con.execute('INSERT INTO offline_bot_guild_members (BotId, GuildId, RankLevel) VALUES (?,?,9)', (bot, gid))
    con.commit()
    con.close()


def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


class PurgeTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.db = Path(self.temp.name) / 'runtime/data/opendaoc.sqlite3.db'
        self.db.parent.mkdir(parents=True)
        self.backups = Path(self.temp.name) / 'runtime/server/deployment-backups'
        # On Windows the tool also looks for a running server process; the test must not depend on what runs.
        patcher = mock.patch.object(tool.subprocess, 'check_output', return_value='')
        patcher.start()
        self.addCleanup(patcher.stop)

    def tearDown(self):
        self.temp.cleanup()

    def run_tool(self, *args):
        out = io.StringIO()
        with mock.patch.object(sys, 'argv', ['set_bot_guilds.py', *args, '--db', str(self.db)]), contextlib.redirect_stdout(out):
            tool.main()
        return out.getvalue()

    def query(self, sql, *params):
        with contextlib.closing(sqlite3.connect(self.db)) as con:
            return con.execute(sql, params).fetchall()

    def test_dry_run_changes_nothing_and_shows_the_counts(self):
        build(self.db)
        before = digest(self.db)
        out = self.run_tool('--purge')
        self.assertEqual(digest(self.db), before)
        self.assertFalse(self.backups.exists())
        for line in ('Bot guilds to delete: 4 (and 1 leftover', 'Guild ranks to delete: 40', 'Bot memberships to delete: 4',
                     'Keeps to release (claim removed, level 4; the realm keeps them): 2', 'Bot guild alliances to end: 1',
                     'your guild "My Own Guild" leaves', 'Characters to move out of a bot guild: 2',
                     'Claudecheck -> Clan Cotswold (rank 8)', 'Hibbie -> no guild', 'house 7 names bot guild', 'Dry run only'):
            self.assertIn(line, out)

    def test_apply_removes_bot_guilds_and_keeps_the_players(self):
        build(self.db)
        out = self.run_tool('--purge', '--apply')
        self.assertIn('Applied. Backup:', out)

        self.assertEqual(self.query("SELECT Value FROM ServerProperty WHERE `Key`='bot_guilds_enabled'"), [('False',)])
        self.assertEqual(self.query('SELECT COUNT(*) FROM offline_bot_guilds'), [(0,)])
        self.assertEqual(sorted(self.query('SELECT BotId, GuildId FROM offline_bot_guild_members')), [(3, 'player-alb')],
                         'A bot the player invited into their own guild stays.')
        self.assertEqual(sorted(self.query('SELECT GuildID, AllianceID FROM Guild')),
                         [('player-alb', ''), ('player-mid', 'ally-player'), ('start-alb', None), ('start-mid', None)])
        self.assertEqual(sorted(self.query('SELECT DISTINCT GuildID FROM GuildRank')),
                         [('player-alb',), ('player-mid',), ('start-alb',), ('start-mid',)])
        self.assertEqual(self.query('SELECT COUNT(*) FROM GuildRank'), [(40,)])
        self.assertEqual(self.query('SELECT GuildAlliance_ID, LeaderGuildID FROM GuildAlliance'), [('ally-player', 'player-mid')])
        self.assertEqual(self.query('SELECT KeepID, Realm, Level, ClaimedGuildName FROM Keep ORDER BY KeepID'), [
            (50, ALB, 4, ''), (51, HIB, 4, ''),                  # released; the realm that took them keeps them
            (52, MID, 5, 'Frost Friends'),                         # the player's claim stays
            (53, ALB, 5, 'order of the oaken shield'), (54, HIB, 4, '')])
        self.assertEqual(self.query('SELECT DOLCharacters_ID, GuildID, GuildRank, GuildNote FROM DOLCharacters ORDER BY 1'), [
            ('c1', 'start-alb', 8, ''), ('c2', '', 3, ''), ('c3', 'player-mid', 0, 'leader'), ('c4', 'start-alb', 8, '')])
        self.assertEqual(self.query('SELECT HouseNumber, GuildName FROM DBHouse'), [(7, 'Order of the Oaken Shield')])
        self.assertEqual(self.query('PRAGMA quick_check'), [('ok',)])
        for line in ('keeps released             2', 'alliances ended            1', 'guilds left an alliance    1',
                     'characters moved           2', 'guild ranks deleted        40', 'guilds deleted             4',
                     'memberships deleted        4', 'bot guild marks deleted    5'):
            self.assertIn(line, out)

        backups = list(self.backups.iterdir())
        self.assertEqual(len(backups), 1)
        with contextlib.closing(sqlite3.connect(backups[0] / 'opendaoc.sqlite3.db')) as copy:
            self.assertEqual(copy.execute('SELECT COUNT(*) FROM offline_bot_guilds').fetchone(), (5,), 'The backup is the old world.')

    def test_running_again_changes_nothing(self):
        build(self.db)
        self.run_tool('--purge', '--apply')
        after = digest(self.db)
        out = self.run_tool('--purge', '--apply')
        self.assertIn('Nothing to remove', out)
        self.assertEqual(digest(self.db), after)
        self.assertEqual(len(list(self.backups.iterdir())), 1, 'No second backup.')

    def test_refuses_while_the_database_is_open(self):
        build(self.db)
        server = sqlite3.connect(self.db)   # a running server: WAL mode with unsaved pages
        try:
            server.execute('PRAGMA journal_mode=WAL')
            server.execute('PRAGMA wal_autocheckpoint=0')
            server.execute("UPDATE Guild SET Bank=99 WHERE GuildID='player-alb'")
            server.commit()
            self.assertGreater(Path(str(self.db) + '-wal').stat().st_size, 0)
            with self.assertRaisesRegex(RuntimeError, 'not empty'):
                self.run_tool('--purge', '--apply')
            self.assertEqual(server.execute('SELECT COUNT(*) FROM offline_bot_guilds').fetchone(), (5,))
            self.assertFalse(self.backups.exists())
        finally:
            server.close()

    def test_starting_guild_off_leaves_characters_guildless(self):
        build(self.db, starting_guild='False')
        self.run_tool('--purge', '--apply')
        self.assertEqual(self.query("SELECT GuildID, GuildRank FROM DOLCharacters WHERE DOLCharacters_ID='c1'"), [('', 8)])

    def test_a_database_without_bot_guild_tables_only_gets_the_switch(self):
        build(self.db, bot_tables=False)
        out = self.run_tool('--purge', '--apply')
        self.assertIn('Bot guilds to delete: 0', out)
        self.assertEqual(self.query("SELECT Value FROM ServerProperty WHERE `Key`='bot_guilds_enabled'"), [('False',)])
        self.assertEqual(self.query('SELECT COUNT(*) FROM Guild'), [(len(GUILDS),)])
        self.assertEqual(self.query('SELECT COUNT(*) FROM GuildAlliance'), [(2,)])

    def test_a_starting_guild_marked_as_a_bot_guild_is_refused(self):
        build(self.db)
        with contextlib.closing(sqlite3.connect(self.db)) as con:
            con.execute("INSERT INTO offline_bot_guilds (GuildId, offline_bot_guilds_ID) VALUES ('start-alb', 'odd')")
            con.commit()
        before = digest(self.db)
        with self.assertRaisesRegex(RuntimeError, 'starting guild'):
            self.run_tool('--purge', '--apply')
        self.assertEqual(digest(self.db), before)

    def test_purge_takes_no_other_switch(self):
        build(self.db)
        with self.assertRaisesRegex(ValueError, 'only --apply'):
            self.run_tool('--purge', '--claims', 'off')


if __name__ == '__main__':
    unittest.main()
