using System;
using System.Collections.Generic;
using System.Linq;
using DOL.Database;

namespace DOL.GS
{
    /// <summary>
    /// What the play test's sweep changed on its guest, saved before it changes it (2026-11-14: the server was stopped,
    /// force-closed by the launcher, while a sweep ran; the end path never ran, the lowered PrivLevel had been saved with
    /// the account, and after the restart the owner's GM account was a plain player: "No such command" for every GM
    /// command until Make Me a GM was turned off and on again).
    ///
    /// The record is one row of AccountXCustomParam (the account's own small saved state, a saved-progress table) keyed
    /// <see cref="Param"/>: the guest's PrivLevel before the sweep (0 when the sweep did not lower it), its character and
    /// that character's realm before the sweep. The sweep's normal end clears it; a server that starts, or an account that
    /// logs in, while no sweep runs and the record is there puts both back (<see cref="RestoreStored"/>). A PrivLevel is
    /// only ever raised back to what the record says the account had, never lowered, and never raised for an account the
    /// sweep did not lower (one that was a player before it): no account gets GM it did not have (co-op audit 2026-11-03
    /// §3, GmAccess.cs).
    /// </summary>
    public static class EpicPlaytestSweepGuestRecord
    {
        private static readonly Logging.Logger Log = Logging.LoggerManager.Create(typeof(EpicPlaytestSweepGuestRecord));

        public const string Param = "LivingServer.EpicSweepGuest";

        public sealed record Entry(string Account, uint HomePrivLevel, string CharacterId, string CharacterName, eRealm HomeRealm);

        /// <summary>The row's value (pure): privlevel=N;realm=N;character=ID;name=NAME.</summary>
        public static string Format(Entry entry) =>
            $"privlevel={entry.HomePrivLevel};realm={(int)entry.HomeRealm};character={entry.CharacterId};name={entry.CharacterName}";

        /// <summary>A row's value read back (pure); null when it can't be read.</summary>
        public static Entry Parse(string account, string value)
        {
            if (string.IsNullOrWhiteSpace(account) || string.IsNullOrWhiteSpace(value)) return null;
            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string part in value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                int at = part.IndexOf('=');
                if (at > 0) fields[part[..at]] = part[(at + 1)..];
            }
            if (!fields.TryGetValue("privlevel", out string level) || !uint.TryParse(level, out uint privLevel)) return null;
            if (!fields.TryGetValue("realm", out string realmText) || !byte.TryParse(realmText, out byte realm) ||
                !Enum.IsDefined(typeof(eRealm), realm)) return null;
            fields.TryGetValue("character", out string id);
            fields.TryGetValue("name", out string name);
            return new Entry(account, privLevel, string.IsNullOrEmpty(id) ? null : id, string.IsNullOrEmpty(name) ? null : name, (eRealm)realm);
        }

        /// <summary>
        /// The account level after a restore (pure): the recorded home level when the sweep lowered the account from a GM
        /// level (home above Player) and the account is below it now; otherwise the level it has. Never lower, and never
        /// GM for an account the record does not say was GM.
        /// </summary>
        public static uint RaisedPrivLevel(uint current, uint home) =>
            home > (uint)ePrivLevel.Player && current < home ? home : current;

        private static WhereClause RowsOf(string account) =>
            DB.Column("Name").IsEqualTo(account).And(DB.Column("KeyName").IsEqualTo(Param));

        public static Entry Read(IObjectDatabase database, string account)
        {
            if (database == null || string.IsNullOrEmpty(account)) return null;
            DbAccountXCustomParam row = database.SelectObjects<DbAccountXCustomParam>(RowsOf(account)).FirstOrDefault();
            return row == null ? null : Parse(row.Name, row.Value);
        }

        /// <summary>Saves the record (one row per account). False when it could not be saved.</summary>
        public static bool Write(IObjectDatabase database, Entry entry)
        {
            try
            {
                IList<DbAccountXCustomParam> rows = database.SelectObjects<DbAccountXCustomParam>(RowsOf(entry.Account));
                DbAccountXCustomParam row = rows.FirstOrDefault();
                bool saved;
                if (row == null)
                    saved = database.AddObject(new DbAccountXCustomParam(entry.Account, Param, Format(entry)));
                else
                {
                    row.Value = Format(entry);
                    saved = database.SaveObject(row);
                    if (rows.Count > 1) database.DeleteObject(rows.Skip(1));
                }
                return saved;
            }
            catch (Exception exception)
            {
                Log.Warn($"EPIC_SWEEP_GUEST_RECORD could not save the record for account {entry.Account}", exception);
                return false;
            }
        }

