using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading;
using DOL.GS.PacketHandler;
using DOL.GS.ServerProperties;

namespace DOL.GS
{
    /// <summary>Where an idle autonomous gamebot is standing around.</summary>
    public enum BodySpot { Town, Waiting, Resting }

    public enum BodyMove { None, Look, Emote, Shuffle, Greet }

    /// <summary>One small idle action. <see cref="HeadingOffset"/> is in game heading units (4096 a full turn).</summary>
    public readonly record struct BodyChoice(BodyMove Move, eEmote Emote = 0, int HeadingOffset = 0, double StepAngle = 0, int StepLength = 0);

    /// <summary>
    /// Living Server body language (bot_living_body_language): an autonomous gamebot standing around in town,
    /// waiting for its group or resting between pulls looks around, emotes now and then, takes a step or two in
    /// town, turns and waves at a real player of its realm who walks up, and cheers a ding nearby. Everything is
    /// done with what the game already sends for an NPC (heading, emote animation, a short walk), only while a
    /// real player can see the bot, and never in a fight. Bots can't jump or sit: the game's NPC updates have no
    /// such state. Companion bots and real players are never changed.
    /// </summary>
    public static class AutonomousBotBodyLanguage
    {
        public static bool On => Properties.BOT_LIVING_BODY_LANGUAGE;

        /// <summary>Game heading units in a full turn.</summary>
        public const int FullTurn = 4096;
        /// <summary>A real player this close (and of the bot's realm) gets a wave the first time the bot sees them.</summary>
        public const int GreetRadius = 650;
        /// <summary>A bot doesn't greet the same player again within this time.</summary>
        public const long GreetCooldownMs = 20 * 60 * 1000;
        /// <summary>Town steps stay this close to the bot's chosen idle spot (the town break returns it from 96).</summary>
        public const int ShuffleRadius = 60;
        public const int ShuffleMinimumStep = 25;
        public const long ShuffleMaximumMs = 3_000;

        // Animations the new client builds (emote ids 1-46) that read as idle or friendly.
        public static readonly eEmote[] TownEmotes =
        [
            eEmote.Ponder, eEmote.Ponder, eEmote.Ponder, eEmote.Shrug, eEmote.Shrug, eEmote.Laugh, eEmote.Laugh,
            eEmote.Yes, eEmote.No, eEmote.Point, eEmote.Flex, eEmote.Clap, eEmote.Cheer, eEmote.Wave, eEmote.Beckon,
            eEmote.Salute, eEmote.Drink, eEmote.Dance,
        ];
        public static readonly eEmote[] WaitEmotes =
        [
            eEmote.Ponder, eEmote.Ponder, eEmote.Shrug, eEmote.Shrug, eEmote.LetsGo, eEmote.Point, eEmote.Yes,
            eEmote.Flex, eEmote.Beckon,
        ];
        public static readonly eEmote[] GreetEmotes = [eEmote.Wave, eEmote.Wave, eEmote.Wave, eEmote.Bow, eEmote.Salute];
        public static readonly eEmote[] CheerEmotes = [eEmote.Cheer, eEmote.Cheer, eEmote.Clap, eEmote.Victory];

        private sealed class BodyState
        {
            public long NextActTick;
            public long ShuffleUntilTick;
            public long StillSinceTick;
            public float LastX, LastY;
            public readonly Dictionary<string, long> Greeted = new(StringComparer.OrdinalIgnoreCase);
        }

        // Weak keys: a bot that logs out takes its state with it.
        private static readonly ConditionalWeakTable<GameBot, BodyState> States = new();

        private static readonly DOL.Logging.Logger Log = DOL.Logging.LoggerManager.Create(typeof(AutonomousBotBodyLanguage));
        // What was done, written to the server log every 5 minutes it is non-zero (BOT_BODY_LANGUAGE), so a play
        // session can be checked from the log.
        private static long _looks, _emotes, _steps, _greets, _cheers, _nextReportTick;
        public const long ReportIntervalMs = 5 * 60_000;

