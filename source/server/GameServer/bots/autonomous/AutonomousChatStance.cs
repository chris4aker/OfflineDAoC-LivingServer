using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace DOL.GS;

/// <summary>
/// One story per speaker for a while (Beta 1.1.3, after RC5). On the owner's PC a level 50 minstrel said in /broadcast within
/// twelve minutes "minst lfg 50, x me", "lfm barrows 47-50", "lf1m dps barrows pst" and "lf1m 47-50 xp grp stonehenge": looking
/// for a group and forming one, and two places for it (Stonehenge Barrows by two of its names). A bot's group posts in any
/// channel (/lfg, /broadcast, /trade, /faction, /say) now keep one stance for <see cref="WindowMs"/>: it looks for a group
/// ("lfg") or forms one ("lfm"), not both, and a group post names one dungeon, by the word the bot first used for it. The
/// bot may change its mind once the window is over. Thread-safe.
/// </summary>
public sealed class AutonomousChatStance
{
    /// <summary>How long a bot keeps its group story: 30 minutes (the owner asked for 20-30).</summary>
    public const long WindowMs = 30 * 60_000;

    /// <summary>The stance every live bot keeps (the tests make their own).</summary>
    public static readonly AutonomousChatStance Shared = new();

    private sealed class Entry
    {
        public string Seek;
        public long SeekAt;
        public string Channel;
        public string Dungeon;
        public string DungeonWord;
        public long DungeonAt;
    }

    private readonly Dictionary<long, Entry> _by = new();

