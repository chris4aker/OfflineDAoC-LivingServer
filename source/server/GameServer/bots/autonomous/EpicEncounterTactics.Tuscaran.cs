using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace DOL.GS
{
    /// <summary>
    /// Tuscaran Glacier's raid rules (1.2, epic wave 2). See docs/encounters/EPIC-ENCOUNTERS.md for each encounter.
    /// <list type="bullet">
    /// <item>King Tuscar and Queen Kula: damage to one heals the other while it is hurt (AllowTuscarTwinHeal), so the raid
    /// kills Kula first (the catalog's "after") and only the King's own target fights him; nobody stands in Kula's frost
    /// circle by her throne (AllowPositionPuzzle), and casters and healers on Kula keep clear of the King's hammers (500
    /// around his target).</item>
    /// <item>Icelord Kvasir: cold heals him (AllowKvasirColdHeal), so no bot casts cold at him; when his primal scream is due,
    /// everyone but his target's party steps 1,050 away from his target (his mesmerize hits 800 around it).</item>
    /// <item>Jailer Vifil: the jail (AllowDisplacement) opens when his two tunnel guardians are dead, so once the fight is on
    /// one or two parties go down the tunnel, kill them and come back; every raider jailed after that walks free.</item>
    /// <item>Fornfrusenen: each shard killed takes a quarter of his health while he is above 35%, so the shards die first;
    /// he takes damage only from within 200 and the shards only from within 400 of his spot, so everyone fights close.</item>
    /// <item>Council Vagn and Nokkvi: Vagn resists 90% of magic, Nokkvi 90% of melee: casters fight Nokkvi, everyone else Vagn.</item>
    /// </list>
    /// Icelord Hakr's and Hurika's throws need no rule: the thrown raider falls by the boss and the healers take it first
    /// (EncounterDisplacementRecovery).
    /// </summary>
    public static partial class EpicEncounterTactics
    {
        /// <summary>The middle of Queen Kula's frost circle and its radius (QueenKulaBrain.PlayerInCenter).</summary>
        public static readonly Vector3 KulaFrostPoint = new(34128, 56095, 11989);
        public const int KulaFrostRadius = 300;
        /// <summary>King Tuscar's hammers and Thor's Might hit 500 around his target.</summary>
        public const int TuscarHammerRadius = 500;
        /// <summary>Kvasir's mesmerize: 800 around his target, 2 s after his scream (KvasirBrain.PrepareMezz / CastMezz).</summary>
        public const int KvasirMezRadius = 800;
        /// <summary>Raiders step out this long before the scream is due, and stay out this long after it.</summary>
        public const int KvasirLeadMilliseconds = 3_000, KvasirAfterMilliseconds = 4_500;
        /// <summary>Fornfrusenen takes damage from within 200 of him; his shards from within 400 of his spot.</summary>
        public const int FornfrusenenReach = 200, FornShardReach = 400;
        public static readonly Vector3 FornShardPoint = new(49617, 32874, 10859);
        /// <summary>A shard's death takes a quarter of his health only above this.</summary>
        public const int FornShardDamageAbove = 35;
        /// <summary>Jailer Vifil's jail (where he sends the raider) and the spot between his two tunnel guardians.</summary>
        public static readonly Vector3 VifilJail = new(16631, 58683, 10858), VifilGuardSpot = new(16544, 58900, 10880);
        public const int VifilGuardSearch = 1_500;

        [DungeonRules]
        private static IEnumerable<DungeonRule> TuscaranRules() =>
        [
            new("KingTuscar", ["KingTuscar", "QueenKula"], TuscarAndKula)
            {
                Summary = "Twin heal: Kula first, only the King's target fights him; out of Kula's frost circle; casters clear of his hammers.",
            },
            new("Kvasir", ["Kvasir"], KvasirScream)
            {
                Summary = "No cold at him (it heals him); out of 800 around his target when his scream is due.",
                Forbids = [eDamageType.Cold],
                ForbidsWhile = _ => EpicEncounterMechanics.AllowKvasirColdHeal,
                Act = (bot, boss, _) =>
                {
                    if (EpicEncounterMechanics.AllowKvasirColdHeal && IsMagicDealer(bot.CharacterClass?.ID ?? 0))
                        MechanicCall(bot, "epic_kvasir_cold", boss, null);
                },
            },
            new("Jailer", ["Jailer"], VifilJailRescue)
            {
                Summary = "Jail rescue: one or two parties kill the tunnel guardians, then every jailed raider walks free.",
                Act = StartVifilRescue,
            },
            new("Fornfrusenen", ["Fornfrusenen", "FornfrusenenShard"], FornfrusenenShards)
            {
                Summary = "Shards first while he is above 35% (each takes a quarter of his health); everyone fights from close by.",
            },
            new("Vagn", ["Vagn", "Nokkvi"], CouncilSplit)
            {
                Summary = "Casters on Nokkvi (90% melee resist), everyone else on Vagn (90% magic resist).",
            },
        ];

        // ---- King Tuscar and Queen Kula ---------------------------------------------------------------------------------

        private static Advice? TuscarAndKula(RuleContext c)
        {
            GameNPC king = c.Find("KingTuscar"), queen = c.Find("QueenKula");
            if (queen != null && On(nameof(EpicEncounterMechanics.AllowPositionPuzzle), "QueenKula") &&
                HazardEscape(c.At, KulaFrostPoint, KulaFrostRadius) is Vector3 outOfCircle)
                return new(BotAction.Reposition, c.Target, outOfCircle, $"out of Kula's frost circle ({KulaFrostRadius})", "epic_frost_circle");
            if (c.Target == king && queen != null && EpicEncounterMechanics.AllowTuscarTwinHeal && king.TargetObject != c.Raider)
                return new(BotAction.Retarget, queen, null, "twin heal: damage to the King heals Kula; only his target fights him", "epic_twin_heal");
            if (c.Target == queen && king?.TargetObject is GameLiving kingTarget && !SameParty(c.Raider, kingTarget) &&
                SpreadSpot(c.At, PosOf(king), PosOf(kingTarget), TuscarHammerRadius, c.AtRange) is Vector3 clear)
                return new(BotAction.Reposition, queen, clear, $"clear of the King's hammers ({TuscarHammerRadius} around \"{kingTarget.Name}\")");
            return default;
        }

        // ---- Icelord Kvasir ------------------------------------------------------------------------------------------

        /// <summary>When Kvasir's next scream is due (game time), or -1: his scream timer's tick (KvasirBrain stores it as
        /// "kvasir_prepareMezz"). Settable for tests.</summary>
        public static Func<GameNPC, long> KvasirScreamAt = boss =>
            boss?.TempProperties.GetProperty<ECSGameTimer>("kvasir_prepareMezz") is { } timer ? timer.NextTick : -1;

        /// <summary>The raid stands clear of his target from <see cref="KvasirLeadMilliseconds"/> before the scream to
        /// <see cref="KvasirAfterMilliseconds"/> after the mesmerize (2 s after the scream).</summary>
        public static bool InKvasirScreamWindow(long now, long screamAt) =>
            screamAt > 0 && now >= screamAt - KvasirLeadMilliseconds && now <= screamAt + 2_000 + KvasirAfterMilliseconds;

        private static Advice? KvasirScream(RuleContext c)
        {
            if (!InKvasirScreamWindow(c.Now, KvasirScreamAt(c.Target)) || c.Target.TargetObject is not GameLiving anchor ||
                SameParty(c.Raider, anchor))
                return default;
            Vector3? spot = AwayFrom(c.At, PosOf(anchor), KvasirMezRadius);
            // Out already: melee wait there (they would walk back in to swing), casters and healers fight on from range.
            if (spot == null && !c.AtRange) spot = c.At;
            return spot is Vector3 at
                ? new(BotAction.Reposition, c.Target, at, $"scream: out of {KvasirMezRadius} around \"{anchor.Name}\"", "epic_kvasir_scream")
                : default;
        }

        // ---- Jailer Vifil ----------------------------------------------------------------------------------------------

        /// <summary>The living tunnel guardians at Vifil's jail. Settable for tests.</summary>
        public static Func<GameNPC, IEnumerable<GameNPC>> VifilGuardians = boss => GuardiansNear(boss, "JailerAdd", VifilGuardSpot, VifilGuardSearch);

        private static readonly ConcurrentDictionary<(GameNPC, string), (long Tick, GameNPC[] Npcs)> GuardianCache = new();

        private static GameNPC[] GuardiansNear(GameNPC boss, string type, Vector3 spot, int radius)
        {
            if (boss?.CurrentRegion == null) return [];
            long now = GameLoop.GameLoopTime;
            if (GuardianCache.TryGetValue((boss, type), out var hit) && now - hit.Tick < 2_000) return hit.Npcs;
            GameNPC[] found = WorldMgr.GetNPCsFromRegion(boss.CurrentRegionID)
                .Where(n => n != null && n.GetType().Name == type && Present(n) &&
                            Vector2.Distance(new(n.X, n.Y), Flat(spot)) <= radius).ToArray();
            if (GuardianCache.Count > 64) GuardianCache.Clear();
            GuardianCache[(boss, type)] = (now, found);
            return found;
        }

        /// <summary>The errand down Vifil's tunnel.</summary>
        public static readonly ErrandJob VifilRescue = new("vifil-jail", VifilGuardSpot, boss => VifilGuardians(boss), "epic_jail_team");

        /// <summary>How many parties go: none for a raid of fewer than four parties at the fight, one up to seven, else two.</summary>
        public static int RescueTeamsWanted(int partiesAtFight) => partiesAtFight < 4 ? 0 : partiesAtFight < 8 ? 1 : 2;

        public sealed class RescueClaim
        {
            public readonly List<Group> Teams = new();
            public long LastStart;
        }

        private static readonly ConcurrentDictionary<GameNPC, RescueClaim> Rescues = new();
        public static void ForgetRescues() => Rescues.Clear();

        /// <summary>A rescue that sent nobody back for this long after its teams finished, with guardians still alive, sends again.</summary>
        public const long RescueRetryMilliseconds = 60_000;

        /// <summary>
        /// Whether <paramref name="party"/> is (or now becomes) a rescue team for <paramref name="boss"/>: a party of bots
        /// (no real player in it), not the one the boss fights, while fewer than <see cref="RescueTeamsWanted"/> are claimed.
        /// <paramref name="teamBusy"/> says whether a claimed team still has a bot on the errand.
        /// </summary>
        public static bool ClaimRescue(RescueClaim claim, Group party, bool partyHasPlayer, bool holdsBoss, int partiesAtFight, long now,
            Func<Group, bool> teamBusy)
        {
            if (claim == null || party == null) return false;
            lock (claim)
            {
                if (claim.Teams.Contains(party)) return true;
                if (claim.Teams.Count > 0 && !claim.Teams.Any(teamBusy) && now - claim.LastStart >= RescueRetryMilliseconds)
                    claim.Teams.Clear();
                if (partyHasPlayer || holdsBoss || claim.Teams.Count >= RescueTeamsWanted(partiesAtFight)) return false;
                claim.Teams.Add(party);
                claim.LastStart = now;
                return true;
            }
        }

        private static bool JailOn(GameNPC boss) => EpicEncounterMechanics.AllowDisplacementFor(boss);

        /// <summary>The raid's parties at the fight: distinct parties of living bots of the raider's realm within 2,500.</summary>
        private static int PartiesAt(GameNPC boss, GameLiving raider, IReadOnlyList<GameNPC> live) =>
            live.OfType<GameBot>().Where(b => b.IsAlive && b.Group != null && b.Realm == raider.Realm && boss.IsWithinRadius(b, 2_500))
                .Select(b => b.Group).Distinct().Count();

        private static Advice? VifilJailRescue(RuleContext c)
        {
            if (!JailOn(c.Target) || c.Raider.Group == null || !Rescues.TryGetValue(c.Target, out RescueClaim claim)) return default;
            bool team;
            lock (claim) team = claim.Teams.Contains(c.Raider.Group);
            if (!team || !VifilGuardians(c.Target).Any()) return default;
            return new(BotAction.StandOff, c.Target, VifilGuardSpot, "jail rescue: down the tunnel for the guardians", "epic_jail_team");
        }

        private static void StartVifilRescue(GameBot bot, GameNPC boss, IReadOnlyList<GameNPC> live)
        {
            if (!JailOn(boss) || bot.Group == null || OnErrand(bot) || !boss.InCombat || !VifilGuardians(boss).Any()) return;
            RescueClaim claim = Rescues.GetOrAdd(boss, _ => new RescueClaim());
            if (EpicEncounterMechanics.FightIsOver(boss))
            {
                lock (claim) claim.Teams.Clear();
                return;
            }
            bool holds = Holds(bot, boss);
            if (!ClaimRescue(claim, bot.Group, AutonomousLivingServer.GroupHasPlayer(bot.Group), holds, PartiesAt(boss, bot, live),
                    GameLoop.GameLoopTime, g => g.GetMembersInTheGroup().OfType<GameBot>().Any(OnErrand)))
            {
                // The real player's party hears that a team went for the guards.
                lock (claim)
                    if (claim.Teams.Count > 0 && AutonomousLivingServer.GroupHasPlayer(bot.Group))
                        MechanicCall(bot, "epic_jail_team_away", boss, claim.Teams[0].LivingLeader);
                return;
            }
            if (StartErrand(bot.Group, boss, VifilRescue) > 0 && Log.IsInfoEnabled)
                Log.Info($"EPIC_TACTIC_RESCUE boss=\"{boss.Name}\" team=\"{bot.Group.LivingLeader?.Name}\" guardians={VifilGuardians(boss).Count()}");
        }

        // ---- Fornfrusenen ----------------------------------------------------------------------------------------------

        private static Advice? FornfrusenenShards(RuleContext c)
        {
            GameNPC boss = c.Find("Fornfrusenen");
            if (c.Target == boss)
            {
                GameNPC[] shards = c.All("FornfrusenenShard");
                if (boss.HealthPercent > FornShardDamageAbove && shards.Length > 0)
                {
                    GameNPC shard = shards.OrderBy(s => s.ObjectID).ElementAt(AssignedIndex(c.PartyIndex, shards.Length));
                    return new(BotAction.Retarget, shard, null, $"shards first: each takes a quarter of his health ({shards.Length} left)",
                        "epic_forn_shards");
                }
                return CloseIn(c.At, PosOf(boss), FornfrusenenReach) is Vector3 close
                    ? new(BotAction.Reposition, boss, close, $"close in: he takes damage only within {FornfrusenenReach}", "epic_close_in")
                    : default;
            }
            // A shard.
            if (boss != null && boss.HealthPercent <= FornShardDamageAbove)
                return new(BotAction.Retarget, boss, null, $"he is at {boss.HealthPercent}%: shards no longer hurt him, kill him");
            return CloseIn(c.At, FornShardPoint, FornShardReach) is Vector3 near
                ? new(BotAction.Reposition, c.Target, near, $"close in: shards take damage only within {FornShardReach} of his spot", "epic_close_in")
                : default;
        }

        // ---- Council Vagn and Nokkvi -------------------------------------------------------------------------------------

        private static Advice? CouncilSplit(RuleContext c)
        {
            if (c.ClassId <= 0) return default;
            bool magic = IsMagicDealer(c.ClassId);
            if (c.Target.GetType().Name == "Vagn" && magic && c.Find("Nokkvi") is { } nokkvi)
                return new(BotAction.Retarget, nokkvi, null, "Vagn resists 90% of magic: casters on Nokkvi", "epic_council_split");
            if (c.Target.GetType().Name == "Nokkvi" && !magic && c.Find("Vagn") is { } vagn)
                return new(BotAction.Retarget, vagn, null, "Nokkvi resists 90% of melee: melee on Vagn", "epic_council_split");
            return default;
        }
    }
}
