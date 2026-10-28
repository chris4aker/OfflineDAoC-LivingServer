using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.AI.Brain;
using DOL.GS.Spells;
using DOL.Logging;
using VC = DOL.GS.AutonomousBotVoiceCorpus;

namespace DOL.GS;

/// <summary>
/// Combat calls in the real player's group (the talking update). Gamebots in a player's group went silent the moment a fight
/// started, so a group of bots fought like pets. Now they call what is really happening, in /group and briefly: the pull
/// ("inc"), an add ("add, add"), their own low health or power ("need a heal", "oom"), a mez, root, stun, taunt, group heal or
/// rez they are casting right now, a wipe ("run!") and the end of the fight ("gj", "med up"). Every call comes from the
/// group's or the bot's real state; nothing is said on a guess. The decisions are pure (unit-tested); Observe reads the game.
/// </summary>
public static class AutonomousBotCombatCalls
{
    private static readonly Logger Log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);

    /// <summary>How often a bot looks at the fight.</summary>
    public const int CheckMs = 1_000;
    /// <summary>At most one call in the group this often (a wipe call excepted).</summary>
    public const int GroupGapMs = 3_500;
    /// <summary>At most one call by a bot this often.</summary>
    public const int BotGapMs = 8_000;
    public const int AddGapMs = 15_000;
    public const int LowHpGapMs = 45_000;
    public const int OomGapMs = 60_000;
    public const int CastGapMs = 20_000;

    public const int LowHpPercent = 30;
    public const int LowHpResetPercent = 50;
    public const int OomPercent = 15;
    public const int OomResetPercent = 40;

    /// <summary>The group's fight as one bot sees it this second.</summary>
    public sealed record GroupView(bool Fighting, int Enemies, int Members, int Alive, bool Worn, bool PlayerDown = false);

    public sealed class GroupState
    {
        public bool WasFighting;
        public int MaxEnemies;
        public int DeadAtStart;
        public bool WipeCalled;
        public long LastAdd = long.MinValue / 2;
        public long LastCall = long.MinValue / 2;
        public long LastObserved = long.MinValue / 2;
        /// <summary>When the group's last fight ended (game time).</summary>
        public long LastFightEnded = long.MinValue / 2;
    }

    /// <summary>How long after a fight hunting talk ("nice pull", "good camp") still fits.</summary>
    public const int HuntTalkWindowMs = 10 * 60_000;

    /// <summary>Whether this group fought in the last ten minutes (its combat calls track the fights).</summary>
    public static bool FoughtRecently(Group group, long now) =>
        group != null && Groups.TryGetValue(group, out GroupState state) &&
        (state.WasFighting || now - state.LastFightEnded <= HuntTalkWindowMs);

    /// <summary>
    /// Hunting talk: a line about pulls, kills, the camp, xp or drops. Play test 2026-10-27: "you have the patience of a saint with
    /// these pulls" and "loot is yours, I don't need it" in a group that had not fought at all. Pure.
    /// </summary>
    public static bool IsHuntTalk(string line)
    {
        if (string.IsNullOrEmpty(line))
            return false;
        string text = " " + new string(line.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : ' ').ToArray()) + " ";
        return new[] { " pull", " camp", " xp ", " drop", " loot", " kill", " bubble", " med ", " mob", " adds ", " this spot",
            " the spot", " these ", " sit ", " oom", " endurance", " power " }.Any(text.Contains);
    }

    public sealed class BotState
    {
        public long NextCheck;
        public bool WasLow;
        public bool WasOom;
        public long LastLow = long.MinValue / 2;
        public long LastOom = long.MinValue / 2;
        public long LastCall = long.MinValue / 2;
        public readonly Dictionary<string, long> LastCast = new(StringComparer.Ordinal);
        public object LastHandler;
        /// <summary>A mez, root or stun being cast: called once the target really is held (a resist is not "mezzed").</summary>
        public string PendingCast;
        public GameLiving PendingTarget;
        public long PendingUntil;
    }

    /// <summary>
    /// What the group's fight calls for now, or null: "pull" when it starts, "add" when more enemies join than it had, "wipe"
    /// when half the group has died in it, "kill" or "rest" when it ends (rest more likely when the group is worn). Updates the
    /// state; the caller checks the group gap.
    /// </summary>
    public static string GroupEvent(GroupState state, GroupView view, long now, Random random)
    {
        if (!state.WasFighting && view.Fighting)
        {
            state.WasFighting = true;
            state.MaxEnemies = view.Enemies;
            state.DeadAtStart = view.Members - view.Alive;
            state.WipeCalled = false;
            return view.Enemies >= 1 && random.NextDouble() < 0.35 ? "pull" : null;
        }
        if (state.WasFighting && view.Fighting)
        {
            int dead = view.Members - view.Alive;
            if (!state.WipeCalled && view.Members >= 3 && view.Alive * 2 <= view.Members && dead > state.DeadAtStart)
            {
                state.WipeCalled = true;
                return random.NextDouble() < 0.8 ? "wipe" : null;
            }
            if (view.Enemies > state.MaxEnemies && view.Enemies >= 2)
            {
                state.MaxEnemies = view.Enemies;
                if (now - state.LastAdd < AddGapMs)
                    return null;
                state.LastAdd = now;
                return random.NextDouble() < 0.75 ? "add" : null;
            }
            return null;
        }
        if (state.WasFighting && !view.Fighting)
        {
            state.WasFighting = false;
            state.LastFightEnded = now;
            // Play test 2026-10-27: "Gj." in /group just after the player died to a myrkcat.
            if (state.WipeCalled || view.Alive <= 0 || view.PlayerDown)
                return null;
            double roll = random.NextDouble();
            if (view.Worn)
                return roll < 0.45 ? "rest" : roll < 0.6 ? "kill" : null;
            return roll < 0.3 ? "kill" : roll < 0.4 ? "rest" : null;
        }
        return null;
    }

    /// <summary>
    /// What a bot's own state calls for, or null: "low_hp" as its health drops under 30% in a fight, "oom" as a caster's or
    /// healer's power drops under 15%. Each fires once on the way down and again only after the bot has recovered.
    /// </summary>
    public static string SelfEvent(BotState state, bool inCombat, int healthPercent, int manaPercent, bool usesPower, long now, Random random)
    {
        if (healthPercent >= LowHpResetPercent)
            state.WasLow = false;
        if (manaPercent >= OomResetPercent)
            state.WasOom = false;
        if (inCombat && healthPercent > 0 && healthPercent < LowHpPercent && !state.WasLow)
        {
            state.WasLow = true;
            if (now - state.LastLow >= LowHpGapMs && random.NextDouble() < 0.6)
            {
                state.LastLow = now;
                return "low_hp";
            }
        }
        // In a fight only: a healer rezzed with no power is not "oom, slow down" (review 2026-10-26).
        if (inCombat && usesPower && manaPercent < OomPercent && !state.WasOom)
        {
            state.WasOom = true;
            if (now - state.LastOom >= OomGapMs && random.NextDouble() < 0.7)
            {
                state.LastOom = now;
                return "oom";
            }
        }
        return null;
    }

    /// <summary>The call a spell being cast fits ("mez", "root", "stun", "heal_big", "rez", "taunt"), or null.</summary>
    public static string CastTopic(eSpellType type, eSpellTarget target, double value, int enemies) => type switch
    {
        // The mez lines say "the add": only with more than one enemy.
        eSpellType.Mesmerize when enemies >= 2 => "mez",
        eSpellType.Stun when target is eSpellTarget.ENEMY or eSpellTarget.AREA or eSpellTarget.CONE => "stun",
        eSpellType.SpeedDecrease or eSpellType.UnbreakableSpeedDecrease when value >= 99 => "root",
        eSpellType.Heal when target == eSpellTarget.GROUP => "heal_big",
        eSpellType.Resurrect => "rez",
        eSpellType.Taunt => "taunt",
        _ => null
    };

    /// <summary>The calls that wait until the spell has landed on its target.</summary>
    public static bool NeedsLanding(string topic) => topic is "mez" or "root" or "stun";

    private static readonly System.Text.RegularExpressions.Regex PullWords = new(@"\b(pull|bringing)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    private static readonly System.Text.RegularExpressions.Regex CountWords = new(
        @"\b(\d+|one|two|three|four|five|a couple|a few|big one|single)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>
    /// A pull call fits the fight: the call comes once the fight has started (the player usually pulls), so no "pulling now",
    /// and a line that counts the enemies ("inc 1", "two of them") only when it is that many. Pure.
    /// </summary>
    public static bool PullLineFits(string text, int enemies)
    {
        if (string.IsNullOrEmpty(text) || PullWords.IsMatch(text))
            return false;
        // "another one" is an add, not a count.
        var count = CountWords.Match(System.Text.RegularExpressions.Regex.Replace(text, @"\banother one\b", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase));
        if (!count.Success)
            return true;
        string word = count.Value.ToLowerInvariant();
        int said = word switch
        {
            "one" or "single" or "big one" => 1, "two" => 2, "three" => 3, "four" => 4, "five" => 5,
            "a couple" => 2, "a few" => 3, _ => int.TryParse(word, out int n) ? n : -1,
        };
        return word == "a few" ? enemies >= 3 : said == enemies;
    }

    /// <summary>How likely a bot calls the spell it casts (a rez nearly always, a stun now and then).</summary>
    public static double CastChance(string topic) => topic switch
    {
        "rez" => 0.7,
        "mez" => 0.5,
        "root" => 0.4,
        "heal_big" => 0.35,
        "stun" => 0.3,
        "taunt" => 0.25,
        _ => 0
    };

    /// <summary>True when a call may go out now (the group and bot gaps), and books it.</summary>
    public static bool Gate(GroupState group, BotState bot, string topic, long now)
    {
        if (topic != "wipe" && now - group.LastCall < GroupGapMs)
            return false;
        if (now - bot.LastCall < BotGapMs)
            return false;
        group.LastCall = now;
        bot.LastCall = now;
        return true;
    }

    private static readonly ConditionalWeakTable<Group, GroupState> Groups = new();
    private static readonly ConditionalWeakTable<GameBot, BotState> Bots = new();

    /// <summary>From the bot's think: a bot in the real player's group looks at the fight once a second.</summary>
    public static void Observe(GameBot bot)
    {
        if (!AutonomousBotVoices.Enabled || bot?.Group == null || !bot.IsAutonomousWorldBot || bot.IsTemporaryGroupHelper ||
            bot.DatabaseID <= 0)
            return;
        long now = GameLoop.GameLoopTime;
        BotState self = Bots.GetValue(bot, _ => new BotState());
        if (now < self.NextCheck)
            return;
        self.NextCheck = now + CheckMs;
        try
        {
            Group group = bot.Group;
            if (!AutonomousLivingServer.GroupHasPlayer(group))
                return;
            GroupState state = Groups.GetValue(group, _ => new GroupState());
            GameLiving[] members = group.GetMembersInTheGroup().ToArray();
            int enemies = CountEnemies(members);
            lock (state)
            {
                if (now - state.LastObserved >= CheckMs - 100)
                {
                    state.LastObserved = now;
                    var view = new GroupView(members.Any(m => m.IsAlive && m.InCombat), enemies, members.Length,
                        members.Count(m => m.IsAlive), members.Any(m => m.IsAlive && (m.HealthPercent < 50 || m.MaxMana > 0 && m.ManaPercent < 40)),
                        members.Any(m => m is GamePlayer && !m.IsAlive));
                    string topic = GroupEvent(state, view, now, Random.Shared);
                    if (topic != null)
                        CallForGroup(members, state, topic, now, enemies);
                }
            }
            if (!bot.IsAlive)
                return;
            string own = SelfEvent(self, bot.InCombat, bot.HealthPercent, bot.ManaPercent, UsesPower(bot), now, Random.Shared);
            if (own == null && self.PendingCast != null && (!bot.IsCasting || !ReferenceEquals(bot.CurrentSpellHandler, self.LastHandler)))
            {
                GameLiving target = self.PendingTarget;
                if (target != null && target.IsAlive && Landed(self.PendingCast, target))
                    own = self.PendingCast;
                if (own != null || now > self.PendingUntil)
                    (self.PendingCast, self.PendingTarget) = (null, null);
            }
            if (own == null && bot.IsCasting && bot.CurrentSpellHandler is ISpellHandler handler && !ReferenceEquals(handler, self.LastHandler))
            {
                self.LastHandler = handler;
                Spell spell = handler.Spell;
                string cast = spell == null ? null : CastTopic(spell.SpellType, spell.Target, spell.Value, enemies);
                if (cast != null && (!self.LastCast.TryGetValue(cast, out long last) || now - last >= CastGapMs) &&
                    Random.Shared.NextDouble() < CastChance(cast))
                {
                    self.LastCast[cast] = now;
                    if (NeedsLanding(cast))
                        (self.PendingCast, self.PendingTarget, self.PendingUntil) = (cast, handler.Target, now + 6_000);
                    else
                        own = cast;
                }
            }
            if (own != null)
                lock (state)
                    if (Gate(state, self, own, now))
                        Say(bot, own, enemies);
        }
        catch (Exception exception)
        {
            Log.Warn($"Bot voices: combat call for {bot.Name} failed", exception);
        }
    }

    private static void CallForGroup(GameLiving[] members, GroupState state, string topic, long now, int enemies)
    {
        if (topic != "wipe" && now - state.LastCall < GroupGapMs)
            return;
        GameBot[] bots = members.OfType<GameBot>()
            .Where(b => b.IsAlive && b.IsAutonomousWorldBot && !b.IsTemporaryGroupHelper && b.DatabaseID > 0 && SawTheFight(b, members))
            .OrderBy(_ => Random.Shared.Next()).ToArray();
        if (bots.Length == 0)
            return;
        // The pull and adds are seen first by whoever is in the fight, a tank before anyone.
        IEnumerable<GameBot> order = topic is "pull" or "add"
            ? bots.OrderByDescending(b => b.InCombat).ThenByDescending(IsTank)
            : bots;
        foreach (GameBot speaker in order)
        {
            BotState self = Bots.GetValue(speaker, _ => new BotState());
            // A bot that just spoke leaves the call to the next one (review 2026-10-26: a wipe call was lost that way).
            if (!Gate(state, self, topic, now))
                continue;
            if (Say(speaker, topic, enemies))
                return;
            // Nothing to say for this bot (its role has no line): let the next one try.
            state.LastCall = long.MinValue / 2;
            self.LastCall = long.MinValue / 2;
        }
    }

    /// <summary>A group call comes from a bot that was there. Play test 2026-10-27: a groupmate at the other end of East
    /// Svealand said "assisting you" and "here it comes" while the player fought alone near Mularn.</summary>
    public const int FightHearingRadius = 3_000;

    public static bool SawTheFight(GameBot bot, IEnumerable<GameLiving> members) =>
        SawTheFight(bot.InCombat, members.Where(member => member is GamePlayer && member.CurrentRegion == bot.CurrentRegion)
            .Select(member => (int?)bot.GetDistanceTo(member)).Min());

    /// <summary>The same rule without a world: whether a bot this far from the player (null: another region) saw the fight. Pure.</summary>
    public static bool SawTheFight(bool inCombat, int? distanceToPlayer) =>
        inCombat || distanceToPlayer is { } distance && distance <= FightHearingRadius;

    private static bool Landed(string topic, GameLiving target) => topic switch
    {
        "mez" => target.IsMezzed,
        "stun" => target.IsStunned,
        "root" => target.effectListComponent?.ContainsEffectForEffectType(eEffect.MovementSpeedDebuff) == true,
        _ => false,
    };

    private static bool Say(GameBot bot, string topic, int enemies)
    {
        if (!AutonomousBotVoices.TryLine(bot, "combat", out VC.Picked picked, topic: topic, relax: true,
                filter: topic is "pull" or "add" ? line => PullLineFits(line.Text, enemies) : null))
            return false;
        AutonomousBotGroupChatter.Send(bot, picked.Text, "none");
        return true;
    }

    /// <summary>The distinct live enemies fighting anyone in the group (monsters and enemy players, not pets of the group).</summary>
    private static int CountEnemies(GameLiving[] members)
    {
        var seen = new HashSet<GameLiving>();
        foreach (GameLiving member in members)
        {
            if (member?.attackComponent?.AttackerTracker == null)
                continue;
            foreach (GameLiving attacker in member.attackComponent.AttackerTracker.Attackers)
                if (attacker != null && attacker.IsAlive && !members.Contains(attacker) &&
                    !(attacker is GameNPC npc && npc.Brain is IControlledBrain controlled && members.Contains(controlled.Owner)))
                    seen.Add(attacker);
        }
        return seen.Count;
    }

    private static bool UsesPower(GameBot bot)
    {
        if (bot.CharacterClass == null || bot.MaxMana <= 0)
            return false;
        var cls = (eCharacterClass)bot.CharacterClass.ID;
        return AutonomousBotVoices.IsCasterClass(cls) || BotPartyRoles.IsHealingClass(cls);
    }

    private static bool IsTank(GameBot bot) =>
        bot.CharacterClass != null && BotPartyRoles.For((eCharacterClass)bot.CharacterClass.ID) == BotPartyRole.Tank;
}
