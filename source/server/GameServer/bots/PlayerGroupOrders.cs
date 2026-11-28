using System;
using System.Collections.Generic;
using DOL.GS.ServerProperties;

namespace DOL.GS
{
    /// <summary>A fight order the player gives his group (#667). <see cref="None"/>: no order, the standing stance rules.</summary>
    public enum GroupOrder : byte { None = 0, Attack, Assist, Hold, Regroup }

    /// <summary>What the order's end rule needs to know about the group now (pure input).</summary>
    /// <param name="SawFight">Someone of the group near the player has been in combat since the order was given.</param>
    /// <param name="InFight">Someone of the group near the player is in combat now.</param>
    /// <param name="QuietMilliseconds">How long nobody of the group near the player has been in combat (0 while in one).</param>
    /// <param name="SinceGivenMilliseconds">How long ago the order was given.</param>
    /// <param name="TargetGone">The attack order's target is dead, gone or out of the player's region.</param>
    /// <param name="AllBack">Every bot that obeys the order is within <see cref="PlayerGroupOrders.RegroupRadius"/> of the player.</param>
    public readonly record struct GroupOrderFacts(bool SawFight, bool InFight, long QuietMilliseconds, long SinceGivenMilliseconds,
        bool TargetGone, bool AllBack);

    /// <summary>
    /// #667 step 1: the player orders his group in a fight, as a group leader calls it in group chat (FarmVibes: "there
    /// needs to be a way to control the way your group is behaving. such as kite back, attack, pbaoe, assist on me").
    ///
    /// Owner answers (#667, 2026-11-26 22:19):
    /// - the full order list (step 1 builds attack, assist, hold and regroup; the rest are later steps on #667);
    /// - given by slash command, by group-chat word and by a click menu (step 1: <c>/order &lt;word&gt;</c>, which a client
    ///   macro can hold, and the word alone as a whole /group line; the menu is a later step);
    /// - an order lasts until the next order or the end of the fight (<see cref="Ends"/>);
    /// - the gamebots in a group the player leads obey as his companions do (<see cref="Obeys"/>);
    /// - a suicidal order is obeyed, with a short warning in group chat (<see cref="Warns"/>).
    ///
    /// Period: one main assist, and "Everyone then makes an /assist macro" (FH Nov 2003), "even if MA is not leading ALL
    /// tanks stick to the ma" (FH Aug 2003); calls are short words in group chat ("stick", "inc"); break off and regroup
    /// when the odds turn (docs/rvr-audit-1.2.3/reference.md 3.1, 3.4, 3.7; docs/research/rvr-field-1.2/ERA-RESEARCH.md 2).
    ///
    /// Pure rules here; the world side is PlayerGroupOrders.World.cs. Tests only until seen in a PC log.
    /// </summary>
    public static partial class PlayerGroupOrders
    {
        public const string PropertyKey = "bot_group_orders";

        public static bool On => Properties.BOT_GROUP_ORDERS;

        /// <summary>The fight is over once nobody of the group near the player has been in combat for this long. A design value.</summary>
        public const long FightOverQuietMilliseconds = 5_000;

        /// <summary>An attack, assist or hold order given out of a fight waits this long for one, then ends. A design value.</summary>
        public const long NoFightMilliseconds = 120_000;

        /// <summary>Regroup: a bot is back once it is within this of the player. A design value.</summary>
        public const int RegroupRadius = 500;

        /// <summary>Regroup lasts at least this long, so a group already beside the player breaks off before it fights on. A design value.</summary>
        public const long RegroupMinimumMilliseconds = 3_000;

        /// <summary>Regroup ends after this long even if a bot could not come back (a bot stuck behind a wall). A design value.</summary>
        public const long RegroupMilliseconds = 20_000;

        /// <summary>At most one answer in group chat per group this often (a macro spammed in a fight gets one line).</summary>
        public const long AnswerCooldownMilliseconds = 3_000;

        /// <summary>How often the end rule is looked at per player.</summary>
        public const long CheckIntervalMilliseconds = 500;

