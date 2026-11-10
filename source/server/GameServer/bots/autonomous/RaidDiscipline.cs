using System;
using System.Collections.Generic;
using System.Linq;

namespace DOL.GS
{
    /// <summary>
    /// Raid discipline (Living Server 1.2, owner 2026-11-08): how well a bot raid plays a scripted mechanic it has to dodge,
    /// the way a 2003 raid did. Three layers, all on the bot side; no boss number changes:
    /// <list type="number">
    /// <item><b>The call.</b> The raid's caller shouts the mechanic a variable time before it lands, sometimes late
    /// (<see cref="CallFor"/>). Raiders react to the call, not to the boss's health.</item>
    /// <item><b>Each raider is a little imperfect</b> (<see cref="ReactionFor"/>): a reaction delay after the call from its
    /// personality, and now and then a lapse: a risk-taker finishes its cast or swing, a healer finishes a heal on the tank,
    /// and leaves late or not at all.</item>
    /// <item><b>The realm learns the boss</b> (<see cref="Familiarity(int, int)"/>): every counted attempt at the encounter, and
    /// every win more, makes the calls earlier, the reactions quicker and the lapses rarer. A fresh world is unlearned.</item>
    /// </list>
    /// Everything here is pure and deterministic per (raider, mechanic, occurrence, seed), so tests and container runs can be
    /// reproduced. Wired today: Xanxicar's stomp (EpicEncounterTactics.RaidCalls.cs). The same API is meant for Scurceol's orbs,
    /// Aidon's illusions, Nosdoden's waves and the other scripted mechanics a raid leader calls.
    /// </summary>
    public static class RaidDiscipline
    {
        // ---- familiarity ------------------------------------------------------------------------------------------------

        /// <summary>Experience points per counted attempt (a win or a loss) and on top for a win.</summary>
        public const double ExperiencePerAttempt = 1, ExperiencePerWin = 2;
        /// <summary>Familiarity is 1 - e^(-experience / scale): 3 losses = 0.39, 3 losses and 2 wins = 0.78, 10 raids with 6
        /// wins = 0.97.</summary>
        public const double ExperienceScale = 6;

        /// <summary>How well the realm knows the encounter, 0 (never fought) to 1 (pure).</summary>
        public static double Familiarity(int attempts, int wins)
        {
            double experience = Math.Max(0, attempts) * ExperiencePerAttempt + Math.Clamp(wins, 0, Math.Max(0, attempts)) * ExperiencePerWin;
            return Math.Clamp(1 - Math.Exp(-experience / ExperienceScale), 0, 1);
        }

        /// <summary>From an encounter's counted results (RealmRaidRecord.GetResults): attempts and wins (pure).</summary>
        public static double Familiarity(IEnumerable<RaidResult> results)
        {
            RaidResult[] counted = (results ?? []).Where(r => RealmRaidRecord.Counted(r.Outcome)).ToArray();
            return Familiarity(counted.Length, counted.Count(r => AutonomousEpicSpecialForces.IsWin(r.Outcome)));
        }

        private static double _override = -1;

        /// <summary>The GM play-test override (/epictest familiarity 0..1); null: read from the record.</summary>
        public static double? FamiliarityOverride
        {
            get { double v = System.Threading.Volatile.Read(ref _override); return v < 0 ? null : v; }
            set => System.Threading.Volatile.Write(ref _override, value is double v ? Math.Clamp(v, 0, 1) : -1);
        }

        private static int _lateCallAt;

        /// <summary>The GM play-test override (/epictest latecall): the call of this blast threshold always comes late (-1: every
        /// threshold; 0: off, the usual roll). Test only, to see what a late call costs without waiting for the 15% roll.</summary>
        public static int ForceLateCallAt
        {
            get => System.Threading.Volatile.Read(ref _lateCallAt);
            set => System.Threading.Volatile.Write(ref _lateCallAt, value);
        }

        /// <summary>The play-test override applies to this occurrence (pure).</summary>
        public static bool ForcedLate(int forceAt, int occurrence) => forceAt == -1 || forceAt > 0 && forceAt == occurrence;

        /// <summary>The realm's familiarity with a raid's encounter (the override when a play test set one; 0 for no raid).</summary>
        public static double FamiliarityOf(string raidId) =>
            FamiliarityOverride ?? (string.IsNullOrEmpty(raidId) ? 0 : Familiarity(RealmRaidRecord.GetResults(raidId)));

        /// <summary>A raid's seed for its raiders' rolls: the attempts on its record, so each new raid rolls afresh and a test
        /// or a replay rolls the same.</summary>
        public static int SeedOf(string raidId) => string.IsNullOrEmpty(raidId) ? 0 : RealmRaidRecord.GetResults(raidId).Count;

        private static double Lerp(double unlearned, double learned, double familiarity) =>
            unlearned + (learned - unlearned) * Math.Clamp(familiarity, 0, 1);

        // ---- the call ---------------------------------------------------------------------------------------------------

