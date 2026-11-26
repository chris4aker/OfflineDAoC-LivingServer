using System;
using System.Linq;
using DAoCLivingServer.Shared;

namespace DOL.GS
{
    /// <summary>
    /// Owner rule 2026-11-17: "the custom class, that should never be played by bots". The pure part of retiring the
    /// gamebots that are already Sluaghbinders: which records the population may log in, the start-up log line and the
    /// launcher's Activity text. Nothing here deletes a character or an item, or sets IsRetired (the launcher's delete
    /// request); a world that wants them back only needs a build without the rule.
    /// </summary>
    public static class CustomClassBotRetirement
    {
        /// <summary>The roster without the custom class (the same array when there is none, as on almost every world).</summary>
        public static OfflineWorldBotRecord[] LoginRoster(OfflineWorldBotRecord[] roster)
        {
            if (roster == null)
                return Array.Empty<OfflineWorldBotRecord>();
            return roster.All(record => BotCustomClassRule.BotsMayPlay(record.ClassId))
                ? roster
                : roster.Where(record => BotCustomClassRule.BotsMayPlay(record.ClassId)).ToArray();
        }

        /// <summary>The kept custom-class gamebots of a (non-retired) roster.</summary>
        public static OfflineWorldBotRecord[] Kept(OfflineWorldBotRecord[] roster) =>
            roster?.Where(record => record != null && !record.IsRetired && !BotCustomClassRule.BotsMayPlay(record.ClassId))
                .ToArray() ?? Array.Empty<OfflineWorldBotRecord>();

        public static string LogLine(OfflineWorldBotRecord record) =>
            $"{BotCustomClassRule.RetiredLogTag} bot=\"{record.Name}\" id={record.BotId} class={BotCustomClassRule.CustomClassName} " +
            $"level={record.Level} realm={RealmName(record.Realm)}: kept with its items, never logged in (bots never play the custom class)";

        /// <summary>Sets the launcher's Activity text; true when the record changed (and needs saving).</summary>
        public static bool MarkActivity(OfflineWorldBotRecord record)
        {
            if (record == null || (record.Activity == BotCustomClassRule.RetiredActivity && !record.IsOnline))
                return false;
            record.Activity = BotCustomClassRule.RetiredActivity;
            record.IsOnline = false;
            record.Dirty = true;
            return true;
        }

        private static string RealmName(int realm) => realm switch
        {
            1 => "Albion",
            2 => "Midgard",
            3 => "Hibernia",
            _ => realm.ToString(),
        };
    }
}
