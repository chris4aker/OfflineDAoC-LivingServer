using System;
using System.Collections.Generic;
using System.Numerics;
using DOL.GS.PacketHandler;

namespace DOL.GS
{
    /// <summary>
    /// Realism 4a (#487, movement audit 1.2.3 tells 2, 4, 6, 7, 8): the small, visual-only rules for how an idle bot's body
    /// looks to a player standing beside it. Every decision here is a pure function of state and rolls, so it is unit
    /// tested without a world; the call sites stay where they were. Nothing here changes a fight: combat, aggro, casting and
    /// raids always win at once, and a bot no player can see keeps today's pace.
    /// </summary>
    public static class BotIdleLife
    {
        // ---- The seat animation (the one switch for the open PC check) --------------------------------------------------

        /// <summary>
        /// Whether a seated bot is shown seated at all. The NPC packets carry no sit state, so a seat is shown by an emote
        /// animation (<see cref="SitAnimation"/> to sit, <see cref="StandAnimation"/> to stand). Not yet seen on the
        /// original client (#487 PC check): set this false to drop the animation everywhere, or change the two ids below
        /// to another animation. The seat's logic (when a bot sits, keeps or leaves its seat) does not change with it.
        /// </summary>
        public static readonly bool ShowSeat = true;

        /// <summary>Sit down, then sit-idle.</summary>
        public static readonly eEmote SitAnimation = (eEmote)0xF0;
        /// <summary>Stand up.</summary>
        public static readonly eEmote StandAnimation = (eEmote)0xF1;

        // ---- The rest seat (tell 4) ---------------------------------------------------------------------------------------

        /// <summary>A watched seated bot stands this long after it decides to go, spread per bot.</summary>
        public const int StandSpreadMinimumMs = 400;
        public const int StandSpreadMaximumMs = 1_500;

        /// <summary>
        /// Whether a bot whose rest just ended (it is full) keeps its seat until its next move, cast or attack instead of
        /// standing at once. Only an autonomous gamebot of its own (not a companion, a hired companion or a bot in a
        /// player's group: those keep today's rules), alive, seated, and with nothing to fight. Visual only: the rest itself
        /// has ended, so recovery and its timing are unchanged.
        /// </summary>
        public static bool KeepsSeatAfterRest(bool ownAutonomousGamebot, bool alive, bool seated, bool combatBlocked,
            bool moving, bool casting, bool attacking) =>
            ownAutonomousGamebot && alive && seated && !combatBlocked && !moving && !casting && !attacking;

        /// <summary>The per-bot delay between "decided to go" and standing up, 0.4-1.5 s.</summary>
        public static int StandSpreadMs(double roll) =>
            StandSpreadMinimumMs + (int)(Math.Clamp(roll, 0, 0.999999) * (StandSpreadMaximumMs - StandSpreadMinimumMs + 1));

        /// <summary>A seated bot told to go holds its seat for the spread only while a player can see it and nothing fights.</summary>
        public static bool SpreadsStanding(bool watched, bool combatBlocked) => watched && !combatBlocked;

        // ---- The post-kill breather (tell 8) ------------------------------------------------------------------------------

        public const int BreatherMinimumMs = 1_500;
        public const int BreatherMaximumMs = 7_000;

        /// <summary>A breather of 1.5-7 s, skewed short (most are 2-4 s).</summary>
        public static int BreatherMs(double roll)
        {
            double r = Math.Clamp(roll, 0, 0.999999);
            return BreatherMinimumMs + (int)(r * r * (BreatherMaximumMs - BreatherMinimumMs + 1));
        }

        /// <summary>
        /// Whether a bot that just finished a fight pauses before its next pull: only while a real player can see it, never
        /// with aggro or a fight near it (an add, a party member fighting), never in a player's group, an RvR party or a
        /// raid. Unwatched bots keep today's pace, so the world's progress is unchanged.
        /// </summary>
        public static bool BreathesAfterKill(bool watched, bool hasAggro, bool combatBlocked, bool playerLedGroup,
            bool rvrParty, bool raid) =>
            watched && !hasAggro && !combatBlocked && !playerLedGroup && !rvrParty && !raid;

        // ---- A standing party (tell 6) ------------------------------------------------------------------------------------

        public const double RingRadiusMinimumScale = 0.75;
        public const double RingRadiusMaximumScale = 1.35;
        public const double RingAngleSpreadDegrees = 14;
        /// <summary>The town rule's personal space.</summary>
        public const float RingPersonalSpace = 32;

        private static readonly double[] SpreadSteps = [1, 0.75, 0.5, 0.25, 0];

        /// <summary>
        /// The offsets (from the centre) of a standing party's members, in member order: the even ring of
        /// <paramref name="radius"/>, with each member's radius scaled 0.75-1.35 and its angle moved up to 14 degrees, by a
        /// stable hash of its key. A member that would stand within 32 units of an earlier one takes less of its spread
        /// (down to its even-ring point, which always clears for 2-8 members). Same keys, same slots: stable across turns.
        /// </summary>
        public static Vector2[] RingOffsets(IReadOnlyList<long> keys, int radius, double rotationRadians)
        {
            int count = keys?.Count ?? 0;
            var slots = new Vector2[count];
            for (int i = 0; i < count; i++)
            {
                double even = rotationRadians + Math.PI * 2d * i / count;
                ulong hash = Mix((ulong)keys[i]);
                double radiusScale = RingRadiusMinimumScale + Unit(hash) * (RingRadiusMaximumScale - RingRadiusMinimumScale);
                double angleShift = (Unit(Mix(hash)) * 2 - 1) * RingAngleSpreadDegrees * Math.PI / 180d;
                Vector2 chosen = default;
                foreach (double share in SpreadSteps)
                {
                    double r = radius * (1 + (radiusScale - 1) * share);
                    double a = even + angleShift * share;
                    chosen = new Vector2((float)(Math.Cos(a) * r), (float)(Math.Sin(a) * r));
                    if (Clear(slots, i, chosen))
                        break;
                }
                slots[i] = chosen;
            }
            return slots;
        }

        private static bool Clear(Vector2[] slots, int count, Vector2 point)
        {
            for (int j = 0; j < count; j++)
                if (Vector2.Distance(slots[j], point) < RingPersonalSpace)
                    return false;
            return true;
        }

        private static double Unit(ulong hash) => (hash >> 11) * (1.0 / (1UL << 53));

        /// <summary>SplitMix64: a stable spread of a member's id (the same in every run).</summary>
        private static ulong Mix(ulong x)
        {
            x += 0x9E3779B97F4A7C15UL;
            x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
            x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
            return x ^ (x >> 31);
        }

        // ---- Emote text (tell 7) ------------------------------------------------------------------------------------------

        /// <summary>
        /// The line a bot's emote prints for one viewer, as a player's emote does (EmoteCommandHandler): "Brannoc waves." with
        /// no target, "Brannoc waves at you." to the target, "Brannoc waves at Maeve." to others. Null when the emote has no
        /// text (a bot-only animation such as a roar or the seat). <paramref name="messages"/> is the player emote's four
        /// forms; <paramref name="name"/> is the bot's name as this viewer sees it.
        /// </summary>
        public static string EmoteLine(string[] messages, string name, string targetName, bool viewerIsTarget)
        {
            if (messages == null || messages.Length < 4 || string.IsNullOrEmpty(name))
                return null;
            if (string.IsNullOrEmpty(targetName) && !viewerIsTarget)
                return string.Format(messages[1], name);
            return string.Format(messages[3], name, viewerIsTarget ? "you" : targetName);
        }
    }
}