        private static void Count(ref long counter)
        {
            Interlocked.Increment(ref counter);
            long now = GameLoop.GameLoopTime;
            long next = Volatile.Read(ref _nextReportTick);
            if (now < next || Interlocked.CompareExchange(ref _nextReportTick, now + ReportIntervalMs, next) != next)
                return;
            if (next == 0)
                return;   // the first call only starts the clock
            long looks = Interlocked.Exchange(ref _looks, 0), emotes = Interlocked.Exchange(ref _emotes, 0),
                steps = Interlocked.Exchange(ref _steps, 0), greets = Interlocked.Exchange(ref _greets, 0),
                cheers = Interlocked.Exchange(ref _cheers, 0);
            if (Log.IsInfoEnabled)
                Log.Info($"BOT_BODY_LANGUAGE minutes={ReportIntervalMs / 60_000} looks={looks} emotes={emotes} steps={steps} greets={greets} cheers={cheers}");
        }

        /// <summary>Milliseconds until a bot's next idle action; <paramref name="roll"/> is in [0, 1).</summary>
        public static long NextDelayMs(BodySpot spot, double roll)
        {
            roll = Math.Clamp(roll, 0, 0.999999);
            return spot switch
            {
                BodySpot.Town => 8_000 + (long)(roll * 17_000),
                BodySpot.Waiting => 10_000 + (long)(roll * 20_000),
                _ => 12_000 + (long)(roll * 18_000),
            };
        }

        /// <summary>
        /// The next idle action from three rolls in [0, 1). A due greeting usually wins; town has looks, emotes and
        /// steps; a wait has looks and emotes; resting is mostly looking around. A resting bot never steps (a step
        /// would wake its rest).
        /// </summary>
        public static BodyChoice Choose(BodySpot spot, bool greetDue, bool resting, double r1, double r2, double r3,
            bool allowStep = true)
        {
            r1 = Math.Clamp(r1, 0, 0.999999);
            r2 = Math.Clamp(r2, 0, 0.999999);
            r3 = Math.Clamp(r3, 0, 0.999999);
            if (greetDue && r1 < 0.6)
                return new(BodyMove.Greet, Pick(GreetEmotes, r2));
            BodyChoice look = new(BodyMove.Look, HeadingOffset: LookOffset(r2, r3));
            switch (spot)
            {
                case BodySpot.Town:
                    // Mostly looking around; an emote about every minute and a half per bot. The first night (2026-10-19)
                    // with a 55% emote share the Camelot Exchange saw 2,303 emotes in 7 hours: too busy to read as people.
                    if (r1 < 0.6 || resting) return look;
                    if (r1 < 0.8 || !allowStep) return r1 < 0.8 ? new(BodyMove.Emote, Pick(TownEmotes, r2)) : look;
                    return new(BodyMove.Shuffle, StepAngle: r2 * 2 * Math.PI, StepLength: ShuffleMinimumStep + (int)(r3 * 35));
                case BodySpot.Waiting:
                    return r1 < 0.75 ? look : new(BodyMove.Emote, Pick(WaitEmotes, r2));
                default:
                    return r1 < 0.85 ? look : new(BodyMove.None);
            }
        }

        /// <summary>A turn of 30-150 degrees to the left or right, in heading units.</summary>
        public static int LookOffset(double r2, double r3)
        {
            int degrees = 30 + (int)(Math.Clamp(r2, 0, 0.999999) * 120);
            int units = degrees * FullTurn / 360;
            return r3 < 0.5 ? -units : units;
        }

        public static ushort Turned(ushort heading, int offset) => (ushort)(((heading + offset) % FullTurn + FullTurn) % FullTurn);

