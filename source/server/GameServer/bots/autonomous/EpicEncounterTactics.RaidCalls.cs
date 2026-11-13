using System;
using System.Collections.Generic;
using System.Linq;
using DOL.GS.PacketHandler;
using DOL.GS.Spells;

namespace DOL.GS
{
    /// <summary>
    /// The raid's call for a blast (1.2, owner 2026-11-08; <see cref="RaidDiscipline"/>). Xanxicar's stomp dodge had become
    /// perfect: every raider in range got the order on the same tick straight from his health, and his 40% and 20% stomps
    /// killed nobody. Now, for the blasts in <see cref="CalledBlastTypes"/>:
    /// <list type="bullet">
    /// <item>the stomp window opens as before (<see cref="InBlastWindow"/>), but nobody moves yet;</item>
    /// <item>the raid's caller (its leader; if dead, the next living party leader) shouts the call in every raid party's
    /// /group a variable time before the stomp lands (<see cref="RaidDiscipline.CallFor"/>), sometimes late;</item>
    /// <item>each raider leaves after its own reaction delay, or lapses (<see cref="RaidDiscipline.ReactionFor"/>); the party he
    /// fights stays in on purpose, as before;</item>
    /// <item>after the stomp the caller says "back in".</item>
    /// </list>
    /// Where each raider goes (the v4 floor spots) is unchanged: the bots decide when they go, not where. Traces:
    /// EPIC_BLAST_CALL, EPIC_BLAST_REACT (one per raider and stomp) and EPIC_BLAST_LANDED (from the boss's stomp cast signal,
    /// or 7.5 s after the health crossing when no cast was seen).
    /// </summary>
    public static partial class EpicEncounterTactics
    {
        /// <summary>The blasts a raid caller calls, by boss class, with the encounter signal its cast raises. Only Xanxicar's
        /// stomp is wired; Myrddraxis's blasts and the rest keep the window rule alone.</summary>
        private static readonly Dictionary<string, string> CalledBlastTypes = new(StringComparer.Ordinal)
        {
            ["Xanxicar"] = EncounterSignals.Stomp,
        };

        public static bool IsCalledBlast(GameNPC boss) => boss != null && CalledBlastTypes.ContainsKey(boss.GetType().Name);

        /// <summary>Tests: a test boss class whose blast is called.</summary>
        public static void CallBlastForTests(string bossType, string signal = EncounterSignals.Stomp) => CalledBlastTypes[bossType] = signal;
        public static void UncallBlastForTests(string bossType) { if (bossType != "Xanxicar") CalledBlastTypes.Remove(bossType); }

        /// <summary>Xanxicar's stomp lands about this long after his health crosses a threshold: his brain sees it on its next
        /// think, 1 s to the announcement, then his cast (6 s on the spell, about 5 s at his casting speed). duo16: the
        /// four stomps landed about 6.2 s after the crossing; duo17 (timed from his cast) 6.9-7.2 s.</summary>
        public const double BlastLandsAfterCrossSeconds = 6.8;
        /// <summary>Without a cast seen, the raiders inside the radius are counted this long before the landing; the dead
        /// are counted this long after it (duo17: the stomp's hits on raiders 700-1,300 out landed 4 s after those beside
        /// him).</summary>
        public const long BlastLandingSnapshotLeadMilliseconds = 400, BlastLandingCountAfterMilliseconds = 6_000;
        /// <summary>"back in" comes this long after the landing (plus up to a second).</summary>
        public const long BlastBackInMilliseconds = 2_500;

        /// <summary>
        /// Seconds until the blast lands (pure): from its cast when the boss has started it, else from the crossing, else from
        /// the health still above the threshold at the raid's damage rate. Infinite when the boss is not losing health.
        /// </summary>
        public static double BlastEtaSeconds(int healthPercent, int threshold, double percentPerSecond, long sinceCrossMilliseconds,
            long? castLandsInMilliseconds = null)
        {
            if (castLandsInMilliseconds is long lands) return Math.Max(0, lands / 1000.0);
            if (sinceCrossMilliseconds >= 0) return Math.Max(0, BlastLandsAfterCrossSeconds - sinceCrossMilliseconds / 1000.0);
            if (percentPerSecond <= 0.01) return double.PositiveInfinity;
            return Math.Max(0, healthPercent - threshold) / percentPerSecond + BlastLandsAfterCrossSeconds;
        }