        /// <summary>Removes the account's record. False when there was none or it could not be removed.</summary>
        public static bool Clear(IObjectDatabase database, string account, DbAccount live = null)
        {
            if (database == null || string.IsNullOrEmpty(account)) return false;
            try
            {
                IList<DbAccountXCustomParam> rows = database.SelectObjects<DbAccountXCustomParam>(RowsOf(account));
                if (live?.CustomParams != null)
                    live.CustomParams = live.CustomParams.Where(p => !string.Equals(p.KeyName, Param, StringComparison.Ordinal)).ToArray();
                return rows.Count > 0 && database.DeleteObject(rows);
            }
            catch (Exception exception)
            {
                Log.Warn($"EPIC_SWEEP_GUEST_RECORD could not clear the record for account {account}", exception);
                return false;
            }
        }

        /// <summary>
        /// Puts back what a sweep that did not end left on its guest, from the saved rows: the account's level (raised only,
        /// <see cref="RaisedPrivLevel"/>) and the character's realm, both saved, then the record is cleared. Nothing while a
        /// sweep runs (its own end restores). <paramref name="live"/>: the account object a login is using, so the restore
        /// is not overwritten by it; with <paramref name="account"/> null, every account's record (server start). Returns
        /// how many records were restored.
        /// </summary>
        public static int RestoreStored(IObjectDatabase database, bool sweepRunning, string why, string account = null, DbAccount live = null)
        {
            if (database == null) return 0;
            account ??= live?.Name;
            IList<DbAccountXCustomParam> rows;
            try
            {
                rows = database.SelectObjects<DbAccountXCustomParam>(account == null
                    ? DB.Column("KeyName").IsEqualTo(Param)
                    : RowsOf(account));
            }
            catch (Exception exception)
            {
                Log.Warn("EPIC_SWEEP_GUEST_RECORD could not read the sweep guest records", exception);
                return 0;
            }
            if (rows.Count == 0) return 0;
            if (sweepRunning)
            {
                Log.Info($"EPIC_SWEEP_GUEST_RESTORE_WAIT why={why} records={rows.Count}: a sweep is running; its end restores");
                return 0;
            }
            int restored = 0;
            foreach (IGrouping<string, DbAccountXCustomParam> byAccount in rows.GroupBy(r => r.Name, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    if (RestoreOne(database, byAccount.First(), why, live))
                        restored++;
                }
                catch (Exception exception)
                {
                    Log.Warn($"EPIC_SWEEP_GUEST_RECORD could not restore account {byAccount.Key}; the record stays for the next start or login", exception);
                }
            }
            return restored;
        }

        private static bool RestoreOne(IObjectDatabase database, DbAccountXCustomParam row, string why, DbAccount live)
        {
            Entry entry = Parse(row.Name, row.Value);
            if (entry == null)
            {
                Log.Warn($"EPIC_SWEEP_GUEST_RESTORED account={row.Name} why={why} unreadable=\"{row.Value}\": record removed, nothing changed");
                Clear(database, row.Name, live);
                return false;
            }
            DbAccount account = live != null && string.Equals(live.Name, entry.Account, StringComparison.OrdinalIgnoreCase)
                ? live
                : database.FindObjectByKey<DbAccount>(entry.Account);
            string level = "-";
            if (account != null)
            {
                uint before = account.PrivLevel, after = RaisedPrivLevel(before, entry.HomePrivLevel);
                level = $"{before}->{after}";
                if (after != before)
                {
                    account.PrivLevel = after;
                    if (!database.SaveObject(account))
                        throw new InvalidOperationException($"account {entry.Account} was not saved");
                }
            }
            string realm = "-";
            if (entry.CharacterId != null && entry.HomeRealm != eRealm.None)
            {
                DbCoreCharacter character = account?.Characters?.FirstOrDefault(c => c?.ObjectId == entry.CharacterId)
                    ?? database.FindObjectByKey<DbCoreCharacter>(entry.CharacterId);
                if (character != null)
                {
                    realm = $"{(eRealm)character.Realm}->{entry.HomeRealm}";
                    if (character.Realm != (int)entry.HomeRealm)
                    {
                        character.Realm = (int)entry.HomeRealm;
                        if (!database.SaveObject(character))
                            throw new InvalidOperationException($"character {entry.CharacterName} was not saved");
                    }
                }
            }
            Clear(database, entry.Account, account);
            Log.Info($"EPIC_SWEEP_GUEST_RESTORED why={why} account={entry.Account} privLevel={level} character=\"{entry.CharacterName}\" " +
                     $"realm={realm}{(account == null ? " (no such account)" : "")}");
            return true;
        }
    }
}