        /// <summary>A step from <paramref name="from"/> that stays within <see cref="ShuffleRadius"/> of the idle spot.</summary>
        public static Vector3 StepTarget(Vector3 anchor, Vector3 from, double angle, int length)
        {
            Vector3 step = from + new Vector3((float)(Math.Cos(angle) * length), (float)(Math.Sin(angle) * length), 0);
            Vector2 offset = new(step.X - anchor.X, step.Y - anchor.Y);
            if (offset.Length() > ShuffleRadius)
            {
                // Back toward the spot instead: a bot drifting off its spot comes back to it.
                offset = Vector2.Normalize(offset) * ShuffleRadius * 0.5f;
                step = new(anchor.X + offset.X, anchor.Y + offset.Y, anchor.Z);
            }
            return step;
        }

        internal static bool Eligible(GameBot bot) =>
            bot != null && bot.IsAutonomousWorldBot && !bot.IsTemporaryGroupHelper && bot.DatabaseID > 0 &&
            bot.ObjectState == GameObject.eObjectState.Active && bot.IsAlive && !bot.InCombat && !bot.IsAttacking &&
            !bot.IsCasting && !bot.IsStealthed && !bot.IsOnStableMasterRoute && !bot.IsCrowdControlled;

        /// <summary>
        /// One idle turn of a bot standing at <paramref name="anchor"/>. Returns true while a town step it started is
        /// still walking, so the caller doesn't stop it.
        /// </summary>
        public static bool Idle(GameBot bot, BodySpot spot, Vector3 anchor) => Act(bot, spot, anchor, allowStep: true);

        /// <summary>A bot standing still this long, with nothing to fight, counts as standing around.</summary>
        public const long StandingStillMs = 5_000;

        /// <summary>
        /// Every turn of every autonomous gamebot: one that has stood still for <see cref="StandingStillMs"/> with no
        /// enemy targeted (at a merchant, the Realm Exchange, a trainer, while planning its next goal or waiting
        /// anywhere) looks around and emotes now and then. It never steps here; only a town break steps.
        /// </summary>
        public static void Ambient(GameBot bot, bool hasAggro)
        {
            if (!On || hasAggro || !Eligible(bot))
                return;
            try
            {
                BodyState state = States.GetValue(bot, _ => new BodyState());
                long now = GameLoop.GameLoopTime;
                bool fighting = bot.TargetObject is GameLiving target && target != bot && target.IsAlive && target.Realm != bot.Realm;
                float dx = bot.X - state.LastX, dy = bot.Y - state.LastY;
                if (bot.IsMoving || fighting || dx * dx + dy * dy > 24 * 24)
                {
                    state.StillSinceTick = now;
                    state.LastX = bot.X;
                    state.LastY = bot.Y;
                    return;
                }
                if (now - state.StillSinceTick < StandingStillMs)
                    return;
                Act(bot, bot.CurrentRegion?.IsCapitalCity == true ? BodySpot.Town : BodySpot.Waiting, default, allowStep: false);
            }
            catch (Exception)
            {
            }
        }