        // The words, as a whole line (after Normalize). A line that only contains one ("did you see that attack") is talk.
        private static readonly Dictionary<string, GroupOrder> Words = new(StringComparer.Ordinal)
        {
            ["attack"] = GroupOrder.Attack, ["atk"] = GroupOrder.Attack, ["attack my target"] = GroupOrder.Attack,
            ["kill my target"] = GroupOrder.Attack,
            ["assist"] = GroupOrder.Assist, ["assist me"] = GroupOrder.Assist, ["assist on me"] = GroupOrder.Assist,
            ["assist my target"] = GroupOrder.Assist,
            ["hold"] = GroupOrder.Hold, ["hold here"] = GroupOrder.Hold, ["hold position"] = GroupOrder.Hold,
            ["regroup"] = GroupOrder.Regroup, ["regroup on me"] = GroupOrder.Regroup, ["on me"] = GroupOrder.Regroup,
            ["back to me"] = GroupOrder.Regroup, ["fall back to me"] = GroupOrder.Regroup,
        };

        /// <summary>The words <c>/order</c> lists, one per order.</summary>
        public static readonly IReadOnlyList<(string Word, string Does)> Help =
        [
            ("attack", "attack your selected target now"),
            ("assist", "fight only your fight, and switch when you switch"),
            ("hold", "only defend themselves (as /hold) until the fight ends; healers keep healing"),
            ("regroup", "break off and come back to you"),
        ];

        /// <summary>The line as the word table reads it: lower case, single spaces, without end punctuation. Pure.</summary>
        public static string Normalize(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return string.Empty;
            string text = line.Trim().ToLowerInvariant().TrimEnd('!', '.', '?', ',');
            return string.Join(" ", text.Split((char[])null, StringSplitOptions.RemoveEmptyEntries));
        }

        /// <summary>The order this whole line gives, or <see cref="GroupOrder.None"/>. Pure.</summary>
        public static GroupOrder Parse(string line) =>
            Words.TryGetValue(Normalize(line), out GroupOrder order) ? order : GroupOrder.None;

        /// <summary>The word of an order, for the log and the player's messages. Pure.</summary>
        public static string Word(GroupOrder order) => order switch
        {
            GroupOrder.Attack => "attack",
            GroupOrder.Assist => "assist",
            GroupOrder.Hold => "hold",
            GroupOrder.Regroup => "regroup",
            _ => "none",
        };

        /// <summary>
        /// A bot obeys the player's orders: it is in his group and he commands it (BotHumanBinding.Commands: his own
        /// companions, and the gamebots when he leads the group or is its only human), and no per-bot order keeps it out
        /// (the passive stance, /bot hold, /bot stay win for that one bot). Pure.
        /// </summary>
        public static bool Obeys(bool inPlayersGroup, bool commanded, bool passiveStance, bool suspended, bool staying) =>
            inPlayersGroup && commanded && !passiveStance && !suspended && !staying;

        /// <summary>
        /// Whether the order ends now, and why (null: it goes on). Every order ends at the next order (the caller). Attack,
        /// assist and hold end when the fight ends (<see cref="FightOverQuietMilliseconds"/> of quiet after one), or after
        /// <see cref="NoFightMilliseconds"/> when no fight came; attack also when its target is gone. Regroup ends when every
        /// bot is back (after at least <see cref="RegroupMinimumMilliseconds"/>) or after <see cref="RegroupMilliseconds"/>.
        /// Hold ends like the others (owner decision 65A, 2026-11-28: "/order hold ends with the fight"); the standing /hold
        /// command is unchanged and lasts until /aggressive or /defensive. Pure.
        /// </summary>
        public static string Ends(GroupOrder order, in GroupOrderFacts facts)
        {
            switch (order)
            {
                case GroupOrder.Attack:
                case GroupOrder.Assist:
                case GroupOrder.Hold:
                    if (order == GroupOrder.Attack && facts.TargetGone) return "target";
                    if (facts.SawFight && !facts.InFight && facts.QuietMilliseconds >= FightOverQuietMilliseconds) return "fight";
                    if (!facts.SawFight && facts.SinceGivenMilliseconds >= NoFightMilliseconds) return "timeout";
                    return null;
                case GroupOrder.Regroup:
                    if (facts.AllBack && facts.SinceGivenMilliseconds >= RegroupMinimumMilliseconds) return "back";
                    return facts.SinceGivenMilliseconds >= RegroupMilliseconds ? "timeout" : null;
                default:
                    return "none";
            }
        }

