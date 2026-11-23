using System;
using System.Collections.Generic;
using System.Linq;
using DOL.AI.Brain;
using DOL.Logging;

namespace DOL.GS.Commands
{
    /// <summary>
    /// GM /jump for bots (#438, 1.2.1). Companions (/spawn and /raid helpers, /bot companions), hired companions and
    /// autonomous gamebots are GameBot NPCs with no client, so ClientService's player lookup never found them and
    /// "/jump &lt;bot&gt; to me" answered "not in the game". This finds them by name and moves them safely.
    /// </summary>
    public static class JumpBotTargets
    {
        private static readonly Logger Log = LoggerManager.Create(typeof(JumpBotTargets));

        public enum NameMatch { None, Exact, Partial, Ambiguous }

        /// <summary>
        /// Pure: the candidate with the name. An exact name (any case) wins over a partial one (a name that starts with
        /// the text). Among equal matches the lowest rank wins (the GM's own bots first); a partial name that two
        /// candidates of the best rank share is ambiguous and picks nothing, as the player lookup does.
        /// </summary>
        public static T Pick<T>(IEnumerable<T> candidates, string name, Func<T, string> nameOf, Func<T, int> rank,
            out NameMatch match) where T : class
        {
            match = NameMatch.None;
            if (candidates == null || string.IsNullOrWhiteSpace(name))
                return null;
            string wanted = name.Trim();
            List<T> named = candidates.Where(candidate => candidate != null && !string.IsNullOrEmpty(nameOf(candidate)))
                .Distinct().ToList();
            List<T> exact = named.Where(candidate =>
                string.Equals(nameOf(candidate), wanted, StringComparison.OrdinalIgnoreCase)).ToList();
            List<T> pool = exact.Count > 0
                ? exact
                : named.Where(candidate => nameOf(candidate).StartsWith(wanted, StringComparison.OrdinalIgnoreCase)).ToList();
            if (pool.Count == 0)
                return null;
            int best = pool.Min(rank);
            List<T> top = pool.Where(candidate => rank(candidate) == best).ToList();
            if (exact.Count == 0 && top.Count > 1)
            {
                match = NameMatch.Ambiguous;
                return null;
            }
            match = exact.Count > 0 ? NameMatch.Exact : NameMatch.Partial;
            return top[0];
        }

        /// <summary>Pure: 0 the GM's own companion, 1 a bot in the GM's group, 2 one in the GM's region, 3 elsewhere.</summary>
        public static int Rank(bool ownedByGm, bool inGmGroup, bool sameRegion) =>
            ownedByGm ? 0 : inGmGroup ? 1 : sameRegion ? 2 : 3;

        /// <summary>Pure: who a "/jump &lt;name&gt; ..." means. An exact player name first, then an exact bot name, then a
        /// partial player name, then a partial bot name. Returns 1 for the player, 2 for the bot, 0 for neither.</summary>
        public static int Choose(bool playerFound, bool playerExact, NameMatch bot)
        {
            if (playerFound && playerExact) return 1;
            if (bot == NameMatch.Exact) return 2;
            if (playerFound) return 1;
            return bot == NameMatch.Partial ? 2 : 0;
        }

        /// <summary>Every bot in the world a GM can name: companions of every kind, hired companions, gamebots,
        /// buffbots and Special Forces, and the bots in any player's group (the /spawn and /raid helpers).</summary>
        public static IEnumerable<GameBot> AllBots()
        {
            var seen = new HashSet<GameBot>();
            IEnumerable<GameBot> sources = BotManager.ActiveBotsSnapshot()
                .Concat(AutonomousBotRegistry.Snapshot())
                .Concat(AutonomousBotRegistry.HiredSnapshot())
                .Concat(AutonomousBotRegistry.BuffBotSnapshot())
                .Concat(AutonomousBotRegistry.SpecialForcesSnapshot())
                .Concat(ClientService.Instance.GetPlayers()
                    .Where(player => player?.Group != null)
                    .SelectMany(player => player.Group.GetMembersInTheGroup().OfType<GameBot>()));
            foreach (GameBot bot in sources)
                if (bot?.ObjectState == GameObject.eObjectState.Active && seen.Add(bot))
                    yield return bot;
        }

        public static GameBot FindBot(GamePlayer gm, string name, out NameMatch match) =>
            Pick(AllBots(), name, bot => bot.Name,
                bot => Rank(gm != null && bot.Owner == gm, gm?.Group != null && bot.Group == gm.Group,
                    gm != null && bot.CurrentRegionID == gm.CurrentRegionID),
                out match);

        /// <summary>The player or bot a GM names: see <see cref="Choose"/>. A partial name that matches several bots
        /// says so in <paramref name="ambiguous"/>.</summary>
        public static GameLiving FindPlayerOrBot(GamePlayer gm, string name, out bool ambiguous)
        {
            GamePlayer player = ClientService.Instance.GetPlayerByPartialName(name, out ClientService.PlayerGuessResult result);
            GameBot bot = FindBot(gm, name, out NameMatch match);
            ambiguous = player == null && match == NameMatch.Ambiguous;
            return Choose(player != null, result == ClientService.PlayerGuessResult.FOUND_EXACT, match) switch
            {
                1 => player,
                2 => bot,
                _ => null,
            };
        }