        /// <summary>A call on time comes this many seconds before the mechanic lands: unlearned 8.0-10.5, learned 9.5-13.5.
        /// Container runs duo16/duo17 (Xanxicar): from melee range a raider needs about 8-9 s after the call to get clear of
        /// his stomp (it hits everyone within 2,500 of his target when it lands, falling off with distance), so calls of
        /// 4-8 s killed 30-85% of the raiders in range, the learned ones too. Unlearned started at 6.5 until duo21-duo24: an
        /// on-time call of 6.6-7.3 s killed 33-49% (as many as a late call: 47% at 6.8 s, 41% at 6.6 s), so "on time" and
        /// "late" were the same call, and duo24 wiped at 40% after three of them; on time now starts where late ends.
        /// duo25 (7.5 s, late call forced at 80%): the late 6.8 s call killed 44%, a 9.0 s call 41%, and the raid wiped at 46%;
        /// so on time 8.0-10.5 and late 7.0-8.0 (the stomp model of UT_RaidDiscipline: 25.1% unlearned, 7.7% learned; at 8.5 it gave 23.6%, under the band).</summary>
        public const double CallLeadUnlearnedMin = 8.0, CallLeadUnlearnedMax = 10.5, CallLeadLearnedMin = 9.5, CallLeadLearnedMax = 13.5;
        /// <summary>The share of calls that come late (unlearned 15%, learned 5%), and how late: 7.0-8.0 s before it lands,
        /// short of the 8-9 s a raider at melee range needs. Late calls of 4.2 and 4.3 s (duo19, duo20) killed 61% and 72% of
        /// the raiders in range and both raids wiped: a wipe from the stomp alone, which the owner counts as mistuned. At
        /// 5.5-7.5 s (duo24, duo25, forced at 80%) a late call killed 44-47% and both raids wiped by 40-46%.</summary>
        public const double LateCallUnlearned = 0.15, LateCallLearned = 0.05, LateCallLeadMin = 7.0, LateCallLeadMax = 8.0;

        /// <param name="LeadSeconds">How long before the mechanic lands the caller shouts it.</param>
        /// <param name="Late">It came late (after the caller hesitated or saw it late).</param>
        public readonly record struct Call(double LeadSeconds, bool Late);

        /// <summary>When the caller calls this occurrence of the mechanic (pure, deterministic).</summary>
        /// <param name="forceLate">Test only (/epictest latecall): this call comes late whatever the roll.</param>
        public static Call CallFor(long callerId, string mechanic, int occurrence, int seed, double familiarity, bool forceLate = false)
        {
            ulong key = Key(callerId, mechanic, occurrence, seed);
            bool late = forceLate || Roll(key, 1) < Lerp(LateCallUnlearned, LateCallLearned, familiarity);
            double lead = late
                ? LateCallLeadMin + Roll(key, 2) * (LateCallLeadMax - LateCallLeadMin)
                : Lerp(CallLeadUnlearnedMin, CallLeadLearnedMin, familiarity) +
                  Roll(key, 2) * (Lerp(CallLeadUnlearnedMax, CallLeadLearnedMax, familiarity) - Lerp(CallLeadUnlearnedMin, CallLeadLearnedMin, familiarity));
            return new Call(lead, late);
        }

        // ---- each raider ------------------------------------------------------------------------------------------------

        /// <summary>The raider's part, for its lapse: melee finish a swing, casters and archers a cast or a shot, healers a heal.</summary>
        public enum Role { Melee, Caster, Healer }

        public enum Lapse
        {
            /// <summary>Leaves after its reaction delay.</summary>
            None,
            /// <summary>Finishes what it is doing first, and leaves late.</summary>
            Late,
            /// <summary>Finishes what it is doing and never leaves.</summary>
            Stays,
        }

        /// <param name="DelaySeconds">From the call to the raider moving (lapse included); infinite when it stays.</param>
        public readonly record struct Reaction(double DelaySeconds, Lapse Lapse, string Reason)
        {
            public bool Leaves => Lapse != Lapse.Stays;
        }

        /// <summary>Reaction delay: 0.5 s plus a span (unlearned 3.5 s, learned 2 s) times a mix of half a roll, a quarter
        /// risk and a quarter impatience. So 0.5-4 s unlearned, 0.5-2.5 s learned; a patient, careful raider is quick.</summary>
        public const double ReactionMinimumSeconds = 0.5, ReactionSpanUnlearned = 3.5, ReactionSpanLearned = 2.0;
        public const double ReactionRollWeight = 0.5, ReactionRiskWeight = 0.25, ReactionImpatienceWeight = 0.25;
        /// <summary>Lapse chance before traits (unlearned 16%, learned 5%), times 0.6 + 0.8 * Risk/9 (0.6-1.4), times 1.5 for a
        /// healer (the tank is low when the call comes).</summary>
        public const double LapseUnlearned = 0.16, LapseLearned = 0.05, HealerLapseFactor = 1.5;
        /// <summary>Of the lapses, this share never leaves; the rest leave 2-4 s late.</summary>
        public const double LapseStaysShare = 0.4, LapseLateMinSeconds = 2, LapseLateMaxSeconds = 4;

