using System.Collections.Generic;
using System.Linq;

namespace DOL.GS
{
    /// <summary>
    /// The tale, then the call (Living Server 1.2, the owner's idea, 2026-11-12): before a realm's raid forms, the realm's
    /// storyteller tells that boss's tale in /faction (AutonomousTales). When the tale ends the realm is called: the large
    /// line across the screen and a horn, and only then are bots recruited and the muster clock started
    /// (<see cref="Start(string, eRealm, out string, bool)"/>). A tale that cannot start, or runs long, never costs the raid:
    /// the call goes out anyway (RealmRaidRecruitmentPolicy.CallStep).
    /// <list type="bullet">
    /// <item>The realm's own scheduled raids and raids forced from the launcher; never a play test's raid (/epictest).</item>
    /// <item>Only while a player of that realm is in the game: nobody else would hear the tale.</item>
    /// <item>Off with realm_raid_tale_first = False: raids form at once, as before.</item>
    /// </list>
    /// AutonomousTales asks (<see cref="TaleWanted"/>), starts the tale outside this class's lock, and reports back
    /// (<see cref="TaleStartResult"/>, <see cref="CallAfterTale"/>); this class never calls into the tales while it holds its lock.
    /// </summary>
    public static partial class AutonomousRealmRaid
    {
        private sealed class PendingCall
        {
            public string Id;
            public string Name;
            public bool Forced;
            public long Asked;
            public bool TaleStarted;
            public long TaleStartedAt;
            public string LastWhy;
        }

        private static readonly Dictionary<eRealm, PendingCall> PendingCalls = new();

        /// <summary>Whether this raid's tale is told before it forms: the setting, tales on, a tale of its boss for the realm,
        /// and a player of the realm in the game to hear it.</summary>
        private static bool TaleFirst(string id, eRealm realm) =>
            RealmRaidRecruitmentPolicy.TaleFirst && AutonomousTales.On && AutonomousTales.HasTaleForRaid(id, realm) &&
            ClientService.Instance.GetPlayersOfRealm(realm).Count > 0;

        /// <summary>Under the raid lock: the raid waits for its tale. False when it forms at once instead.</summary>
        private static bool BeginCall(Definition definition, eRealm realm, long now, bool forced)
        {
            if (definition == null || PendingCalls.ContainsKey(realm) || !TaleFirst(definition.Id, realm))
                return false;
            PendingCalls[realm] = new PendingCall { Id = definition.Id, Name = definition.Name, Forced = forced, Asked = now };
            Log.Info($"REALM_RAID_TALE_FIRST event={definition.Id} realm={GlobalConstants.RealmToName(realm)} forced={forced}");
            return true;
        }

        /// <summary>
        /// A raid forced from the launcher: its tale first, when it has one and the encounter is up. True when the raid now
        /// waits for its tale (<paramref name="reason"/> says so); false when the caller starts it at once, as before.
        /// </summary>
        public static bool BeginForcedCall(string id, eRealm realm, out string reason)
        {
            using (AiProfile.Lock(Sync, AiSystem.LockRaid))
            {
                reason = null;
                Definition definition = Definitions.FirstOrDefault(d => d.Id == id && d.Realm == realm);
                if (definition == null || Raids.ContainsKey(id) || !Available(id) || !BeginCall(definition, realm, GameLoop.GameLoopTime, true))
                    return false;
                reason = $"{definition.Name}: the realm's storyteller tells its tale in /faction first. The raid is called when the tale ends, " +
                         $"within {RealmRaidRecruitmentPolicy.TaleMaxMs / 60_000} minutes at most.";
                return true;
            }
        }

        /// <summary>The raid whose tale the realm's storyteller should start now, or false.</summary>
        public static bool TaleWanted(eRealm realm, out string raidId)
        {
            using (AiProfile.Lock(Sync, AiSystem.LockRaid))
            {
                raidId = PendingCalls.TryGetValue(realm, out PendingCall call) && !call.TaleStarted ? call.Id : null;
                return raidId != null;
            }
        }

        /// <summary>AutonomousTales tried to start the raid's tale: it runs, or why not (tried again on the next pulse).</summary>
        public static void TaleStartResult(eRealm realm, string raidId, bool started, string why)
        {
            using (AiProfile.Lock(Sync, AiSystem.LockRaid))
            {
                if (!PendingCalls.TryGetValue(realm, out PendingCall call) || call.Id != raidId)
                    return;
                if (started)
                {
                    call.TaleStarted = true;
                    call.TaleStartedAt = GameLoop.GameLoopTime;
                    Log.Info($"REALM_RAID_TALE event={raidId} realm={GlobalConstants.RealmToName(realm)} started=True");
                }
                else
                {
                    call.TaleStarted = false;
                    if (call.LastWhy != why)
                        Log.Info($"REALM_RAID_TALE event={raidId} realm={GlobalConstants.RealmToName(realm)} started=False reason=\"{why}\"");
                    call.LastWhy = why;
                }
            }
        }

        /// <summary>The raid's tale ended (told to its end, or cut short): the realm is called now.</summary>
        public static void CallAfterTale(eRealm realm, string raidId, bool finished, string reason)
        {
            using (AiProfile.Lock(Sync, AiSystem.LockRaid))
            {
                if (PendingCalls.TryGetValue(realm, out PendingCall call) && call.Id == raidId)
                    OpenCall(realm, call, finished ? "the tale was told" : $"the tale was cut short ({reason})");
            }
        }

        /// <summary>Under the raid lock, each pulse: a tale that never started, or ran long, does not hold the raid.</summary>
        private static void PulseCalls(long now)
        {
            foreach (var pair in PendingCalls.ToArray())
            {
                PendingCall call = pair.Value;
                switch (RealmRaidRecruitmentPolicy.CallStep(now, call.Asked, call.TaleStarted, call.TaleStartedAt))
                {
                    case RealmRaidRecruitmentPolicy.RaidCallStep.CallWithoutTale:
                        OpenCall(pair.Key, call, $"no tale could start ({call.LastWhy ?? "not tried"})");
                        break;
                    case RealmRaidRecruitmentPolicy.RaidCallStep.CallTaleRanLong:
                        OpenCall(pair.Key, call, "the tale ran long");
                        break;
                }
            }
        }

        private static void OpenCall(eRealm realm, PendingCall call, string after)
        {
            PendingCalls.Remove(realm);
            bool opened = Start(call.Id, realm, out string reason, call.Forced);
            string realmName = GlobalConstants.RealmToName(realm);
            Log.Info($"REALM_RAID_CALLED event={call.Id} realm={realmName} forced={call.Forced} after=\"{after}\" opened={opened} reason=\"{reason}\"");
            if (opened && !call.Forced && Raids.TryGetValue(call.Id, out Raid raid))
                Log.Info($"REALM_RAID_SCHEDULED event={call.Id} realm={realmName} online={OnlineLevelFifty(realm)} required={raid.Required} " +
                         $"recruitCap={raid.RecruitCap} {SizeField(raid)} after=tale");
        }

        /// <summary>Under the raid lock: a raid waiting for its tale is dropped (stopped from the launcher, or another raid of
        /// the realm was forced meanwhile).</summary>
        private static bool CancelCall(string id, string why)
        {
            foreach (var pair in PendingCalls.Where(p => id == null || p.Value.Id == id).ToArray())
            {
                PendingCalls.Remove(pair.Key);
                Log.Info($"REALM_RAID_CALL_CANCELLED event={pair.Value.Id} realm={GlobalConstants.RealmToName(pair.Key)} reason=\"{why}\"");
                return true;
            }
            return false;
        }
    }
}