        /// <summary>Why a bot can't be jumped: <see cref="MoveRefusal.None"/> when it can.</summary>
        public enum MoveRefusal { None, NotInWorld, Dead }

        /// <summary>Pure: a bot that left the world can't be moved; a dead one isn't either, as its corpse stays where it fell
        /// (GameBot.RefuseCorpseMove: its release and return are planned from that spot, so moving the body would send the
        /// released bot to a bind of the wrong region).</summary>
        public static MoveRefusal Refusal(bool inWorld, bool alive) =>
            !inWorld ? MoveRefusal.NotInWorld : !alive ? MoveRefusal.Dead : MoveRefusal.None;

        public static MoveRefusal Refusal(GameBot bot) =>
            Refusal(bot?.ObjectState == GameObject.eObjectState.Active, bot?.IsAlive == true);

        /// <summary>Pure: what the GM is told when a jump of the bot named <paramref name="name"/> failed.</summary>
        public static string FailureText(string name, MoveRefusal refusal) => refusal switch
        {
            MoveRefusal.Dead => $"{name} is dead, and a body stays where it fell: resurrect it there, or wait for it to release, then jump it.",
            MoveRefusal.NotInWorld => $"{name} is not in the world any more.",
            _ => $"{name} could not be moved there.",
        };

        /// <summary>What the GM is told when <see cref="TryMove"/> returned false for <paramref name="bot"/>.</summary>
        public static string FailureText(GameBot bot) => FailureText(bot?.Name, Refusal(bot));

        /// <summary>
        /// Moves a bot where a GM sends it, in its region or another. It drops what it was doing first (attack, path, follow,
        /// cast, a stablemaster ride or one waiting to leave), so its brain plans again from the new place; leaving the
        /// world for the move takes it off every monster's aggro list (GameNPC.RemoveFromWorld), so a fight it was in
        /// doesn't follow it. An autonomous gamebot also forgets the trip it planned from the old spot (camp, patrol, town
        /// teleporter, frontier porter, a stable ride it meant to take; its task stays), records the move as progress, so
        /// its stuck watchdog does not take the jump for a stall, and replots its route (the same steps the watchdog uses
        /// when it puts a stuck gamebot back beside its player). A bot in a player's group follows on from there. A dead
        /// bot is refused before anything changes (<see cref="Refusal(bool, bool)"/>). Bots never ride boats (only players
        /// board GameBoat), so there is no boat to leave.
        /// </summary>
        public static bool TryMove(GameBot bot, ushort region, int x, int y, int z, ushort heading, GamePlayer gm)
        {
            if (Refusal(bot) != MoveRefusal.None)
            {
                if (bot != null)
                    Log.Warn($"GM_JUMP_BOT_REFUSED gm=\"{gm?.Name}\" bot=\"{bot.Name}\" kind={Kind(bot)} reason={Refusal(bot)} " +
                             $"at={bot.CurrentRegionID}:{bot.X},{bot.Y},{bot.Z}");
                return false;
            }
            int oldX = bot.X, oldY = bot.Y, oldZ = bot.Z;
            ushort oldRegion = bot.CurrentRegionID;
            bool riding = bot.IsOnStableMasterRoute;
            bool fighting = bot.InCombat;
            if (riding)
                bot.CompleteStableMasterRoute();
            bot.StopAttack();
            bot.StopFollowing();
            bot.StopMovingOnPath();
            bot.StopMoving();
            bot.StopCurrentSpellcast();
            if (bot.Brain is BotBrain brain)
                brain.ClearAggroList();
            if (!bot.MoveTo(region, x, y, z, heading))
                return false;
            bot.ForcePathReplot();
            if (bot.IsAutonomousWorldBot)
            {
                (bot.Brain as BotBrain)?.ForgetAutonomousTrip();
                bot.TryConsumeAutonomousPathFailure(out _, out _);
                AutonomousStuckWatchdog.MarkProgress(bot, eAutonomousProgressKind.Recovery);
                bot.MarkAutonomousStateDirty();
            }
            if (bot.IsPlayerLedGroup && bot.Brain is BotBrain followBrain)
                followBrain.FSM.SetCurrentState(eFSMStateType.FOLLOW);
            Log.Warn($"GM_JUMP_BOT gm=\"{gm?.Name}\" bot=\"{bot.Name}\" kind={Kind(bot)} " +
                     $"from={oldRegion}:{oldX},{oldY},{oldZ} to={region}:{x},{y},{z} " +
                     $"region_change={oldRegion != region} was_riding={riding} was_fighting={fighting}");
            return true;
        }

        public static string Kind(GameBot bot) =>
            bot.IsHiredCompanion ? "hired" : bot.IsAutonomousWorldBot ? "gamebot" : bot.IsTemporaryGroupHelper ? "helper" : "companion";
    }
}
