using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace DOL.GS
{
    public static class AutonomousDungeonPolicy
    {
        public readonly record struct GroupTransitMember(ushort Region, Vector3 Position, bool OnStableRoute);

        public const float DungeonEntranceStagingRadius = 700;
        // These are the installed Old Frontiers branches, not the post-1.65
        // Passage of Conflict (244), or the unused client aliases 245/247.
        public static bool IsSharedFrontierDungeon(ushort region) => region is 246 or 248 or 276 or 277;
        public static bool IsSharedCombatDungeon(ushort region) => IsSharedFrontierDungeon(region) || region == 249;

        /// <summary>
        /// The Old Frontiers dungeons are destinations, not hallways. Walking from one realm's frontier to
        /// another runs the length of a raid dungeon: on 2026-10-19 the Albion route to Midgard, Hibernia and
        /// Dodens Gruva crossed Hall of the Corrupt beside Aidon the Archwizard (level 75), and about 118 of
        /// an hour's 895 bot deaths were in these four regions. A route may enter one of them as its
        /// destination, but passes through one only on the way out (the route starts inside the hub) or
        /// for the neutral raid's own approach to Summoner's Hall. Porters still carry warbands across.
        /// </summary>
        public static bool MayRouteThrough(ushort region, ushort start, ushort target) =>
            !IsSharedFrontierDungeon(region) || region == start || IsSharedFrontierDungeon(start) ||
            target == SummonersHallRegion;
        public static bool IsSupportedDungeonZone(ushort zone) => zone is
            19 or 21 or 22 or 23 or 24 or 50 or 60 or 61 or 62 or
            125 or 126 or 127 or 128 or 129 or 150 or 160 or 161 or
            180 or 190 or 191 or 220 or 221 or 222 or 223 or 224 or
            246 or 248 or 276 or 277 ||
            zone == 249 && AutonomousDarknessFallsNavigation.IsReady;

        public static bool IsStarterDungeonRegion(ushort region) => region is 21 or 129 or 221;

        private static readonly Vector3 CursedTombArrival = new(31700, 33000, 16000);

        public static bool IsCursedTombEntryStage(ushort leaderRegion, Vector3 leaderPosition) =>
            leaderRegion != 128 || AtCursedTombEntrance(leaderPosition);

        private static bool AtCursedTombEntrance(Vector3 point) =>
            MathF.Abs(point.Z - CursedTombArrival.Z) <= 500 &&
            Vector2.DistanceSquared(new(point.X, point.Y),
                new(CursedTombArrival.X, CursedTombArrival.Y)) <= 2_000 * 2_000;

        /// <summary>
        /// On a formed party's first Cursed Tomb assignment, favor the verified
        /// entrance-room spawn cells it can start killing immediately. In the
        /// overnight run, distant initial goals left otherwise productive
        /// parties fighting the respawning entry pack without target credit.
        /// The caller supplies only already-legal, level-valid camps and turns
        /// this preference off once real interior combat progress is observed.
        /// Other dungeons, solo goals and the later Cursed Tomb pool are intact.
        /// </summary>
        public static T[] PreferFirstCursedTombGroupCamps<T>(IEnumerable<T> candidates,
            bool formedGroup, bool hasInteriorProgress, Func<T, ushort> region,
            Func<T, Vector3> position)
        {
            T[] available = candidates?.ToArray() ?? [];
            if (!formedGroup || hasInteriorProgress || region == null || position == null)
                return available;
            bool AtEntrance(T camp) => region(camp) == 128 &&
                AtCursedTombEntrance(position(camp));
            if (!available.Any(AtEntrance)) return available;
            return available.Where(camp => region(camp) != 128 || AtEntrance(camp)).ToArray();
        }

        // Nisse's two haunt packs mix level-18 and level-29 creatures deep in
        // the same branch. The live audit showed hundreds of bots clearing
        // unrelated corridor mobs while never producing a valid haunt kill.
        // They remain real monsters and normal route threats, but are not a
        // reliable autonomous camp objective.
        /// <summary>
        /// Whether a corridor monster is pulled to clear the route. An epic monster is pulled only
        /// by bots that came for it, or by a raid inside its own dungeon (where the raid fights it).
        /// </summary>
        public static bool PullsOnRoute(bool epic, bool ownTarget, bool inOwnRaidDungeon) =>
            !epic || ownTarget || inOwnRaidDungeon;

        public static bool IsReliableAutonomousGoal(ushort region, string name) =>
            (region != AutonomousDarknessFallsPolicy.RegionId || AutonomousDarknessFallsNavigation.IsReady) &&
            !(region == 129 && string.Equals(name, "haunt", StringComparison.OrdinalIgnoreCase)) &&
            // Cursed Tomb's two level-13/14 cave spiders are behind the
            // entrance's five level-19/20 roaming corpses. The overnight
            // run repeatedly left solo bots fighting those respawns at
            // 31700,33000 without reaching a spider. Keep the actual mobs
            // for players; exclude only this unsafe autonomous objective.
            !(region == 128 && string.Equals(name, "cave spider", StringComparison.OrdinalIgnoreCase)) &&
            // Installed entrance-to-husk corridors cross aggressive level
            // 36-43 packs. Level 10-11 husks cannot be safe XP goals for the
            // low-level bots that qualify for them. Do not alter any monsters.
            !(region == 125 && string.Equals(name, "husk", StringComparison.OrdinalIgnoreCase)) &&
            // Summoner's Hall is reachable only through Dodens Gruva, Hall of the
            // Corrupt or Marfach Caverns. Groups spent their whole task fighting
            // through those first: 1 of 268 attempts arrived in one evening run.
            // Its monsters and routes stay for players; bots never target it.
            region != SummonersHallRegion;

        public const ushort SummonersHallRegion = 248;

        // A monster may be geometrically close to the route through a wall or
        // from a disconnected room polygon.  Visibility alone is insufficient:
        // only force a route-clearing pull when both endpoints project to the
        // installed mesh and the bot can actually reach that monster.
        public static bool CanSelectRouteBlocker(bool botFloorResolved,
            bool blockerFloorResolved, bool completeCorridor) =>
            botFloorResolved && blockerFloorResolved && completeCorridor;

        // The party member at the front of a formation is not necessarily the
        // designated tank-puller.  A legal corridor blocker may be handed to
        // that tank while the current PvE roster is viable and actively
        // travelling or grinding. Attendance may already have removed one or
        // more no-shows before the dungeon task starts. Resource, casualty and
        // cohesion gates are still applied by CanInitiateNewPull.
        public static bool CanHandoffRouteBlocker(bool groupPve, string phase,
            int memberCount, bool requiredComposition, bool differentPuller,
            bool pullerAlive, bool sameRegion, bool pullerOnStableRoute) =>
            groupPve && phase is "Traveling" or "Grinding" &&
            memberCount is >= 3 and <= 8 && requiredComposition && differentPuller &&
            pullerAlive && sameRegion && !pullerOnStableRoute;

        /// <summary>
        /// Before the first party member uses a dungeon entrance, every living
        /// member must be standing on the source side near that same entrance.
        /// Once the crossing has begun, the remaining staged members may follow.
        /// </summary>
        public static bool GroupReadyForDungeonEntrance(ushort sourceRegion, ushort targetRegion,
            Vector3 source, IReadOnlyList<GroupTransitMember> members)
        {
            if (members == null || members.Count < 2)
                return true;
            if (members.Any(member => member.Region == targetRegion))
                return true;
            float radiusSquared = DungeonEntranceStagingRadius * DungeonEntranceStagingRadius;
            return members.All(member => !member.OnStableRoute && member.Region == sourceRegion &&
                Math.Abs(member.Position.Z - source.Z) <= 500 &&
                Vector2.DistanceSquared(new(member.Position.X, member.Position.Y), new(source.X, source.Y)) <= radiusSquared);
        }

        public static bool GroupReadyAtInteriorStaging(ushort targetRegion, Vector3 staging,
            IReadOnlyList<GroupTransitMember> members, float radius = 350)
        {
            if (members == null || members.Count < 2)
                return true;
            float radiusSquared = radius * radius;
            return members.All(member => !member.OnStableRoute && member.Region == targetRegion &&
                Math.Abs(member.Position.Z - staging.Z) <= 500 &&
                Vector2.DistanceSquared(new(member.Position.X, member.Position.Y),
                    new(staging.X, staging.Y)) <= radiusSquared);
        }

        public static bool CanEngageLocalOpponent(eRealm attacker, eRealm target,
            ushort attackerRegion, ushort targetRegion, bool alive, bool allowed) =>
            IsSharedCombatDungeon(attackerRegion) && attackerRegion == targetRegion &&
            attacker != eRealm.None && target != eRealm.None && attacker != target && alive && allowed;

        /// <summary>A hold before a corridor threat is one hold while the bot keeps holding in one place. With no hold scan
        /// for longer than this a new hold begins. It was 5 s, on the belief that scans come every 0.75-1 s; a held raid
        /// party's scans were measured 6.5 s or more apart (play test duo32, 2026-11-11, AUTONOMOUS_DUNGEON_HOLD_LONG:
        /// median longest gap 13 s, 25-27 restarts in three minutes), so every scan began a new hold and the 90-second limit never ended one:
        /// 80 raiders stood before a Summoned Guardian in Dodens Gruva for up to 18 minutes (duo30: 60 until the watchdog
        /// took them to town; duo31: 39 while the other 41 fought Govannon and wiped).</summary>
        public const long ThreatHoldGapMilliseconds = 60_000;
        /// <summary>Further than this from where the hold began, the bot has walked on and the next hold is a new one.</summary>
        public const float ThreatHoldSamePlace = 400f;

        /// <summary>
        /// When the current hold before a corridor threat began (pure). PC test 2026-11-09 (build b72716e): Ferken, a member of
        /// a forced Krondon raid party, stood in Avalon City on "Holding before dungeon threat" for over 30 minutes (01:16 to
        /// 01:55) while his party and its puller were at Caer Diogel in another region, and the raid waited for him. The
        /// 90-second limit was kept per blocker: the clock started again whenever the first threat along his corridor
        /// changed (roaming monsters take turns at the front of the list). It now runs from the first scan of an unbroken
        /// hold, whichever monster is in the way, and (2026-11-11) however far apart the scans come: a hold is unbroken
        /// while the bot stays within <see cref="ThreatHoldSamePlace"/> of where it began and scans again within
        /// <see cref="ThreatHoldGapMilliseconds"/>.
        /// </summary>
        public static long ThreatHoldStart(long holdSince, long lastHeld, long now, float movedSinceHoldBegan = 0) =>
            holdSince <= 0 || lastHeld <= 0 || now - lastHeld > ThreatHoldGapMilliseconds ||
            movedSinceHoldBegan > ThreatHoldSamePlace ? now : holdSince;

        /// <summary>
        /// Whether a party member holds before a corridor threat for its puller (pure): only while some other living member
        /// of its party is in the same region (a puller in another region never comes) and for at most
        /// <paramref name="limitMilliseconds"/> of one hold.
        /// </summary>
        public static bool HoldsForPuller(bool partyMemberInRegion, long heldMilliseconds, long limitMilliseconds) =>
            partyMemberInRegion && heldMilliseconds < limitMilliseconds;

        /// <summary>
        /// Whether a raid party may clear a corridor monster while its raid holds (pure): at a crossing (as before), or a
        /// monster that is not an epic encounter, inside the raid's own dungeon. PC test 2026-11-09 (forced Krondon): the raid
        /// holds while it musters for a named encounter, and a held raid let nobody pull, so every party stopped 90 s before
        /// each monster on its walk to the muster and then fought it anyway (Ulor se Bysen's muster took 19 minutes, with 174
        /// coin drops from monsters killed in Krondon meanwhile, REALM_RAID_LOOT_MONEY). The puller now pulls it at once, with the party's usual
        /// gates (resources, roster, cohesion); an epic encounter in the way still waits for the raid's order.
        /// </summary>
        public static bool ExpeditionMayClearCorridor(bool hold, bool crossing, bool corridorBlocker, bool ownRaidDungeonTrash) =>
            !hold || corridorBlocker && (crossing || ownRaidDungeonTrash);

        // A follower that was already separated by a death, reconnect, or old
        // transient-leader handoff must be able to clear a legal blocker while
        // walking back to its leader. Normal outbound dungeon pulls remain
        // leader-only, so this cannot make a healthy formation fan out and pull.
        public static bool IsFollowerRejoinDestination(bool dynamicGroup, bool isFollower,
            ushort followerRegion, ushort leaderRegion, Vector3 followerPosition,
            Vector3 destination, Vector3 leaderPosition) =>
            dynamicGroup && isFollower && followerRegion == leaderRegion &&
            Vector3.DistanceSquared(followerPosition, leaderPosition) > 650 * 650 &&
            Vector3.DistanceSquared(destination, leaderPosition) <= 220 * 220;

        // Distance to the real, winding, 3-D corridor; never a straight line
        // through the dungeon's walls/floors toward a distant camp.
        public static bool IntersectsCorridor(ReadOnlySpan<Vector3> route, Vector3 position,
            float radius, float lookAhead, out float along)
        {
            along = float.MaxValue;
            float travelled = 0;
            for (int i = 1; i < route.Length && travelled < lookAhead; i++)
            {
                Vector3 start = route[i - 1], delta = route[i] - start;
                float length = delta.Length();
                if (length < 0.01f) continue;
                float available = Math.Min(length, lookAhead - travelled);
                float t = Math.Clamp(Vector3.Dot(position - start, delta) / (length * length), 0, available / length);
                Vector3 nearest = start + delta * t;
                // Aggro across another floor must not become a route pull.
                if (Math.Abs(nearest.Z - position.Z) <= 160 && Vector3.DistanceSquared(nearest, position) <= radius * radius)
                {
                    along = travelled + t * length;
                    return true;
                }
                travelled += length;
            }
            return false;
        }
    }
}