        private static bool Act(GameBot bot, BodySpot spot, Vector3 anchor, bool allowStep)
        {
            if (!On || !Eligible(bot))
                return false;
            try
            {
                BodyState state = States.GetValue(bot, _ => new BodyState());
                long now = GameLoop.GameLoopTime;
                if (now < state.ShuffleUntilTick && bot.IsMoving)
                    return true;
                if (now < state.NextActTick)
                    return false;
                state.NextActTick = now + NextDelayMs(spot, Random.Shared.NextDouble());

                GamePlayer watcher = NearestWatcher(bot, out double distance);
                if (watcher == null)
                    return false;
                bool greetDue;
                lock (state.Greeted)
                    greetDue = watcher.Realm == bot.Realm && distance <= GreetRadius &&
                               (!state.Greeted.TryGetValue(watcher.Name, out long last) || now - last >= GreetCooldownMs);

                BodyChoice choice = Choose(spot, greetDue, bot.IsRecoveryResting,
                    Random.Shared.NextDouble(), Random.Shared.NextDouble(), Random.Shared.NextDouble(), allowStep);
                switch (choice.Move)
                {
                    case BodyMove.Look:
                        // Living Server 1.2: a wide look is sent in steps instead of one snap (BotTurning).
                        BotTurning.TurnGradually(bot, Turned(bot.Heading, choice.HeadingOffset));
                        Count(ref _looks);
                        return false;
                    case BodyMove.Emote:
                        bot.Emote(choice.Emote);
                        Count(ref _emotes);
                        return false;
                    case BodyMove.Greet:
                        lock (state.Greeted)
                            state.Greeted[watcher.Name] = now;
                        BotTurning.TurnGradually(bot, watcher);
                        AutonomousLivingServer.Later(bot, 350 + Random.Shared.Next(500),
                            () => Eligible(bot) && bot.IsWithinRadius(watcher, GreetRadius * 2), () => bot.Emote(choice.Emote));
                        Count(ref _greets);
                        return false;
                    case BodyMove.Shuffle:
                        // A step never competes with resting: only a fully recovered bot shifts its feet.
                        if (!allowStep || spot != BodySpot.Town || bot.IsRecoveryResting ||
                            !AutonomousRestPolicy.IsFullyRecovered(bot.HealthPercent, bot.ManaPercent, bot.EndurancePercent, bot.MaxMana > 0))
                            return false;
                        Vector3 from = new(bot.X, bot.Y, bot.Z);
                        Vector3 to = StepTarget(anchor, from, choice.StepAngle, choice.StepLength);
                        if (Vector2.Distance(new(from.X, from.Y), new(to.X, to.Y)) < ShuffleMinimumStep * 0.6f)
                            return false;
                        bot.WalkTo(to, (short)Math.Clamp(bot.MaxSpeed / 3, 60, 100));
                        state.ShuffleUntilTick = now + ShuffleMaximumMs;
                        Count(ref _steps);
                        return bot.IsMoving;
                    default:
                        return false;
                }
            }
            catch (Exception)
            {
                // Body language is cosmetic: never let it break a bot's turn.
                return false;
            }
        }

        /// <summary>A few bots standing near a character that just gained a level turn to it and cheer.</summary>
        public static void CheerDing(GameLiving who)
        {
            if (!On || who?.CurrentRegion == null)
                return;
            try
            {
                if (NearestWatcher(who, out _) == null)
                    return;
                GameBot[] near = who.GetNPCsInRadius(WorldMgr.SAY_DISTANCE).OfType<GameBot>()
                    .Where(bot => bot != who && bot.Realm == who.Realm && Eligible(bot))
                    .OrderBy(_ => Random.Shared.Next()).ToArray();
                int count = Math.Min(near.Length, who is GamePlayer ? Random.Shared.Next(1, 4) : Random.Shared.Next(0, 2));
                for (int i = 0; i < count; i++)
                {
                    GameBot bot = near[i];
                    eEmote emote = Pick(CheerEmotes, Random.Shared.NextDouble());
                    AutonomousLivingServer.Later(bot, 600 + i * 900 + Random.Shared.Next(1_200), () => Eligible(bot), () =>
                    {
                        BotTurning.TurnGradually(bot, who);
                        bot.Emote(emote);
                        Count(ref _cheers);
                    });
                }
            }
            catch (Exception)
            {
            }
        }

        /// <summary>The nearest real player who can see <paramref name="who"/>, or null.</summary>
        private static GamePlayer NearestWatcher(GameObject who, out double distance)
        {
            GamePlayer best = null;
            distance = double.MaxValue;
            foreach (GamePlayer player in ClientService.Instance.GetPlayersOfRegion(who.CurrentRegion))
            {
                if (player.ObjectState != GameObject.eObjectState.Active || !who.IsWithinRadius(player, WorldMgr.VISIBILITY_DISTANCE))
                    continue;
                double d = who.GetDistanceTo(player);
                if (d < distance)
                {
                    distance = d;
                    best = player;
                }
            }
            return best;
        }

        private static eEmote Pick(eEmote[] list, double roll) => list[(int)(Math.Clamp(roll, 0, 0.999999) * list.Length)];
    }
}