    // Forming a group: "lf1m dps barrows", "lfm 47-50", "need a healer for tc", "lf a tank for keltoi", "rvr grp forming",
    // "camping x if anyone wants to join", "anyone up for spindel?", "any1 lf grp?".
    private static readonly Regex Forming = new(
        @"\blf\d*m\b|\blf ?\d more\b|\bneed (?:a |an |\d )?(?:heal|heals|healer|tank|cc|mezzer|dps|puller|caster|support|cleric|druid|shaman|bard|\d more)\b(?! pots?\b)|" +
        @"\blf (?:a |an )?(?:heal|healer|tank|cc|mezzer|dps|puller|caster|support|cleric|druid|shaman|bard|\d more)\b(?! pots?\b)|\bforming\b|\bgrp up for\b|" +
        @"\bif anyone wants to join\b|\bany1 lf grp\b|\banyone up for\b|\bgrp lf\d*m\b|\broom for \d\b|" +
        // RC6 on the owner's PC: "group at Coruscating Mine needs a healer, pst" and "group has room near Connacht, all classes
        // welcome" from bots in full rvr parties went unchecked.
        @"\bneeds (?:a |an |\d )?(?:heal|heals|healer|tank|cc|mezzer|dps|puller|caster|support|cleric|druid|shaman|bard|\d more)\b(?! pots?\b)|" +
        @"\b(?:gr(?:ou)?p|we) (?:has|have|got) (?:room|a spot|spots|\d spots?)\b|\bspots? (?:open|left)\b|\ball classes welcome\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    // Looking for one: "lfg 50 minst", "lf grp 38 thane", "looking for a grp", "any grps for a 24 ranger?", "lf duo",
    // "any room for a 30 cleric?", "free for grps", "lf rvr grp".
    private static readonly Regex Seeking = new(
        @"\blfg\b|\blf (?:xp |rvr |a )?gr(?:ou)?p\b|\blooking for a gr(?:ou)?p\b|\bany gr(?:ou)?ps (?:for|need)\b|\bany room for\b|\bfree for grps\b|" +
        @"\bwants a grp\b|\blf duo\b|\bwanna duo\b|\bany rvr grps lf\b|" +
        // RC6: "33 minstrel looking for group", "lf anything with xp", "any1 need a 32 theurgist?", "looking for a duo partner".
        @"\blooking for (?:a )?(?:gr(?:ou)?p|duo|xp|any)\w*\b|\blf (?:any|anything|duo)\b|\bany ?(?:1|one|group|grp) need (?:a|an) \d|" +
        @"\blooking, any level\b|\bneed a gr(?:ou)?p\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>"lfm" for a line that forms a group, "lfg" for one that looks for a group, null for anything else. Pure.</summary>
    public static string SeekOf(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        if (Forming.IsMatch(text))
            return "lfm";
        return Seeking.IsMatch(text) ? "lfg" : null;
    }

    /// <summary>The stance a bot took in the window (lfg or lfm), or null.</summary>
    public string SeekOf(long speaker, long now)
    {
        lock (_by)
            return _by.TryGetValue(speaker, out Entry e) && e.Seek != null && Within(e.SeekAt, now) ? e.Seek : null;
    }

    /// <summary>The dungeon a bot's group posts named in the window (its full name), or null.</summary>
    public string DungeonOf(long speaker, long now)
    {
        lock (_by)
            return _by.TryGetValue(speaker, out Entry e) && e.Dungeon != null && Within(e.DungeonAt, now) ? e.Dungeon : null;
    }

    private static bool Within(long at, long now) => now >= at && now - at < WindowMs;

    /// <summary>
    /// Whether a line keeps the speaker's story: a group post of the other stance, or one naming another dungeon than the one
    /// in the window, doesn't. <paramref name="dungeon"/> is the dungeon the line names (full name), or null.
    /// </summary>
    public bool Allows(long speaker, string text, string dungeon, long now)
    {
        string seek = SeekOf(text);
        if (seek == null)
            return true;
        lock (_by)
        {
            if (!_by.TryGetValue(speaker, out Entry e))
                return true;
            if (e.Seek != null && Within(e.SeekAt, now) && e.Seek != seek)
                return false;
            return dungeon == null || e.Dungeon == null || !Within(e.DungeonAt, now) ||
                   string.Equals(e.Dungeon, dungeon, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>A line was said: a group post sets the stance and, with a dungeon, the dungeon and the word for it.</summary>
    public void Note(long speaker, string text, string dungeon, string dungeonWord, long now, string channel = null)
    {
        string seek = SeekOf(text);
        if (seek == null)
            return;
        lock (_by)
        {
            if (!_by.TryGetValue(speaker, out Entry e))
            {
                if (_by.Count > 8192)
                    foreach (long old in _by.Where(pair => !Within(pair.Value.SeekAt, now)).Select(pair => pair.Key).ToList())
                        _by.Remove(old);
                _by[speaker] = e = new Entry();
            }
            e.Seek = seek;
            e.SeekAt = now;
            e.Channel = channel;
            if (dungeon != null)
            {
                if (!string.Equals(e.Dungeon, dungeon, StringComparison.OrdinalIgnoreCase) || !Within(e.DungeonAt, now))
                {
                    e.Dungeon = dungeon;
                    e.DungeonWord = dungeonWord;
                }
                e.DungeonAt = now;
            }
        }
    }

    /// <summary>
    /// The post a bot made in this channel in the window that its state has since outrun, once (the caller may say "nm got a
    /// grp" or "nvm, full" there): an "lfg" from a bot now in a group, an "lfm" from a group no longer recruiting. Null for none.
    /// </summary>
    public (string Seek, long AgeMs)? Outrun(long speaker, string channel, bool inGroup, bool recruiting, long now)
    {
        lock (_by)
        {
            if (!_by.TryGetValue(speaker, out Entry e) || e.Seek == null || !Within(e.SeekAt, now) || e.Channel != channel)
                return null;
            if (e.Seek == "lfg" ? !inGroup : recruiting)
                return null;
            var outrun = (e.Seek, now - e.SeekAt);
            // The story ends here: the next post may take the bot's new state.
            e.Seek = null;
            e.Dungeon = null;
            return outrun;
        }
    }

    /// <summary>
    /// Closes the bot's open group post of this stance ("lfg" or "lfm") once, for a closing said elsewhere (the /lfg market's
    /// "got a grp ty", AutonomousChatMarket): true when such a post was open in the window, and its story then ends, so
    /// <see cref="Outrun"/> doesn't close it a second time. False when it was already closed or the bot moved on.
    /// </summary>
    public bool TryClose(long speaker, string seek, long now)
    {
        lock (_by)
        {
            if (!_by.TryGetValue(speaker, out Entry e) || e.Seek == null || e.Seek != seek || !Within(e.SeekAt, now))
                return false;
            e.Seek = null;
            e.Dungeon = null;
            return true;
        }
    }

    /// <summary>The word a bot used for its dungeon in the window ("barrows" for Stonehenge Barrows), or null.</summary>
    public string DungeonWordOf(long speaker, long now)
    {
        lock (_by)
            return _by.TryGetValue(speaker, out Entry e) && e.Dungeon != null && Within(e.DungeonAt, now) ? e.DungeonWord : null;
    }
}