        /// <summary>One stomp's call and measurement.</summary>
        private sealed class BlastCall
        {
            public int Threshold;
            public string RaidId;
            public double Familiarity;
            public int Seed;
            public Group[] Parties = [];
            public long PlannedCallerId;
            public RaidDiscipline.Call Planned;
            public bool Called;
            public long CalledAt;
            public double Lead;
            public GameBot Caller;
            public long LandsAt;
            public bool CastSeen, Landed;
            public long CastAt;
            public long CrossedAt;
            public List<GameLiving> Exposed;
            public List<GameLiving> Within;
            public GameLiving Center;
            public bool Reported, BackInSaid;
            public long NextPulse;
            public readonly Dictionary<long, RaidDiscipline.Reaction> Reactions = new();
        }

        private static readonly Dictionary<GameNPC, BlastCall> BlastCalls = new();

        private static int CrossedThreshold(GameNPC boss)
        {
            lock (BlastClocks) return BlastClocks.TryGetValue(boss, out var c) ? c.Crossed : 0;
        }

        /// <summary>The stomp the window is about now: the one just crossed, else the next one below his health.</summary>
        private static int WindowThreshold(Tactic t, GameNPC boss, long sinceCross) =>
            sinceCross >= 0 && sinceCross < BlastAftermathMilliseconds && CrossedThreshold(boss) is int crossed and > 0
                ? crossed : NextBlastBelow(t.BlastThresholds, boss.HealthPercent);

