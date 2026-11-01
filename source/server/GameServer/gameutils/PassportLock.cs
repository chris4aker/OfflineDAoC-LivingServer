using System;
using System.Collections.Generic;
using DOL.Database;

namespace DOL.GS
{
    /// <summary>
    /// A character is playable in one world at a time. While it visits another world, its rows stay
    /// here with these custom params (written by OfflineDaoc.Passport, see docs/CHARACTER-PASSPORT.md)
    /// and nothing may load, delete or move it.
    /// </summary>
    public static class PassportLock
    {
        public const string AwayKey = "passport.away";
        public const string HostKey = "passport.host";

        /// <summary>The database is read each time, so a lock written after this account logged in still holds.</summary>
        public static bool IsAway(DbCoreCharacter character, out string where)
        {
            return IsAway(DOLDB<DbCoreCharacterXCustomParam>.SelectObjects(DB.Column("DOLCharactersObjectId").IsEqualTo(character.ObjectId)), out where);
        }

        public static bool IsAway(IEnumerable<DbCoreCharacterXCustomParam> customParams, out string where)
        {
            bool away = false;
            where = "another world";

            foreach (DbCoreCharacterXCustomParam param in customParams)
            {
                if (string.Equals(param.KeyName, AwayKey, StringComparison.OrdinalIgnoreCase))
                    away = !string.IsNullOrEmpty(param.Value);
                else if (string.Equals(param.KeyName, HostKey, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(param.Value))
                    where = param.Value;
            }

            return away;
        }
    }
}
