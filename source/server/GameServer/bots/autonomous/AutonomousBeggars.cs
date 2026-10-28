using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Text.RegularExpressions;
using DOL.AI.Brain;
using DOL.Database;
using DOL.GS.PacketHandler;
using DOL.GS.ServerProperties;
using DOL.Logging;

namespace DOL.GS;

/// <summary>
/// Beggars (bot_living_beggars, docs/bot-voices/TALKING-UPDATE.md section 11): about 4% of the Crafter personalities
/// give up adventuring and live in their realm's capital. A beggar keeps a corner beside a merchant, wanders the city
/// begging real players and passing bots, and hoards every copper: it never buys, never upgrades, never spends.
/// <list type="bullet">
/// <item>Every coin and item is real. A player gives by dragging coin or an item onto the beggar (the item moves to
/// the beggar's own saved inventory); a passing bot that gives pays from its own purse; begged items are sold at a
/// merchant or listed on the Realm Exchange by the existing economy code.</item>
/// <item>A player who asks anyone for gold or items, on any channel or with /beg, is marked for an hour: the beggars of
/// their capital that hear of it or see them follow them (no combat), insulting them and claiming their corners, until
/// the player leaves the capital; then they go back to their corners. Coin given to a chasing beggar calms that one.</item>
/// <item>A beggar logs in in its capital, never joins groups, raids or realm forces, and declines invitations.</item>
/// </list>
/// Real players and companion bots are never beggars; only autonomous gamebots are.
/// </summary>
public static class AutonomousBeggars
{
    private static readonly Logger Log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);

    /// <summary>The share of Crafter personalities who are beggars.</summary>
    public const double ShareOfCrafters = 0.04;
    private const long Salt = 0x6265_6767_6172;   // "beggar"

    public const int BegRadius = 900;            // a player this close may be walked up to and begged
    public const int BegReach = 160;             // how close the beggar stands to beg
    public const int BotBegRadius = 350;         // a passing bot this close may be begged
    public const int CornerArrived = 140;
    public const int FollowDistance = 110;
    public const int FollowLeash = 8_000;        // farther than this the beggar walks after the player instead
    public static readonly TimeSpan HostileLimit = TimeSpan.FromMinutes(30);   // a safety stop; leaving the city ends it first
    public static readonly TimeSpan PlayerBegRest = TimeSpan.FromMinutes(4);   // one player is begged at most this often
    public static readonly TimeSpan CalmedRest = TimeSpan.FromMinutes(10);     // a player who paid off a beggar is not begged

    public static bool On => Properties.BOT_LIVING_BEGGARS;

    /// <summary>
    /// Whether a bot id is one of the beggars. On a running server: the realm's share of its own Crafters
    /// (<see cref="ChooseBeggars"/>, from the roster once per run). Without a server (tests): a Crafter whose stable roll is
    /// under the share.
    /// </summary>
    public static bool IsBeggarId(long botId)
    {
        if (botId <= 0)
            return false;
        HashSet<long> chosen = Chosen();
        return chosen != null ? chosen.Contains(botId) : IsBeggarByRoll(botId);
    }

    /// <summary>The roll alone: a Crafter whose stable roll is under the share. Pure.</summary>
    public static bool IsBeggarByRoll(long botId) => botId > 0 &&
        AutonomousBotPersonality.TypeFor(botId) == BotPersonalityType.Crafter &&
        AutonomousBotSchedule.Unit(botId, Salt) < ShareOfCrafters;

    /// <summary>
    /// The beggars of a roster: in each realm, the share of that realm's Crafters (rounded, at least one) with the lowest
    /// stable rolls, so every capital gets its fair number and the same bots beg every run. The roll alone gave Albion one
    /// beggar of 96 Crafters, Midgard two of 117 and Hibernia four of 103 (in-game check 2026-10-27). Pure.
    /// </summary>
    public static HashSet<long> ChooseBeggars(IEnumerable<(long Id, int Realm)> roster)
    {
        var chosen = new HashSet<long>();
        foreach (var realm in roster.Where(bot => bot.Id > 0 && AutonomousBotPersonality.TypeFor(bot.Id) == BotPersonalityType.Crafter)
                     .GroupBy(bot => bot.Realm))
        {
            int count = Math.Max(1, (int)Math.Round(realm.Count() * ShareOfCrafters, MidpointRounding.AwayFromZero));
            foreach (var bot in realm.OrderBy(bot => AutonomousBotSchedule.Unit(bot.Id, Salt)).ThenBy(bot => bot.Id).Take(count))
                chosen.Add(bot.Id);
        }
        return chosen;
    }

    private static HashSet<long> _chosen;
    private static readonly object ChosenSync = new();

    private static HashSet<long> Chosen()
    {
        if (_chosen != null || GameServer.Instance == null || GameServer.Database == null)
            return _chosen;
        lock (ChosenSync)
        {
            if (_chosen != null)
                return _chosen;
            try
            {
                _chosen = ChooseBeggars(GameServer.Database.SelectAllObjects<OfflineWorldBotRecord>()
                    .Where(record => !record.IsRetired).Select(record => (record.BotId, record.Realm)));
                Log.Info($"BEGGARS_CHOSEN count={_chosen.Count}");
            }
            catch (Exception exception)
            {
                Log.Warn("Beggars: could not read the roster, using the roll", exception);
                return null;
            }
            return _chosen;
        }
    }

    /// <summary>An autonomous gamebot that is a beggar while the setting is on.</summary>
    public static bool Is(GameBot bot) => On && bot?.IsAutonomousWorldBot == true && !bot.IsTemporaryGroupHelper &&
                                          !bot.IsEpicSpecialForce && IsBeggarId(bot.DatabaseID);

    /// <summary>A saved record that logs in as a beggar.</summary>
    public static bool IsBeggarRecord(OfflineWorldBotRecord record) => On && record != null && !record.IsRetired && IsBeggarId(record.BotId);

    #region Reading what players say

    // A request for coin or loot: an asking phrase and something of value. "give me a sec" and "can i get an invite" are
    // not; "u want gold?" and "here, take this" are offers.
    private static readonly Regex Wealth = new(
        @"\b(gold|money|coins?|copper|coppers|silver|plat|platinum|cash|change|loot|items?|gear|stuff|armou?r|weapons?|" +
        @"drops?|potions?|pots|anything|something|charity|alms|handouts?|donations?)\b|\b\d+ ?(g|s|c|p)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Asking = new(
        @"\b(spare|gimme|give me|giv me|giveme|gimmie|hand me|toss me|throw me|lend me|loan me|send me|pay me|" +
        @"can i (have|get|borrow)|could i (have|get|borrow)|may i (have|get)|can you (give|spare|lend)|could you (give|spare|lend)|" +
        @"(i|we) (really )?need|(got|have|has) (any|some)\b.*\b(for me|to spare|i can have)|any (spare|extra)|" +
        @"donate|alms|handout|beg(ging)? (you|u|for)|plz.*\b(gold|coin|money)|please.*\b(gold|coin|money)( for me)?\?|share (the|ur|your))",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Offering = new(
        @"\b(you|u|ya|yall) (want|need|wanna)\b|\bhere(s| is| you go| ya go|,)\b|\btake (this|it|some)\b|\bfor you\b|" +
        @"(^|[.,!]\s*)have (some|this|it)\b|\b(you|u) can have\b|\bi('ll| will| can) (give|toss|spare|pay)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>True when a player's line asks for gold or items (and is not an offer). Pure.</summary>
    public static bool AsksForCharity(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return false;
        string text = message.Trim();
        if (Offering.IsMatch(text))
            return false;
        return Asking.IsMatch(text) && Wealth.IsMatch(text);
    }

    /// <summary>A copper amount as players typed it: "3g 25s", "40c", "1p 2g". Pure.</summary>
    public static string CoinText(long copper)
    {
        if (copper <= 0)
            return "0c";
        var parts = new List<string>();
        long mithril = Money.GetMithril(copper), platinum = Money.GetPlatinum(copper), gold = Money.GetGold(copper),
            silver = Money.GetSilver(copper), rest = Money.GetCopper(copper);
        if (mithril > 0) parts.Add(mithril + "m");
        if (platinum > 0) parts.Add(platinum + "p");
        if (gold > 0) parts.Add(gold + "g");
        if (silver > 0) parts.Add(silver + "s");
        if (rest > 0 && parts.Count < 2) parts.Add(rest + "c");
        return string.Join(" ", parts.Take(3));
    }

    /// <summary>
    /// How much a passing bot gives from its own purse: about 1% of it, between 1 copper and 2 silver, never more than
    /// it has. Pure.
    /// </summary>
    public static long DonationFor(long donorPurse, double roll)
    {
        if (donorPurse <= 0)
            return 0;
        long amount = (long)Math.Round(donorPurse * (0.004 + 0.012 * Math.Clamp(roll, 0, 1)));
        return Math.Clamp(amount, 1, Math.Min(200, donorPurse));
    }

    #endregion

    #region State

    private enum Errand { None, Selling, Listing }

    private sealed class State
    {
        public ushort Region;
        public Vector3 Corner;
        public string CornerNpc = "the merchant";
        public Vector3? Spot;                 // where it is begging now (null: the corner)
        public bool OpenerDue;                // heard of it from afar: the "heard" opener waits until it reaches the player
        public long NextLookAt, NextMoveAt, NextLineAt, NextBotBegAt, NextHoardAt, NextErrandAt, NextProgressAt;
        public GamePlayer Approaching;
        public long ApproachUntil;
        public Errand Errand;
        public GameNPC ErrandNpc;
        public GamePlayer Mark;              // the player it is chasing
        public long MarkSince, NextInsultAt;
        public bool CornerTurn;
        public int Stalls;
        public string Activity;
        public readonly Dictionary<string, long> Begged = new(StringComparer.Ordinal);
        public readonly Dictionary<string, long> Calmed = new(StringComparer.Ordinal);
    }

    private static readonly ConcurrentDictionary<long, State> States = new();
    private static readonly ConcurrentDictionary<long, GameBot> Online = new();

    /// <summary>The beggars that have been seen in their capital and are still in the world.</summary>
    private static IEnumerable<GameBot> OnlineBeggars() =>
        Online.Values.Where(bot => bot.ObjectState == GameObject.eObjectState.Active && Is(bot));

    #endregion

    #region Login

    /// <summary>
    /// Before a beggar's login: it starts in its own capital (a beggar's saved spot is a street corner). Called by the
    /// population controller before AddToWorld.
    /// </summary>
    public static bool PlaceAtLogin(GameBot bot, OfflineWorldBotRecord record)
    {
        if (!IsBeggarRecord(record) || bot == null)
            return false;
        // Out of every group, raid and realm force: nothing else may call it to the frontier.
        record.ObjectiveKind = nameof(eAutonomousObjectiveKind.SoloPve);
        AutonomousStuckWatchdog.CapitalLocation capital = AutonomousStuckWatchdog.SafeCapitalFor((eRealm)record.Realm);
        if (capital.RegionId == 0 || bot.CurrentRegionID == capital.RegionId)
            return false;
        bot.CurrentRegionID = capital.RegionId;
        bot.X = capital.X;
        bot.Y = capital.Y;
        bot.Z = capital.Z;
        Log.Info($"BEGGAR_LOGIN bot=\"{record.Name}\" capital={capital.Name}");
        return true;
    }

    #endregion

    #region The beggar's turn

    /// <summary>
    /// The beggar's turn, early in its brain's Think. True when the beggar was handled (the rest of the brain is skipped).
    /// A beggar outside its capital, in a group, dead or fighting is left to the ordinary brain.
    /// </summary>
    public static bool Think(BotBrain brain)
    {
        GameBot bot = brain?.BotBody;
        if (!Is(bot) || bot.ObjectState != GameObject.eObjectState.Active || !bot.IsAlive || bot.InCombat ||
            bot.Group != null || bot.IsOnStableMasterRoute)
            return false;
        AutonomousStuckWatchdog.CapitalLocation capital = AutonomousStuckWatchdog.CapitalFor(bot.Realm);
        if (capital.RegionId == 0 || bot.CurrentRegionID != capital.RegionId)
            return false;

        long now = GameLoop.GameLoopTime;
        State state = States.GetOrAdd(bot.DatabaseID, _ => new State());
        Online[bot.DatabaseID] = bot;
        if (state.Region != bot.CurrentRegionID && !ChooseCorner(bot, state))
            return false;
        brain.ThinkInterval = 1_000 + (int)(bot.ObjectID % 300);
        if (now >= state.NextProgressAt)
        {
            state.NextProgressAt = now + 60_000;
            AutonomousStuckWatchdog.MarkProgress(bot, eAutonomousProgressKind.Movement);
        }

        if (state.Mark == null && now >= state.NextLookAt)
        {
            state.NextLookAt = now + 3_000;
            if (MarkedPlayerInSight(bot, state, now) is { } marked)
                StartChase(bot, marked, "seen", true);
        }
        if (state.Mark != null)
            Chase(bot, state, now);
        else if (state.Errand != Errand.None)
            RunErrand(bot, state, now);
        else
            Beg(bot, state, now);
        return true;
    }

    private static bool ChooseCorner(GameBot bot, State state)
    {
        Region region = bot.CurrentRegion;
        GameNPC[] anchors = Anchors(region, bot.Realm);
        if (anchors.Length == 0)
            return false;
        GameMerchant[] merchants = anchors.OfType<GameMerchant>().ToArray();
        GameNPC[] pool = merchants.Length > 0 ? merchants : anchors;
        GameNPC anchor = pool[(int)(AutonomousBotPersonality.Mix(bot.DatabaseID, 77) % (ulong)pool.Length)];
        state.Region = bot.CurrentRegionID;
        state.Corner = PointNear(anchor, 220) ?? new Vector3(anchor.X, anchor.Y, anchor.Z);
        state.CornerNpc = anchor.Name;
        state.Spot = null;
        state.Errand = Errand.None;
        state.NextMoveAt = GameLoop.GameLoopTime + 120_000 + Random.Shared.Next(120_000);
        Log.Info($"BEGGAR_CORNER bot=\"{bot.Name}\" region={bot.CurrentRegionID} near=\"{anchor.Name}\" at={(int)state.Corner.X},{(int)state.Corner.Y}");
        return true;
    }

    private static readonly ConcurrentDictionary<(ushort, eRealm), (long Until, GameNPC[] Npcs)> AnchorCache = new();

    private static GameNPC[] Anchors(Region region, eRealm realm)
    {
        if (region == null)
            return [];
        long now = GameLoop.GameLoopTime;
        if (AnchorCache.TryGetValue((region.ID, realm), out var cached) && now < cached.Until)
            return cached.Npcs;
        GameNPC[] npcs = region.Objects.OfType<GameNPC>()
            .Where(npc => npc.ObjectState == GameObject.eObjectState.Active && (npc.Realm == realm || npc.Realm == eRealm.None) &&
                          npc is GameMerchant or GameTrainer or RealmExchangeBroker or GameStableMaster)
            .OrderBy(npc => npc.X).ThenBy(npc => npc.Y).ToArray();
        AnchorCache[(region.ID, realm)] = (now + 10 * 60_000, npcs);
        return npcs;
    }

    private static Vector3? PointNear(GameObject anchor, float radius)
    {
        IPathfindingMgr nav = PathfindingProvider.Instance;
        Zone zone = anchor?.CurrentZone;
        if (zone == null || !nav.IsAvailable || !nav.HasNavmesh(zone))
            return null;
        Vector3? point = nav.GetRandomPoint(zone, new Vector3(anchor.X, anchor.Y, anchor.Z), radius, nav.DefaultFilters);
        return point.HasValue && anchor.CurrentRegion.GetZone((int)point.Value.X, (int)point.Value.Y) == zone ? point : null;
    }

    private static short WalkSpeed(GameBot bot) => (short)Math.Max(120, bot.MaxSpeed / 2);

    /// <summary>
    /// Walks toward a point; true once there. A point it cannot reach (no route, or a route that keeps stopping short)
    /// counts as reached after a few tries: it begs where it stands rather than standing still trying forever.
    /// </summary>
    private static bool GoTo(GameBot bot, State state, Vector3 point, int arrived, short speed)
    {
        if (Vector3.DistanceSquared(new Vector3(bot.X, bot.Y, bot.Z), point) <= (float)arrived * arrived)
        {
            state.Stalls = 0;
            if (bot.IsMoving && bot.FollowTarget == null)
                bot.StopMoving();
            return true;
        }
        if (bot.IsMoving)
            return false;
        if (++state.Stalls > 6)
            return true;
        bot.PathTo(point, speed);
        return false;
    }

    /// <summary>Sets a new place to walk to (the corner when null).</summary>
    private static void Head(GameBot bot, State state, Vector3? spot)
    {
        state.Spot = spot;
        state.Stalls = 0;
        if (bot.IsMoving || bot.FollowTarget != null)
            bot.StopMoving();   // also ends a follow
    }

    private static void SetActivity(GameBot bot, State state, string activity, string progress)
    {
        if (state.Activity == activity || bot.PersistentRecord == null)
            return;
        state.Activity = activity;
        bot.PersistentRecord.Activity = activity;
        bot.PersistentRecord.CurrentGoal = $"Beg on the corner by {state.CornerNpc} and never spend a copper";
        bot.PersistentRecord.ObjectiveProgress = progress;
        bot.PersistentRecord.Dirty = true;
        bot.MarkAutonomousStateDirty();
    }

    private static string Hoard(GameBot bot) => CoinText(bot.PersistentRecord?.MoneyCopper ?? 0);

    private static Dictionary<string, string> SlotsFor(GameBot bot, State state, GameLiving other = null, string coin = null, string item = null)
    {
        var slots = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["npc"] = state?.CornerNpc,
            ["hoard"] = Hoard(bot),
        };
        if (other is GamePlayer player)
        {
            slots["player"] = player.Name;
            slots["player_class"] = player.CharacterClass?.Name?.ToLowerInvariant();
        }
        else if (other != null)
            slots["target"] = other.Name;
        if (coin != null)
            slots["coin"] = coin;
        if (item != null)
            slots["item"] = item;
        return slots;
    }

    /// <summary>Says a beggar line in /say (heard only when a player is near).</summary>
    private static bool Say(GameBot bot, string topic, IReadOnlyDictionary<string, string> slots)
    {
        if (!AutonomousLivingServer.CanTalk(bot) || !AutonomousBotVoices.TryEventLine(bot, "beggar", out string line, topic, slots))
            return false;
        AutonomousLivingServer.Speak(bot, AutonomousLivingServer.Channel.Say, line);
        return true;
    }

    private static void Beg(GameBot bot, State state, long now)
    {
        // A player walking past: walk up, beg once, go back (each player at most every four minutes).
        if (state.Approaching != null)
        {
            GamePlayer target = state.Approaching;
            if (target.ObjectState != GameObject.eObjectState.Active || target.CurrentRegionID != bot.CurrentRegionID ||
                now > state.ApproachUntil || !bot.IsWithinRadius(target, BegRadius * 2))
            {
                state.Approaching = null;
                Head(bot, state, state.Spot);
            }
            else if (bot.IsWithinRadius(target, BegReach))
            {
                state.Approaching = null;
                Head(bot, state, state.Spot);
                bot.TurnTo(target);
                bot.Emote(eEmote.Beg);
                Say(bot, "beg", SlotsFor(bot, state, target));
                state.NextLineAt = now + 20_000 + Random.Shared.Next(25_000);
            }
            else if (bot.FollowTarget != target)
                bot.Follow(target, BegReach - 40, BegRadius * 2);
            return;
        }

        if (state.Spot is null && bot.Inventory != null && now >= state.NextErrandAt && StartErrand(bot, state, now))
            return;

        Vector3 here = state.Spot ?? state.Corner;
        if (now >= state.NextMoveAt)
        {
            // Mostly the corner; now and then a tour of the city (another merchant, a trainer, the broker).
            state.NextMoveAt = now + 150_000 + Random.Shared.Next(240_000);
            GameNPC[] anchors = Anchors(bot.CurrentRegion, bot.Realm);
            Head(bot, state, Random.Shared.NextDouble() < 0.45 && anchors.Length > 0
                ? PointNear(anchors[Random.Shared.Next(anchors.Length)], 300)
                : null);
            here = state.Spot ?? state.Corner;
        }
        if (!GoTo(bot, state, here, CornerArrived, WalkSpeed(bot)))
        {
            SetActivity(bot, state, state.Spot == null ? "Walking back to the corner" : "Wandering the city, begging",
                $"Hoard {Hoard(bot)}");
            return;
        }
        SetActivity(bot, state, state.Spot == null ? $"Begging on the corner by {state.CornerNpc}" : "Begging around the city",
            $"Hoard {Hoard(bot)}");

        if (now < state.NextLineAt)
            return;
        state.NextLineAt = now + 8_000 + Random.Shared.Next(8_000);

        GamePlayer mark = PlayerToBeg(bot, state, now);
        if (mark != null)
        {
            state.Begged[mark.Name] = now;
            state.Approaching = mark;
            state.ApproachUntil = now + 45_000;
            return;
        }
        if (now >= state.NextBotBegAt && TryBegBot(bot, state, now))
            return;
        if (!AutonomousLivingServer.PlayerHears(bot))
            return;
        if (now >= state.NextHoardAt && Random.Shared.NextDouble() < 0.35)
        {
            state.NextHoardAt = now + 240_000 + Random.Shared.Next(360_000);
            Say(bot, "hoard", SlotsFor(bot, state));
            return;
        }
        if (Random.Shared.NextDouble() < 0.25)
        {
            if (Random.Shared.Next(3) == 0)
                bot.Emote(eEmote.Beg);
            Say(bot, "beg_idle", SlotsFor(bot, state));
            state.NextLineAt = now + 45_000 + Random.Shared.Next(60_000);
        }
    }

    private static GamePlayer PlayerToBeg(GameBot bot, State state, long now)
    {
        long rest = (long)PlayerBegRest.TotalMilliseconds;
        long calmed = (long)CalmedRest.TotalMilliseconds;
        return bot.GetPlayersInRadius(BegRadius).OfType<GamePlayer>()
            .Where(player => player.ObjectState == GameObject.eObjectState.Active && player.IsAlive && player.Realm == bot.Realm &&
                             !player.IsStealthed && player.Client?.Account?.PrivLevel <= 1 &&
                             (!state.Begged.TryGetValue(player.Name, out long last) || now - last >= rest) &&
                             (!state.Calmed.TryGetValue(player.Name, out long paid) || now - paid >= calmed))
            .OrderBy(player => player.GetDistanceTo(bot))
            .FirstOrDefault();
    }

    /// <summary>Begs a passing bot. One in three gives a little from its own purse (a real transfer); the rest say no.</summary>
    private static bool TryBegBot(GameBot bot, State state, long now)
    {
        GameBot donor = bot.GetNPCsInRadius(BotBegRadius).OfType<GameBot>()
            .FirstOrDefault(other => other != bot && other.IsAutonomousWorldBot && !other.IsTemporaryGroupHelper && other.IsAlive &&
                                     other.Realm == bot.Realm && !Is(other) && !other.InCombat && other.DatabaseID > 0);
        if (donor == null)
            return false;
        state.NextBotBegAt = now + 120_000 + Random.Shared.Next(180_000);
        bool heard = AutonomousLivingServer.PlayerHears(bot);
        if (heard)
            Say(bot, "beg_bot", SlotsFor(bot, state, donor));
        bool gives = Random.Shared.NextDouble() < 0.34;
        long amount = gives ? DonationFor(AutonomousBotEconomy.GetMoney(donor.DatabaseID), Random.Shared.NextDouble()) : 0;
        int delay = 3_000 + Random.Shared.Next(3_000);
        AutonomousLivingServer.Later(donor, delay, () => donor.IsAlive && bot.ObjectState == GameObject.eObjectState.Active, () =>
        {
            if (amount > 0 && AutonomousBotEconomy.TrySpend(donor.DatabaseID, amount))
            {
                if (!AutonomousBotEconomy.AddMoney(bot.DatabaseID, amount))
                {
                    AutonomousBotEconomy.AddMoney(donor.DatabaseID, amount);
                    return;
                }
                Log.Info($"BEGGAR_GIFT from_bot=\"{donor.Name}\" beggar=\"{bot.Name}\" copper={amount}");
                if (!heard)
                    return;
                if (AutonomousBotVoices.TryEventLine(donor, "beggar", out string given, "donor_give", SlotsFor(donor, null, bot)))
                    AutonomousLivingServer.Speak(donor, AutonomousLivingServer.Channel.Say, given);
                AutonomousLivingServer.Later(bot, 2_000 + Random.Shared.Next(2_000), null, () =>
                    Say(bot, "thanks_bot", SlotsFor(bot, state, donor, CoinText(amount))));
                return;
            }
            if (!heard || Random.Shared.NextDouble() < 0.4)
                return;
            if (AutonomousBotVoices.TryEventLine(donor, "beggar", out string no, "donor_refuse", SlotsFor(donor, null, bot)))
                AutonomousLivingServer.Speak(donor, AutonomousLivingServer.Channel.Say, no);
            if (Random.Shared.NextDouble() < 0.6)
                AutonomousLivingServer.Later(bot, 2_000 + Random.Shared.Next(2_500), null, () =>
                {
                    if (Random.Shared.Next(2) == 0)
                        bot.Emote(eEmote.Rude);
                    Say(bot, "spurned", SlotsFor(bot, state, donor));
                });
        });
        return true;
    }

    #endregion

    #region Selling what it was given

    /// <summary>
    /// With something worth selling in the pack, the beggar walks to the Realm Exchange (a valuable item) or a merchant
    /// (vendor trash), using the economy's own rules for what may be sold. Checked every few minutes from the corner.
    /// </summary>
    private static bool StartErrand(GameBot bot, State state, long now)
    {
        state.NextErrandAt = now + 180_000 + Random.Shared.Next(120_000);
        GameNPC[] anchors = Anchors(bot.CurrentRegion, bot.Realm);
        if (AutonomousBotEconomy.FindValuableListingCandidate(bot) != null &&
            AutonomousBotEconomy.FindFreeListingSlot(AutonomousBotEconomy.GetOwnerId(bot.DatabaseID)) >= 0 &&
            Nearest<RealmExchangeBroker>(bot, anchors) is { } broker && broker.Realm == bot.Realm)
        {
            state.Errand = Errand.Listing;
            state.ErrandNpc = broker;
            Head(bot, state, null);
            return true;
        }
        if (AutonomousBotEconomy.FindVendorTrashCandidate(bot) != null && Nearest<GameMerchant>(bot, anchors) is { } merchant)
        {
            state.Errand = Errand.Selling;
            state.ErrandNpc = merchant;
            Head(bot, state, null);
            return true;
        }
        return false;
    }

    private static T Nearest<T>(GameBot bot, GameNPC[] anchors) where T : GameNPC =>
        anchors.OfType<T>().Where(npc => npc.ObjectState == GameObject.eObjectState.Active)
            .OrderBy(npc => npc.GetDistanceTo(bot)).FirstOrDefault();

    private static void RunErrand(GameBot bot, State state, long now)
    {
        GameNPC npc = state.ErrandNpc;
        if (npc == null || npc.ObjectState != GameObject.eObjectState.Active || npc.CurrentRegionID != bot.CurrentRegionID)
        {
            EndErrand(bot, state);
            return;
        }
        int reach = Math.Max(100, Properties.WORLD_PICKUP_DISTANCE - 40);
        if (!GoTo(bot, state, new Vector3(npc.X, npc.Y, npc.Z), reach, WalkSpeed(bot)))
        {
            SetActivity(bot, state, state.Errand == Errand.Listing ? "Taking begged loot to the Realm Exchange" : "Taking begged loot to a merchant",
                $"Hoard {Hoard(bot)}");
            return;
        }
        // A merchant it could not reach: no selling from across the city.
        if (!bot.IsWithinRadius(npc, Properties.WORLD_PICKUP_DISTANCE))
        {
            EndErrand(bot, state);
            return;
        }
        if (now < state.NextLineAt)
            return;
        state.NextLineAt = now + 3_000 + Random.Shared.Next(2_000);
        bot.TurnTo(npc);
        if (state.Errand == Errand.Listing)
        {
            AutonomousBotEconomy.ListingCandidate candidate = AutonomousBotEconomy.FindValuableListingCandidate(bot);
            int price = candidate == null ? 0 : AutonomousBotEconomy.RollListingPrice(candidate.Item);
            if (candidate != null && AutonomousBotEconomy.TryList(bot, candidate.Item, price))
            {
                Log.Info($"BEGGAR_LISTED bot=\"{bot.Name}\" item=\"{candidate.Item.Name}\" copper={price}");
                if (Random.Shared.NextDouble() < 0.5)
                    Say(bot, "listed", SlotsFor(bot, state, null, CoinText(price), candidate.Item.Name.ToLowerInvariant()));
                return;
            }
            // Nothing (more) to list: sell the trash on the way back, or go home.
            GameMerchant merchant = AutonomousBotEconomy.FindVendorTrashCandidate(bot) != null
                ? Nearest<GameMerchant>(bot, Anchors(bot.CurrentRegion, bot.Realm)) : null;
            if (merchant == null)
            {
                EndErrand(bot, state);
                return;
            }
            state.Errand = Errand.Selling;
            state.ErrandNpc = merchant;
            Head(bot, state, null);
            return;
        }
        DbInventoryItem trash = AutonomousBotEconomy.FindVendorTrashCandidate(bot);
        if (trash != null && npc is GameMerchant seller && AutonomousBotEconomy.TrySellToVendor(bot, seller, trash, out long copper))
        {
            Log.Info($"BEGGAR_SOLD bot=\"{bot.Name}\" item=\"{trash.Name}\" copper={copper}");
            if (Random.Shared.NextDouble() < 0.4)
            {
                Dictionary<string, string> slots = SlotsFor(bot, state, null, CoinText(copper), trash.Name.ToLowerInvariant());
                slots["npc"] = npc.Name;
                Say(bot, "sold", slots);
            }
            return;
        }
        EndErrand(bot, state);
    }

    private static void EndErrand(GameBot bot, State state)
    {
        state.Errand = Errand.None;
        state.ErrandNpc = null;
        Head(bot, state, null);
    }

    #endregion

    #region Gifts (players drag coin or items onto a beggar)

    /// <summary>A player drags an item onto a beggar: the item moves into the beggar's own saved inventory.</summary>
    public static bool TryReceiveItem(GameBot bot, GamePlayer player, DbInventoryItem item)
    {
        if (!Is(bot) || player == null || item == null || !bot.IsAlive || bot.Inventory == null || player.Inventory == null)
            return false;
        if (!item.IsTradable || item.OwnerID != player.InternalID ||
            item.SlotPosition is < (int)eInventorySlot.FirstBackpack or > (int)eInventorySlot.LastBackpack)
            return false;
        State state = States.GetOrAdd(bot.DatabaseID, _ => new State());
        eInventorySlot slot = bot.Inventory.FindFirstEmptySlot(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack);
        if (slot == eInventorySlot.Invalid)
        {
            Say(bot, "full", SlotsFor(bot, state, player));
            return false;
        }
        int fromSlot = item.SlotPosition;
        string name = item.GetName(0, false);
        int count = item.Count;
        lock (AutonomousBotStatusPersistence.DatabaseWriteLock)
        {
            if (!player.Inventory.RemoveItemWithoutDbDeletion(item))
                return false;
            if (!bot.Inventory.AddItem(slot, item))
            {
                player.Inventory.AddItem((eInventorySlot)fromSlot, item);
                return false;
            }
            item.OwnerID = AutonomousBotEconomy.GetOwnerId(bot.DatabaseID);
        }
        InventoryLogging.LogInventoryAction(player, bot, eInventoryActionType.Trade, item.Template, count);
        AutonomousBotEconomy.MarkInventoryChanged(bot);
        bot.MarkAutonomousStateDirty();
        AutonomousBotStatusPersistence.Queue(bot, true);
        player.Out.SendMessage($"You give {name} to {bot.Name}.", eChatType.CT_System, eChatLoc.CL_SystemWindow);
        Log.Info($"BEGGAR_GIFT from_player=\"{player.Name}\" beggar=\"{bot.Name}\" item=\"{item.Name}\" count={count}");
        Thank(bot, state, player, "thanks_item", SlotsFor(bot, state, player, item: name.ToLowerInvariant()));
        return true;
    }

    /// <summary>A player drags coin onto a beggar: it moves from the player's purse into the beggar's saved purse.</summary>
    public static bool TryReceiveMoney(GameBot bot, GamePlayer player, long copper)
    {
        if (!Is(bot) || player == null || copper <= 0 || !bot.IsAlive)
            return false;
        if (!player.RemoveMoney(copper))
            return false;
        if (!AutonomousBotEconomy.AddMoney(bot.DatabaseID, copper))
        {
            player.AddMoney(copper);
            return false;
        }
        InventoryLogging.LogInventoryAction(player, bot, eInventoryActionType.Trade, copper);
        player.Out.SendMessage($"You give {Money.GetString(copper)} to {bot.Name}.", eChatType.CT_System, eChatLoc.CL_SystemWindow);
        Log.Info($"BEGGAR_GIFT from_player=\"{player.Name}\" beggar=\"{bot.Name}\" copper={copper}");
        State state = States.GetOrAdd(bot.DatabaseID, _ => new State());
        if (state.Mark == player)
        {
            // Paid off: the chase ends (a coin buys a truce, never the corner).
            EndChase(bot, state, null);
            state.Calmed[player.Name] = GameLoop.GameLoopTime;
            Thank(bot, state, player, "calm", SlotsFor(bot, state, player, CoinText(copper)));
            return true;
        }
        Thank(bot, state, player, "thanks_coin", SlotsFor(bot, state, player, CoinText(copper)));
        return true;
    }

    private static void Thank(GameBot bot, State state, GamePlayer player, string topic, Dictionary<string, string> slots)
    {
        AutonomousLivingServer.Later(bot, 1_200 + Random.Shared.Next(1_500), () => AutonomousLivingServer.CanTalk(bot), () =>
        {
            bot.TurnTo(player);
            bot.Emote(topic == "calm" ? eEmote.Shrug : Random.Shared.Next(2) == 0 ? eEmote.Bow : eEmote.Cheer);
            Say(bot, topic, slots);
        });
    }

    #endregion

    #region Asked for coin: the chase

    /// <summary>
    /// Any player line on any channel (/say, /faction, /group, /guild, /lfg, /trade, /yell, /broadcast, a tell). One that
    /// asks anyone for gold or items marks the player for <see cref="MarkTime"/>: begging is the beggars' job. Beggars in
    /// earshot turn on the player at once; the others in the city hear about it within a minute; and any beggar that sees a
    /// marked player in its capital later goes after them. True when a beggar heard it in person (the ordinary /say
    /// replies are skipped).
    /// </summary>
    public static bool OnPlayerAsked(GamePlayer player, string message, string how)
    {
        if (!On || player == null || !AsksForCharity(message))
            return false;
        return MarkAsker(player, how, null);
    }

    /// <summary>A player's /say (OnPlayerAsked on the local channel).</summary>
    public static bool OnPlayerSay(GamePlayer player, string message) => OnPlayerAsked(player, message, "say");

    /// <summary>A player's tell to a gamebot. Asking anyone for coin marks the player; a tell to a beggar is the beggar's.</summary>
    public static bool OnPlayerTell(GamePlayer player, GameBot bot, string message)
    {
        if (!On || player == null)
            return false;
        if (AsksForCharity(message))
            MarkAsker(player, "tell", Is(bot) ? bot : null);
        if (!Is(bot))
            return false;
        if (AsksForCharity(message))
            return true;
        State state = States.GetOrAdd(bot.DatabaseID, _ => new State());
        AutonomousLivingServer.Later(bot, 2_500 + Random.Shared.Next(3_000), () => AutonomousLivingServer.IsSpeaker(bot), () =>
        {
            // A tell about grouping gets "no groups"; anything else a word from the corner.
            string topic = Regex.IsMatch(message, @"\b(group|grp|grouping|invite|inv|join|duo|party|pt)\b", RegexOptions.IgnoreCase) ? "tell_group" : "tell";
            if (AutonomousBotVoices.TryEventLine(bot, "beggar", out string line, topic, SlotsFor(bot, state, player)))
                AutonomousLivingServer.Tell(bot, player, line);
        });
        return true;
    }

    /// <summary>A player's /beg, at anyone or at no one: that is asking for coin.</summary>
    public static void OnPlayerBegs(GamePlayer player, GameObject target)
    {
        if (On && player != null)
            MarkAsker(player, "beg emote", target is GameBot bot && Is(bot) ? bot : null);
    }

    /// <summary>How long a player who asked anyone for coin or items stays fair game for every beggar of the realm.</summary>
    public static readonly TimeSpan MarkTime = TimeSpan.FromHours(1);
    public const int SpotRadius = 1_500;          // a beggar notices a marked player this close
    private static readonly ConcurrentDictionary<string, long> Marked = new(StringComparer.Ordinal);

    private static string MarkKey(GamePlayer player) => $"{(int)player.Realm}|{player.Name}";

    /// <summary>True while a player who asked for coin or items is fair game (the mark lasts an hour from the last ask).</summary>
    public static bool IsMarked(GamePlayer player, long now) =>
        player != null && Marked.TryGetValue(MarkKey(player), out long until) && now < until;

    private static bool MarkAsker(GamePlayer player, string how, GameBot asked)
    {
        long now = GameLoop.GameLoopTime;
        Marked[MarkKey(player)] = now + (long)MarkTime.TotalMilliseconds;
        Log.Info($"BEGGAR_MARKED player=\"{player.Name}\" how={how}");
        if (player.CurrentRegion?.IsCapitalCity != true)
            return false;
        bool heard = false;
        foreach (GameBot bot in OnlineBeggars().Where(bot => bot.Realm == player.Realm && bot.CurrentRegionID == player.CurrentRegionID).ToArray())
        {
            // Asked to its face: the beggar asked by tell or /beg, or any beggar in earshot of a /say or a /beg.
            bool inPerson = bot == asked || how is "say" or "beg emote" && bot.IsWithinRadius(player, WorldMgr.SAY_DISTANCE);
            if (inPerson)
            {
                heard |= StartChase(bot, player, how, false);
                continue;
            }
            // Word travels fast on the street.
            AutonomousLivingServer.Later(bot, 5_000 + Random.Shared.Next(40_000),
                () => ChasedBy(bot) == null && player.ObjectState == GameObject.eObjectState.Active && player.CurrentRegionID == bot.CurrentRegionID,
                () => StartChase(bot, player, "word of " + how, true));
        }
        return heard;
    }

    private static bool StartChase(GameBot bot, GamePlayer player, string how, bool heard)
    {
        if (!bot.IsAlive || bot.InCombat || bot.Group != null || player.CurrentRegion?.IsCapitalCity != true ||
            player.CurrentRegionID != bot.CurrentRegionID || AutonomousStuckWatchdog.CapitalFor(bot.Realm).RegionId != bot.CurrentRegionID)
            return false;
        State state = States.GetOrAdd(bot.DatabaseID, _ => new State());
        Online[bot.DatabaseID] = bot;
        long now = GameLoop.GameLoopTime;
        if (state.Mark == player)
        {
            state.NextInsultAt = Math.Min(state.NextInsultAt, now + 1_500);
            return true;
        }
        state.Mark = player;
        state.MarkSince = now;
        state.NextInsultAt = now + 6_000 + Random.Shared.Next(3_000);
        state.Approaching = null;
        state.Errand = Errand.None;
        bot.StopMoving();
        bot.TurnTo(player);
        SetActivity(bot, state, $"Chasing {player.Name} off the corner", $"{player.Name} asked for coin or items ({how})");
        Log.Info($"BEGGAR_HOSTILE beggar=\"{bot.Name}\" player=\"{player.Name}\" how={how}");
        // Asked in person: outraged at once. Heard of it on the street: the opener waits until it has walked over (Chase), or
        // the player would never hear it.
        state.OpenerDue = heard;
        if (!heard)
            AutonomousLivingServer.Later(bot, 900 + Random.Shared.Next(1_200), () => state.Mark == player, () =>
            {
                bot.Emote(Random.Shared.Next(2) == 0 ? eEmote.Angry : eEmote.Roar);
                Say(bot, "outraged", SlotsFor(bot, state, player));
            });
        return true;
    }

    /// <summary>A marked player this beggar can see in its city (not one that paid it off or that it gave up on lately).</summary>
    private static GamePlayer MarkedPlayerInSight(GameBot bot, State state, long now)
    {
        long rest = (long)CalmedRest.TotalMilliseconds;
        return bot.GetPlayersInRadius(SpotRadius).OfType<GamePlayer>()
            .Where(player => player.ObjectState == GameObject.eObjectState.Active && player.Realm == bot.Realm && IsMarked(player, now) &&
                             (!state.Calmed.TryGetValue(player.Name, out long calmed) || now - calmed >= rest))
            .OrderBy(player => player.GetDistanceTo(bot))
            .FirstOrDefault();
    }

    private static void Chase(GameBot bot, State state, long now)
    {
        GamePlayer player = state.Mark;
        bool gone = player == null || player.ObjectState != GameObject.eObjectState.Active || player.CurrentRegionID != bot.CurrentRegionID;
        if (gone || now - state.MarkSince > (long)HostileLimit.TotalMilliseconds)
        {
            // Given up after the limit: this beggar leaves them be for a while even if they are still fair game.
            if (!gone)
                state.Calmed[player.Name] = now;
            EndChase(bot, state, gone ? player : null);
            return;
        }
        if (!bot.IsWithinRadius(player, FollowLeash))
        {
            bot.StopFollowing();
            if (!bot.IsMoving)
                bot.PathTo(new Vector3(player.X, player.Y, player.Z), bot.MaxSpeed);
        }
        else if (bot.FollowTarget != player)
            bot.Follow(player, FollowDistance, FollowLeash + 2_000);

        if (state.OpenerDue && bot.IsWithinRadius(player, WorldMgr.SAY_DISTANCE))
        {
            state.OpenerDue = false;
            state.NextInsultAt = now + 6_000 + Random.Shared.Next(3_000);
            bot.TurnTo(player);
            bot.Emote(Random.Shared.Next(2) == 0 ? eEmote.Angry : eEmote.Roar);
            Say(bot, "heard", SlotsFor(bot, state, player));
            return;
        }
        if (now < state.NextInsultAt)
            return;
        state.NextInsultAt = now + 6_000 + Random.Shared.Next(7_000);
        if (state.OpenerDue || !bot.IsWithinRadius(player, WorldMgr.SAY_DISTANCE))
            return;
        state.CornerTurn = !state.CornerTurn && Random.Shared.NextDouble() < 0.45;
        if (Random.Shared.NextDouble() < 0.35)
            bot.Emote(Random.Shared.Next(4) switch { 0 => eEmote.Rude, 1 => eEmote.Taunt, 2 => eEmote.ThrowDirt, _ => eEmote.Point });
        Say(bot, state.CornerTurn ? "corner" : "insult", SlotsFor(bot, state, player));
    }

    private static void EndChase(GameBot bot, State state, GamePlayer left)
    {
        GamePlayer was = state.Mark;
        state.Mark = null;
        Head(bot, state, null);
        state.NextMoveAt = GameLoop.GameLoopTime + 120_000 + Random.Shared.Next(120_000);
        state.NextLineAt = GameLoop.GameLoopTime + 15_000;
        Log.Info($"BEGGAR_CHASE_END beggar=\"{bot.Name}\" player=\"{was?.Name}\" reason={(left != null ? "left the city" : "stopped")}");
        if (left != null)
            AutonomousLivingServer.Later(bot, 1_500 + Random.Shared.Next(2_000), () => AutonomousLivingServer.CanTalk(bot), () =>
            {
                bot.Emote(eEmote.Victory);
                Say(bot, "gone", SlotsFor(bot, state, left));
            });
    }

    /// <summary>The player a beggar is chasing, or null (tests and the dashboard).</summary>
    public static GamePlayer ChasedBy(GameBot bot) =>
        bot != null && States.TryGetValue(bot.DatabaseID, out State state) ? state.Mark : null;

    /// <summary>Forgets a beggar that left the world.</summary>
    public static void Forget(GameBot bot)
    {
        if (bot == null)
            return;
        States.TryRemove(bot.DatabaseID, out _);
        Online.TryRemove(bot.DatabaseID, out _);
    }

    #endregion
}