        /// <summary>
        /// Advances the boss's call (throttled to five times a second per boss; any raider near him drives it): opens it with
        /// the window, makes the call when the stomp is its lead away, measures the landing and says "back in". Returns the
        /// boss's current call, or null (<see cref="CalledNow"/>: made for the stomp in the window; until then nobody moves).
        /// </summary>
        private static BlastCall PulseBlastCall(GameNPC boss, GameLiving raider, long now)
        {
            Tactic t = For(boss);
            if (t is not { HasBlast: true } || !CalledBlastTypes.TryGetValue(boss.GetType().Name, out string signalName)) return null;
            BlastCall call;
            lock (BlastCalls)
            {
                BlastCalls.TryGetValue(boss, out call);
                if (call != null && now < call.NextPulse) return call;
            }
            long since = SinceBlastCross(boss, now);
            double rate = BlastRate(boss, now);
            bool inWindow = boss.InCombat && InBlastWindow(t.BlastThresholds, boss.HealthPercent, since, rate);
            int threshold = WindowThreshold(t, boss, since);
            EncounterSignal cast = EncounterSignals.Active(boss, signalName, now);
            // A new stomp's window: who calls it, how well the realm knows him.
            if (inWindow && threshold > 0 && (call == null || call.Threshold != threshold))
            {
                var raid = AutonomousRealmRaid.RaidOf(raider?.Group);
                string raidId = raid?.Id ?? AutonomousRealmRaid.RaidIdForBoss(boss.GetType().Name);
                Group[] parties = raid?.Parties ?? (raider?.Group != null ? [raider.Group] : []);
                double familiarity = RaidDiscipline.FamiliarityOf(raidId);
                int seed = RaidDiscipline.SeedOf(raidId);
                GameBot planned = CallerOf(parties);
                long plannedId = planned?.DatabaseID ?? 0;
                var fresh = new BlastCall
                {
                    Threshold = threshold, RaidId = raidId, Familiarity = familiarity, Seed = seed, Parties = parties,
                    PlannedCallerId = plannedId, Planned = RaidDiscipline.CallFor(plannedId, MechanicKey(boss), threshold, seed, familiarity,
                        RaidDiscipline.ForcedLate(RaidDiscipline.ForceLateCallAt, threshold)),
                };
                lock (BlastCalls)
                {
                    BlastCalls.TryGetValue(boss, out call);
                    if (call == null || call.Threshold != threshold)
                    {
                        if (BlastCalls.Count > 64) BlastCalls.Clear();
                        BlastCalls[boss] = call = fresh;
                    }
                }
            }
            if (call == null) return null;
            bool makeCall = false, snapshot = false, report = false, backIn = false;
            double eta = 0;
            // While he casts it, when it lands is his cast's own end (the cast time his casting speed gives, not the
            // spell's 6 s: duo16, it landed 5 s after the cast began).
            // Only the blast itself counts (duo17: at 80% his cast of something else ended first and was taken as the landing).
            SpellHandler handler = boss.castingComponent?.IsCasting == true ? boss.castingComponent.SpellHandler : null;
            bool casting = handler?.Spell is { } spell && spell.Radius == t.BlastRadius;
            long? castEnd = casting && handler.CastStartTick > 0 ? handler.CastStartTick + handler.CalculateCastingTime() : null;
            lock (BlastCalls)
            {
                call.NextPulse = now + 200;
                if (since >= 0 && CrossedThreshold(boss) == call.Threshold && call.CrossedAt == 0) call.CrossedAt = now - since;
                if (cast != null && !call.CastSeen && !call.Landed && call.CrossedAt > 0 && cast.At >= call.CrossedAt - 1_000)
                {
                    call.CastSeen = true;
                    call.CastAt = cast.At;
                    call.LandsAt = cast.Lands;
                }
                if (call.CastSeen && !call.Landed && casting && castEnd is long end) call.LandsAt = end;
                if (!call.Called && inWindow && call.Threshold == threshold)
                {
                    eta = BlastEtaSeconds(boss.HealthPercent, call.Threshold, rate, CrossedThreshold(boss) == call.Threshold ? since : -1,
                        call.CastSeen ? call.LandsAt - now : null);
                    if (eta <= call.Planned.LeadSeconds) { call.Called = makeCall = true; call.CalledAt = now; call.Lead = eta; }
                }
                if (call.Called && !call.Landed)
                {
                    if (call.CastSeen)
                    {
                        // The raiders inside it are counted on every look while he casts; the last count before his cast
                        // ends is the one at the landing.
                        if (casting) snapshot = true;
                        else if (call.Within != null || now > call.LandsAt + 1_000) { call.Landed = true; call.LandsAt = Math.Min(now, call.LandsAt + 1_000); }
                    }
                    // No cast seen: the landing is taken as the crossing plus its usual delay.
                    else if (call.CrossedAt > 0 && now >= call.CrossedAt + (long)(BlastLandsAfterCrossSeconds * 1000) - BlastLandingSnapshotLeadMilliseconds)
                    {
                        call.LandsAt = call.CrossedAt + (long)(BlastLandsAfterCrossSeconds * 1000);
                        call.Landed = snapshot = true;
                    }
                }
                if (call.Landed && !snapshot && !call.Reported && now >= call.LandsAt + BlastLandingCountAfterMilliseconds)
                    call.Reported = report = true;
                if (call.Reported && !report && !call.BackInSaid &&
                    now >= call.LandsAt + BlastBackInMilliseconds + (long)(1_000 * RaidDiscipline.Roll(
                        RaidDiscipline.Key(call.PlannedCallerId, MechanicKey(boss), call.Threshold, call.Seed), 41)))
                    call.BackInSaid = backIn = true;
            }
            if (makeCall) MakeBlastCall(boss, t, call, now);
            if (snapshot)
            {
                GameLiving center = boss.TargetObject as GameLiving ?? boss;
                List<GameLiving> within = RaidersWithin(boss, center, t.BlastRadius);
                // A count that finished after the landing would hold only the survivors.
                lock (BlastCalls) if (!call.Landed || call.Within == null) { call.Within = within; call.Center = center; }
            }
            if (report) ReportBlastLanding(boss, t, call);
            if (backIn) SayToRaid(call, CallerOf(call.Parties), RaidDiscipline.BackInLine(call.PlannedCallerId, MechanicKey(boss), call.Threshold, call.Seed));
            return call;
        }

        /// <summary>The call has been made for the stomp the window is about now.</summary>
        private static bool CalledNow(BlastCall call, Tactic t, GameNPC boss, long now)
        {
            if (call == null) return false;
            int threshold = WindowThreshold(t, boss, SinceBlastCross(boss, now));
            lock (BlastCalls) return call.Called && call.Threshold == threshold;
        }

        private static string MechanicKey(GameNPC boss) => boss.GetType().Name + "/blast";