        /// <summary>A neutral raider (personalities off): middling risk and patience.</summary>
        public static readonly BotPersonalityTraits NeutralTraits = new(5, 5, 5, 5, 5);

        /// <summary>How this raider answers this call (pure, deterministic).</summary>
        public static Reaction ReactionFor(long raiderId, BotPersonalityTraits traits, Role role, string mechanic, int occurrence,
            int seed, double familiarity)
        {
            ulong key = Key(raiderId, mechanic, occurrence, seed);
            double risk = Math.Clamp(traits.Risk, 0, 9) / 9.0, impatience = 1 - Math.Clamp(traits.Patience, 0, 9) / 9.0;
            double mix = ReactionRollWeight * Roll(key, 11) + ReactionRiskWeight * risk + ReactionImpatienceWeight * impatience;
            double delay = ReactionMinimumSeconds + Lerp(ReactionSpanUnlearned, ReactionSpanLearned, familiarity) * mix;
            double lapse = Lerp(LapseUnlearned, LapseLearned, familiarity) * (0.6 + 0.8 * risk) * (role == Role.Healer ? HealerLapseFactor : 1);
            if (Roll(key, 12) >= lapse) return new Reaction(delay, Lapse.None, "on the call");
            string what = role switch
            {
                Role.Healer => "finishing a heal on the tank",
                Role.Caster => "finishing a cast",
                _ => "finishing a swing",
            };
            if (Roll(key, 13) < LapseStaysShare) return new Reaction(double.PositiveInfinity, Lapse.Stays, what + ", never left");
            return new Reaction(delay + LapseLateMinSeconds + Roll(key, 14) * (LapseLateMaxSeconds - LapseLateMinSeconds), Lapse.Late,
                what + ", left late");
        }

        // ---- what the caller says ---------------------------------------------------------------------------------------

        /// <summary>A call on time (2003 raid voice; no internal words).</summary>
        public static readonly string[] OutLines =
        [
            "STOMP INC, everyone OUT",
            "out out out",
            "back off he's gonna stomp",
            "stomp coming, everyone out",
            "get out get out, stomp",
            "STOMP - run out",
            "everyone OUT, he's stomping",
            "out of range NOW, stomp",
        ];

        /// <summary>A learned caller sometimes names the health it comes at ({pct}).</summary>
        public static readonly string[] LearnedOutLines = ["{pct}% stomp, everyone out", "{pct} stomp - OUT", "stomp at {pct}, get out"];

        /// <summary>A late call.</summary>
        public static readonly string[] LateLines = ["OUT OUT OUT", "STOMP RUN", "omg out NOW", "RUN stomp!!", "OUT NOW"];

        /// <summary>After it landed.</summary>
        public static readonly string[] BackInLines = ["back in", "ok back in", "back in go go", "clear, back on him", "in in in", "ok back on him"];

        /// <summary>The caller's line for this call (pure): late, learned (with the health, at familiarity 0.5 and up, a third
        /// of the time) or plain.</summary>
        public static string OutLine(Call call, int percent, long callerId, string mechanic, int occurrence, int seed, double familiarity)
        {
            ulong key = Key(callerId, mechanic, occurrence, seed);
            if (call.Late) return Pick(LateLines, key, 21);
            if (familiarity >= 0.5 && percent > 0 && Roll(key, 22) < 1 / 3.0)
                return Pick(LearnedOutLines, key, 23).Replace("{pct}", percent.ToString());
            return Pick(OutLines, key, 24);
        }

        public static string BackInLine(long callerId, string mechanic, int occurrence, int seed) =>
            Pick(BackInLines, Key(callerId, mechanic, occurrence, seed), 31);

        // ---- deterministic rolls ----------------------------------------------------------------------------------------

        private static string Pick(string[] lines, ulong key, ulong salt) => lines[(int)(AutonomousBotPersonality.Mix((long)key, salt) % (ulong)lines.Length)];

        /// <summary>One key per (raider, mechanic, occurrence, seed).</summary>
        public static ulong Key(long id, string mechanic, int occurrence, int seed)
        {
            unchecked
            {
                ulong h = 14695981039346656037UL;
                foreach (char c in mechanic ?? string.Empty) { h ^= c; h *= 1099511628211UL; }
                h ^= (ulong)(uint)occurrence; h *= 1099511628211UL;
                h ^= (ulong)(uint)seed; h *= 1099511628211UL;
                return AutonomousBotPersonality.Mix(id, h);
            }
        }

        /// <summary>A roll in [0, 1) from a key and a salt.</summary>
        public static double Roll(ulong key, ulong salt) =>
            (AutonomousBotPersonality.Mix((long)key, salt) >> 11) * (1.0 / (1UL << 53));
    }
}
