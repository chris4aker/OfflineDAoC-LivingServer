using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace DOL.GS
{
    /// <summary>
    /// Quiet twisting (owner 2026-10-30, from a r/daoc report): a Minstrel, Bard or Skald bot twists by restarting a song
    /// every few seconds, and each restart played its 3-second cast music and its start sound to everyone nearby. A bot
    /// restarting a song it started within the last 30 seconds now does so without the cast animation and with the start
    /// effect silent. The first start of a song is still heard, and the songs, timings and buffs are unchanged. Players'
    /// own songs are untouched.
    /// </summary>
    public static class BotQuietSongs
    {
        /// <summary>A song a bot started again within this long is a twist restart.</summary>
        public const int RestartWindowMs = 30_000;

        private static readonly ConditionalWeakTable<GameLiving, Dictionary<int, long>> Started = new();

        /// <summary>Whether a start at <paramref name="now"/> restarts a song last started at <paramref name="lastStart"/>. Pure.</summary>
        public static bool IsRestart(long lastStart, long now) =>
            lastStart > 0 && now >= lastStart && now - lastStart <= RestartWindowMs;

        /// <summary>Bots' beneficial songs only; harmful pulses (a flute mez) and everyone else's spells keep their sounds.</summary>
        public static bool Applies(GameLiving caster, Spell spell) =>
            caster is GameBot && spell is { IsPulsing: true, IsHarmful: false };

        /// <summary>Whether this cast of <paramref name="spell"/> by <paramref name="caster"/> is a twist restart.</summary>
        public static bool IsRestart(GameLiving caster, Spell spell, long now)
        {
            if (!Applies(caster, spell) || !Started.TryGetValue(caster, out Dictionary<int, long> starts))
                return false;
            lock (starts)
                return starts.TryGetValue(spell.ID, out long last) && IsRestart(last, now);
        }

        /// <summary>Records a song's start. Returns whether it was a twist restart (and so starts silently).</summary>
        public static bool MarkStarted(GameLiving caster, Spell spell, long now)
        {
            if (!Applies(caster, spell))
                return false;
            Dictionary<int, long> starts = Started.GetOrCreateValue(caster);
            lock (starts)
            {
                bool restart = starts.TryGetValue(spell.ID, out long last) && IsRestart(last, now);
                starts[spell.ID] = now;
                return restart;
            }
        }
    }
}