        /// <summary>The raid's caller: the first party's leader, else the next living party leader (a bot), else any living
        /// gamebot of the raid.</summary>
        private static GameBot CallerOf(Group[] parties)
        {
            foreach (Group g in parties ?? [])
                if (g?.LivingLeader is GameBot leader && leader.IsAlive && leader.ObjectState == GameObject.eObjectState.Active) return leader;
            foreach (Group g in parties ?? [])
                if (g?.GetMembersInTheGroup().OfType<GameBot>().FirstOrDefault(b => b.IsAlive) is GameBot any) return any;
            return null;
        }

        private static void MakeBlastCall(GameNPC boss, Tactic t, BlastCall call, long now)
        {
            GameBot caller = CallerOf(call.Parties);
            GameLiving center = boss.TargetObject as GameLiving ?? boss;
            List<GameLiving> exposed = RaidersWithin(boss, center, t.BlastRadius);
            lock (BlastCalls) { call.Caller = caller; call.Exposed = exposed; }
            if (Log.IsInfoEnabled)
                Log.Info($"EPIC_BLAST_CALL boss=\"{boss.Name}\" threshold={call.Threshold} health={boss.HealthPercent} " +
                         $"caller=\"{caller?.Name ?? "none"}\" plannedCaller={call.PlannedCallerId} lead={call.Lead:0.0}s " +
                         $"planned={call.Planned.LeadSeconds:0.0}s late={call.Planned.Late} familiarity={call.Familiarity:0.00} " +
                         $"seed={call.Seed} raid={call.RaidId ?? "none"} exposed={exposed.Count} parties={call.Parties.Length}");
            if (caller != null)
                SayToRaid(call, caller, RaidDiscipline.OutLine(call.Planned, call.Threshold, call.PlannedCallerId, MechanicKey(boss),
                    call.Threshold, call.Seed, call.Familiarity));
        }

        /// <summary>
        /// The call goes out on what a real player seated in the raid would see. Bot raids use no BattleGroup (a raid is a
        /// set of /group parties, AutonomousRealmRaid.Parties), so the caller's line goes to every raid party's /group.
        /// Only real players receive party chat, so a raid without one hears nothing (the trace still has it).
        /// </summary>
        private static void SayToRaid(BlastCall call, GameBot caller, string text)
        {
            if (caller == null || string.IsNullOrEmpty(text)) return;
            try
            {
                string said = AutonomousBotVoices.StyleFor(caller, text);
                bool heard = false;
                foreach (Group g in call.Parties)
                {
                    if (g == null || !AutonomousLivingServer.GroupHasPlayer(g)) continue;
                    g.SendMessageToGroupMembers(caller, said, eChatType.CT_Group, eChatLoc.CL_ChatWindow);
                    heard = true;
                }
                if (heard) BotChatTranscript.Bot("group", caller.Name, caller.Realm, said);
                if (Log.IsInfoEnabled) Log.Info($"EPIC_BLAST_SAY caller=\"{caller.Name}\" heard={heard} text=\"{said}\"");
            }
            catch (Exception ex)
            {
                Log.Warn($"EPIC_BLAST_SAY failed: {ex.Message}");
            }
        }

        /// <summary>Living raiders of another realm than the boss (gamebots and players) within the radius of the center.</summary>
        private static List<GameLiving> RaidersWithin(GameNPC boss, GameLiving center, int radius)
        {
            var result = new List<GameLiving>();
            try
            {
                ushort search = (ushort)Math.Min(ushort.MaxValue, radius + 400);
                foreach (GameNPC npc in boss.GetNPCsInRadius(search))
                    if (npc is GameBot b && b.IsAlive && b.Realm != boss.Realm && b.Realm != eRealm.None && b.IsWithinRadius(center, radius))
                        result.Add(b);
                foreach (GamePlayer p in boss.GetPlayersInRadius(search))
                    if (p.IsAlive && p.Realm != boss.Realm && p.IsWithinRadius(center, radius))
                        result.Add(p);
            }
            catch (Exception) { /* a measurement never breaks the fight */ }
            return result;
        }