        /// <summary>Regroup: a bot still on its way back fights back below this share of its health (in percent). A design value.</summary>
        public const int RegroupFightBackHealthPercent = 50;

        /// <summary>
        /// Regroup's gate for one bot of the group (review of #824, point 2). Once back at the player it defends him and the
        /// group: anything hitting it, the player or a groupmate (<paramref name="hitsGroup"/>). On the way back it breaks
        /// off and opens nothing, but fights back what is hitting it when it can't get away: it can't move (rooted,
        /// <paramref name="cannotMove"/>) or it is below <see cref="RegroupFightBackHealthPercent"/>% health. Every other
        /// order leaves the gate to the stance rules. Pure.
        /// </summary>
        public static bool MayFight(GroupOrder order, bool back, bool hitsMe, bool hitsGroup, bool cannotMove, int healthPercent) =>
            order != GroupOrder.Regroup ||
            (back ? hitsMe || hitsGroup : hitsMe && (cannotMove || healthPercent < RegroupFightBackHealthPercent));

        /// <summary>
        /// The per-bot holds win over a party order (owner answer 4): the passive stance, /bot hold (the brain's PASSIVE
        /// state) and, for a /bot companion, /bot stay (IDLE; other bots pass through IDLE in their own work). Pure.
        /// </summary>
        public static bool HeldBack(eBotStance stance, eFSMStateType? state, bool botCompanion) =>
            stance == eBotStance.Passive || state == eFSMStateType.PASSIVE ||
            botCompanion && state == eFSMStateType.IDLE;

        /// <summary>
        /// The order lets this target through /defensive's range and the RvR leash: attack's own target, or under assist the
        /// player's fight (so the group follows its main assist wherever he fights). Pure.
        /// </summary>
        public static bool LetsThrough(GroupOrder order, bool orderedTarget, bool playersFight) =>
            order == GroupOrder.Attack && orderedTarget || order == GroupOrder.Assist && playersFight;

        /// <summary>
        /// The order sets this bot's target: attack and assist, for every fighter, tanks included ("ALL tanks stick to the
        /// ma"); a tank's peel for a groupmate still comes first (BotBrain's protection target), and supports heal. Pure.
        /// </summary>
        public static bool Focuses(GroupOrder order, bool support) =>
            (order == GroupOrder.Attack || order == GroupOrder.Assist) && !support;

        /// <summary>
        /// The group picks nothing of its own (no #664 pick, no /defensive nearby pull, no /raid radar, no shared threat
        /// focus): assist (the player alone calls the target) and regroup (the group is breaking off). Pure.
        /// </summary>
        public static bool SuppressesOwnPick(GroupOrder order) => order is GroupOrder.Assist or GroupOrder.Regroup;

        /// <summary>
        /// The answering bot warns the player (owner answer 5: obey, but say a short warning): an attack where realms fight
        /// that #664's size-up (the group's line, 0.64) calls a clearly losing fight. Pure.
        /// </summary>
        public static bool Warns(GroupOrder order, bool warZone, RvrSightCall call) =>
            order == GroupOrder.Attack && warZone && call == RvrSightCall.BackOff;

        /// <summary>One answer per group per <see cref="AnswerCooldownMilliseconds"/>. Pure.</summary>
        public static bool Answers(long now, long lastAnswer) => lastAnswer <= 0 || now - lastAnswer >= AnswerCooldownMilliseconds;

        /// <summary>The answering bot's line in group chat. Pure.</summary>
        public static string Line(GroupOrder order, string target, bool warn) => order switch
        {
            GroupOrder.Attack when warn => $"There are a lot of them around {target}. Going in anyway.",
            GroupOrder.Attack => $"On {target}.",
            GroupOrder.Assist => "Assisting you.",
            GroupOrder.Hold => "Holding.",
            GroupOrder.Regroup => "Coming back to you.", // promise: true (regroup drops the fight and the bot walks back)
            _ => null,
        };
    }
}
