using System;
using System.Collections.Generic;
using System.Linq;
using DOL.Database;

namespace DOL.GS
{
    /// <summary>
    /// The names a hired companion may take (written for Living Server; True 1.65 used its offline_bot_names table,
    /// BotNameService, which Living Server does not have). A name is free when no player character, no gamebot row and
    /// no hire in progress wears it, ignoring case. A claim lasts from the check until the companion's own row is
    /// written, so two hires at once can't take one name.
    /// </summary>
    public static class CompanionNames165
    {
        private static readonly object Gate = new();
        private static readonly HashSet<string> Claimed = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Tests: another lookup than the database (a name -> taken answer).</summary>
        public static Func<string, bool> LookupForTests { get; set; }

        public static bool IsTaken(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return true;
            lock (Gate)
                if (Claimed.Contains(name))
                    return true;
            if (LookupForTests != null)
                return LookupForTests(name);
            if (AutonomousBotRegistry.TryGetByName(name, out _))
                return true;
            IObjectDatabase database = GameServer.Database;
            if (database == null)
                return false;
            // SQLite compares = case-sensitively, so both spellings a player could have used are checked
            string[] spellings = [name, name.ToLowerInvariant(), char.ToUpperInvariant(name[0]) + name[1..].ToLowerInvariant()];
            return database.SelectObjects<DbCoreCharacter>(DB.Column("Name").IsIn(spellings)).Count > 0 ||
                   database.SelectObjects<OfflineWorldBotRecord>(DB.Column("Name").IsIn(spellings)).Count > 0;
        }

        /// <summary>Claims a free name for a hire in progress; false when it is taken.</summary>
        public static bool TryClaim(string name)
        {
            if (IsTaken(name))
                return false;
            lock (Gate)
                return Claimed.Add(name);
        }

        public static void Release(string name)
        {
            if (string.IsNullOrEmpty(name))
                return;
            lock (Gate)
                Claimed.Remove(name);
        }

        /// <summary>A realm-styled name nobody wears (the gamebots' own generator), or null after a few tries.</summary>
        public static string Suggest(eRealm realm, eGender gender, Random random = null)
        {
            random ??= Random.Shared;
            for (int attempt = 0; attempt < 8; attempt++)
            {
                try
                {
                    string name = AutonomousBotIdentityGenerator.Generate(realm, gender, null, random).Name;
                    if (!string.IsNullOrEmpty(name) && name.Length is >= 3 and <= 20 && name.All(char.IsAsciiLetter) && !IsTaken(name))
                        return name;
                }
                catch (ArgumentException)
                {
                    return null;
                }
            }
            return null;
        }
    }
}