        private static void ReportBlastLanding(GameNPC boss, Tactic t, BlastCall call)
        {
            List<GameLiving> within, exposed;
            GameLiving center;
            lock (BlastCalls) { within = call.Within ?? []; exposed = call.Exposed ?? []; center = call.Center; }
            int died = within.Count(l => !l.IsAlive);
            int exposedDied = exposed.Count(l => !l.IsAlive);
            Group holding = center?.Group;
            int holdingWithin = holding == null ? 0 : within.Count(l => l.Group == holding);
            int holdingDied = holding == null ? 0 : within.Count(l => l.Group == holding && !l.IsAlive);
            if (Log.IsInfoEnabled)
                Log.Info($"EPIC_BLAST_LANDED boss=\"{boss.Name}\" threshold={call.Threshold} source={(call.CastSeen ? "cast" : "crossing")} " +
                         $"afterCross={(call.CrossedAt > 0 ? (call.LandsAt - call.CrossedAt) / 1000.0 : -1):0.0}s " +
                         $"castMs={(call.CastSeen ? call.LandsAt - call.CastAt : -1)} realLead={(call.LandsAt - call.CalledAt) / 1000.0:0.0}s " +
                         $"center=\"{center?.Name}\" within={within.Count} died={died} share={(within.Count == 0 ? 0 : died / (double)within.Count):0.00} " +
                         $"exposedAtCall={exposed.Count} exposedDied={exposedDied} " +
                         $"exposedShare={(exposed.Count == 0 ? 0 : exposedDied / (double)exposed.Count):0.00} " +
                         $"holdingPartyWithin={holdingWithin} holdingPartyDied={holdingDied} lead={call.Lead:0.0}s late={call.Planned.Late} " +
                         $"familiarity={call.Familiarity:0.00}");
        }

        /// <summary>The raider's role for its lapse (healer, caster or archer, melee).</summary>
        public static RaidDiscipline.Role RoleOf(GameBot bot)
        {
            int id = bot?.CharacterClass?.ID ?? 0;
            if (id > 0 && BotPartyRoles.IsHealingClass((eCharacterClass)id)) return RaidDiscipline.Role.Healer;
            return CanShootFlyers(bot) ? RaidDiscipline.Role.Caster : RaidDiscipline.Role.Melee;
        }

        /// <summary>
        /// Whether this raider is on its way out of the called blast now: only after the call plus its own reaction delay, and
        /// never when it lapsed for good. A raider that is not a gamebot (a test's) goes on the call. Logs EPIC_BLAST_REACT once
        /// per raider and stomp; <paramref name="holds"/>: it is in the party the boss fights, which stays in on purpose.
        /// </summary>
        private static bool RaiderGoes(GameNPC boss, BlastCall call, GameLiving raider, long now, bool holds)
        {
            if (raider is not GameBot bot) return !holds;
            RaidDiscipline.Reaction reaction;
            bool first;
            lock (BlastCalls)
            {
                first = !call.Reactions.TryGetValue(bot.DatabaseID, out reaction);
                if (first)
                {
                    BotPersonalityTraits traits = AutonomousBotPersonality.Applies(bot) ? AutonomousBotPersonality.For(bot).Traits
                        : RaidDiscipline.NeutralTraits;
                    reaction = holds ? new RaidDiscipline.Reaction(double.PositiveInfinity, RaidDiscipline.Lapse.Stays, "the party he fights holds him")
                        : RaidDiscipline.ReactionFor(bot.DatabaseID, traits, RoleOf(bot), MechanicKey(boss), call.Threshold, call.Seed, call.Familiarity);
                    call.Reactions[bot.DatabaseID] = reaction;
                }
            }
            if (first && Log.IsInfoEnabled)
            {
                BotPersonalityTraits tr = AutonomousBotPersonality.For(bot).Traits;
                Log.Info($"EPIC_BLAST_REACT boss=\"{boss.Name}\" threshold={call.Threshold} bot={bot.Name} class={bot.CharacterClass?.Name} " +
                         $"role={RoleOf(bot)} " + (reaction.Leaves ? $"delay={reaction.DelaySeconds:0.0}s" : "stayed") +
                         $" lapse={reaction.Lapse} reason=\"{reaction.Reason}\" dist={(int)bot.GetDistanceTo(boss)} risk={tr.Risk} " +
                         $"patience={tr.Patience} sinceCall={(now - call.CalledAt) / 1000.0:0.0}s familiarity={call.Familiarity:0.00}");
            }
            return reaction.Leaves && now >= call.CalledAt + (long)(reaction.DelaySeconds * 1000);
        }

        /// <summary>Tests and the reset: forget every call.</summary>
        private static void ResetBlastCalls()
        {
            lock (BlastCalls) BlastCalls.Clear();
        }
    }
}
