using System.Runtime.CompilerServices;
using DOL.AI.Brain;

namespace DOL.GS
{
    /// <summary>
    /// Port of zomboden/OfflineDAoC-True165 e4d2498ca and 927018e (raid parties are one side).
    /// One fight per monster (True165 owner 2026-11-05: "make it so gamebots stop attacking same mobs and sharing xp from mobs.
    /// bots shouldnt attack mobs that other bots are fighting unless they are in the same group"). A gamebot that pulls
    /// a monster claims it for itself or its group for 30 s (enough to reach it and land the first blow); after that
    /// the monster's attackers say whose it is. Another gamebot outside that group leaves it alone - claimed, hit by a
    /// bot, a player or their pets from another group. Self-defense is untouched (a monster attacking a bot is fought).
    /// </summary>
    public static class AutonomousMobClaims
    {
        public const int ClaimMilliseconds = 30_000;

        private sealed class Claim
        {
            public object Key;
            public long Until;
        }

        private static readonly ConditionalWeakTable<GameNPC, Claim> Claims = new();

        /// <summary>
        /// The bot's side: its realm raid when its party is in one, else its group, or itself when solo.
        /// dragon-fight L2: the 2026-10-31 "fighting someone outside the group" filter and these claims (2026-11-05)
        /// treated every sister party of a raid as another group, so only the party whose puller reached the dragon
        /// first opened the fight and the other 37 waited for a line-of-sight defence call or the first breath. A raid
        /// is one side; an ordinary group is still its own side.
        /// </summary>
        public static object SideOf(GameLiving living)
        {
            Group group = living?.Group;
            return group == null ? living : RaidSideOf(group, living) ?? group;
        }

        /// <summary>Pure: the two sides are one (the same raid, group or bot), or the other is a real player of the
        /// bot's realm fighting beside its raid (a horn sounder is in no raid party).</summary>
        public static bool SameSide(object mine, object theirs, bool mineIsRaid, bool theirsIsRealmPlayer) =>
            mine != null && (ReferenceEquals(mine, theirs) || mineIsRaid && theirsIsRealmPlayer);

        /// <summary>
        /// The bot is fighting on the same side as <paramref name="fighting"/>, the living a monster is attacking: its
        /// own group (as before), or for a raid party a sister party or a real player of its realm.
        /// </summary>
        public static bool SameFight(GameBot bot, GameLiving fighting)
        {
            if (bot?.Group == null || fighting == null)
                return false;
            if (fighting.Group == bot.Group)
                return true;
            object mine = SideOf(bot);
            return SameSide(mine, SideOf(fighting), mine is not Group,
                fighting is GamePlayer && fighting.Realm == bot.Realm);
        }

        private sealed class RaidSide
        {
            public object Raid;
            public long Until;
        }

        /// <summary>The raid a party is in, read from the raid roster at most every 2 s per party (one lookup, cached).</summary>
        public const long RaidSideRefreshMilliseconds = 2_000;
        private static readonly ConditionalWeakTable<Group, RaidSide> RaidSides = new();

        private static object RaidSideOf(Group group, GameLiving asker)
        {
            RaidSide side = RaidSides.GetValue(group, _ => new RaidSide());
            long now = GameLoop.GameLoopTime;
            if (now < side.Until)
                return side.Raid;
            // Living Server: SupportScope is the realm raid, else a realm warband (both one side), else the group itself.
            GameBot member = asker as GameBot ?? group.LivingLeader as GameBot;
            object raid = member != null ? AutonomousRealmRaid.SupportScope(member) : null;
            side.Raid = raid is Group ? null : raid;
            side.Until = now + RaidSideRefreshMilliseconds;
            return side.Raid;
        }

        public static void Take(GameBot bot, GameNPC npc)
        {
            if (bot == null || npc == null)
                return;
            Claim claim = Claims.GetValue(npc, _ => new Claim());
            lock (claim)
            {
                claim.Key = SideOf(bot);
                claim.Until = GameLoop.GameLoopTime + ClaimMilliseconds;
            }
        }

        /// <summary>Someone outside the bot's side has this monster: a fresh claim, or an attacker (or its pet).</summary>
        public static bool TakenByOther(GameBot bot, GameNPC npc)
        {
            if (bot == null || npc == null)
                return false;
            object mine = SideOf(bot);
            bool raid = mine is not Group && mine != bot;
            if (Claims.TryGetValue(npc, out Claim claim))
            {
                lock (claim)
                {
                    if (claim.Until > GameLoop.GameLoopTime && claim.Key != null && !ReferenceEquals(claim.Key, mine) &&
                        (claim.Key is not GameLiving holder || holder.IsAlive && holder.ObjectState == GameObject.eObjectState.Active))
                        return true;
                }
            }
            var attackers = npc.attackComponent?.AttackerTracker?.Attackers;
            if (attackers == null)
                return false;
            foreach (GameLiving attacker in attackers)
            {
                if (attacker == null || !attacker.IsAlive || attacker == bot)
                    continue;
                GameLiving side = attacker is GameNPC { Brain: IControlledBrain controlled } && controlled.Owner != null
                    ? controlled.Owner : attacker;
                if (side == bot || side is not (GameBot or GamePlayer))
                    continue;
                if (!SameSide(mine, SideOf(side), raid, side is GamePlayer && side.Realm == bot.Realm))
                    return true;
            }
            return false;
        }
    }
}
