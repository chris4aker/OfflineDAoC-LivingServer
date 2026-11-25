using System;
using System.Collections.Generic;
using System.Linq;

namespace DOL.GS
{
    /// <summary>
    /// #525: siege engines that gamebots set up on the spot (no kit carried; owner 2026-10-07, "skip the shopping
    /// trips") are now built in sight, and each siege has a finite stock of them.
    /// - Build (owner 2026-11-24): the operator stands at the verified spot and builds; players see the bot working and
    ///   the engine appears only when the build is done. Being attacked, killed or pulled away interrupts the build,
    ///   which then starts again from nothing. A destroyed engine is replaced only by a new build.
    /// - Build time (owner 2026-11-24): half the period build time, one multiplier (<see cref="BuildTimeMultiplier"/>).
    /// - Stock: a per-siege count of synthetic engines sized to the army at the keep (the "finite inventory").
    /// An engine from a carried kit is set up as before (a player's /use of a kit is not timed here) and does not use
    /// the stock. Applies to autonomous gamebots only; companions and players never come here.
    /// </summary>
    public static class AutonomousSiegeBuild
    {
        /// <summary>The owner's tuning knob: build time = period build time x this.</summary>
        public const double BuildTimeMultiplier = 0.5;

        /// <summary>
        /// Period build time per engine, in seconds. No period figure was found (reference.md 5.2 says siegecraft made
        /// engines from wood and metal but gives no time; the 1.x patch notes and the guides found give none either), so
        /// these are chosen: a ram a minute, a ballista a little less, a catapult a minute and a half, a trebuchet two.
        /// Owner check listed on #525.
        /// </summary>
        public static int PeriodBuildSeconds(BotSiegeKind kind) => kind switch
        {
            BotSiegeKind.Ram => 60,
            BotSiegeKind.Ballista => 40,
            BotSiegeKind.Catapult => 90,
            BotSiegeKind.Trebuchet => 120,
            _ => 60
        };

        public static long BuildMilliseconds(BotSiegeKind kind) => (long)Math.Round(PeriodBuildSeconds(kind) * 1000 * BuildTimeMultiplier);

        /// <summary>A build is lost if the builder did not work on it for this long (it died, fought or was pulled off).
        /// Bots think at most a few seconds apart, so this only catches a real break.</summary>
        public const long BuildGapMilliseconds = 15_000;

        /// <summary>The builder works an emote this often, so players nearby see the build.</summary>
        public const long BuildEmoteMilliseconds = 5_000;

        /// <summary>Why a running build stops, or null if it goes on. Attacked counts any enemy hit since the build began
        /// (a builder killed was attacked first).</summary>
        public static string Interruption(long started, long lastWorked, long now, long lastAttackedByEnemy, bool alive, bool sameSpot, bool sameKind)
        {
            if (started == 0) return null;
            if (!alive) return "killed";
            if (lastAttackedByEnemy >= started) return "attacked";
            if (now - lastWorked > BuildGapMilliseconds) return "left";
            if (!sameSpot || !sameKind) return "moved";
            return null;
        }

        public static bool Complete(BotSiegeKind kind, long started, long now) => started != 0 && now - started >= BuildMilliseconds(kind);

        public static int SecondsLeft(BotSiegeKind kind, long started, long now) =>
            (int)Math.Max(0, (BuildMilliseconds(kind) - (now - started) + 999) / 1000);

        // --- the per-siege stock ---

        /// <summary>A siege's stock is forgotten after this long with no siege work at the keep: the 10-minute repel
        /// rule ends a bot siege with no attacker present, so the next siege there gets a fresh stock.</summary>
        public const long StockIdleResetMilliseconds = 10 * 60_000;

        /// <summary>Synthetic engines one realm may set up at one keep in one siege: one per working place
        /// (<see cref="AutonomousSiegeJobs.Slots"/>) plus replacements, two at least and one more per 12 present.
        /// 100 present: 6 + 8 = 14. Played 1.2.2 868a82d (Dun Abermenai, 3 hours): 82 deployments, 19 destroyed.</summary>
        public static int Stock(int present)
        {
            int places = AutonomousSiegeJobs.Slots(present);
            return places == 0 ? 0 : places + Math.Max(2, present / 12);
        }

        private sealed class Siege { public long LastWork; public int MostPresent; public int Built; }
        private static readonly object Gate = new();
        private static readonly Dictionary<(ushort Region, string Keep, eRealm Realm), Siege> Sieges = new();

        private static Siege Current((ushort, string, eRealm) key, long now)
        {
            if (!Sieges.TryGetValue(key, out var siege) || now - siege.LastWork > StockIdleResetMilliseconds)
                Sieges[key] = siege = new Siege();
            if (Sieges.Count > 256)
                foreach (var stale in Sieges.Where(p => now - p.Value.LastWork > StockIdleResetMilliseconds).Select(p => p.Key).ToArray())
                    Sieges.Remove(stale);
            return siege;
        }

        /// <summary>Siege work at the keep (any operator holding a job): keeps the stock alive and sizes it to the
        /// largest army seen at the keep this siege, so it never shrinks as the army takes losses.</summary>
        public static void Touch(ushort region, string keep, eRealm realm, int present, long now)
        {
            if (keep == null) return;
            lock (Gate)
            {
                var siege = Current((region, keep, realm), now);
                siege.LastWork = now;
                siege.MostPresent = Math.Max(siege.MostPresent, present);
            }
        }

        /// <summary>Engines left in this siege's stock (with the army now present counted).</summary>
        public static int Remaining(ushort region, string keep, eRealm realm, int present, long now, out int stock)
        {
            lock (Gate)
            {
                var siege = Current((region, keep, realm), now);
                siege.LastWork = now;
                siege.MostPresent = Math.Max(siege.MostPresent, present);
                stock = Stock(siege.MostPresent);
                return Math.Max(0, stock - siege.Built);
            }
        }

        /// <summary>Takes one engine from the stock when a build completes; false if the stock is spent.</summary>
        public static bool TryTake(ushort region, string keep, eRealm realm, int present, long now)
        {
            lock (Gate)
            {
                var siege = Current((region, keep, realm), now);
                siege.LastWork = now;
                siege.MostPresent = Math.Max(siege.MostPresent, present);
                if (siege.Built >= Stock(siege.MostPresent)) return false;
                siege.Built++;
                return true;
            }
        }

        /// <summary>Tests only.</summary>
        public static void Reset() { lock (Gate) Sieges.Clear(); }

        /// <summary>The operator's status detail, true for both sources of engine.</summary>
        public static string StatusDetail(bool synthetic, int remaining, int stock) => synthetic
            ? $"Siege engines built on the spot ({remaining} of {stock} left this siege) and native combat timers"
            : "Carried siege kit and native combat timers";
    }
}
