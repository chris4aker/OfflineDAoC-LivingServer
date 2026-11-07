using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using DOL.Events;
using DOL.Logging;

namespace DOL.GS.LoreChannels
{
    /// <summary>
    /// Each realm's first kill of each boss in the lore (Living Server 1.2), for the bestiary (launcher), the /lore codex
    /// and anything that wants to mark the moment (<see cref="FirstKill"/>: the lore engine's realm-first-kill atmosphere
    /// line can listen here). A boss counts for the realm of whoever struck the killing blow (a pet's owner); a GM's
    /// kill or a visiting Epic Special Forces member's does not count. Written once per realm and boss to the realm event
    /// ledger (LoreFirstKillStore), off the game loop. Raid wins already in the ledger count as first kills of their
    /// final boss, so a realm that beat Krondon before 1.2 finds Orylle's page open.
    /// </summary>
    public static class LoreFirstKills
    {
        private static readonly Logger Log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);
        private static readonly object Sync = new();
        private static readonly Dictionary<(int Realm, string Boss), LoreFirstKill> Known = new();
        private static readonly Dictionary<string, LoreBoss> ByClass = new(StringComparer.OrdinalIgnoreCase);
        /// <summary>Plain world spawns by exact name (a boss file's npcNames).</summary>
        private static readonly Dictionary<string, LoreBoss> ByName = new(StringComparer.Ordinal);

        /// <summary>A realm's first kill of a boss, raised once (on the game loop, at the kill).</summary>
        public static event Action<LoreFirstKill, LoreBoss> FirstKill;

        private static string LedgerPath => WorldDatabaseSafety.DataDirectory is { } data
            ? RealmEventRecordStore.PathIn(data) : Path.Combine(AppContext.BaseDirectory, RealmEventRecordStore.FileName);

        public static IReadOnlyList<LoreFirstKill> All
        {
            get { lock (Sync) return Known.Values.ToList(); }
        }

        /// <summary>
        /// Whether a kill is a realm's first: the boss is in the lore, the realm is a real one, the killer is not staff
        /// or a visiting special force, and the realm has none yet. The kill to record, or null.
        /// </summary>
        public static LoreFirstKill Decide(LoreBoss boss, int realm, string killer, bool excluded, ISet<(int, string)> known, DateTime utc)
        {
            if (boss == null || realm is < LoreRealm.Albion or > LoreRealm.Hibernia || excluded || known.Contains((realm, boss.Id)))
                return null;
            return new LoreFirstKill(realm, boss.Id, utc.ToString("O"), killer ?? "", "");
        }

        /// <summary>The ledger's raid wins as first kills of their final bosses (the earliest per realm and boss).</summary>
        public static List<LoreFirstKill> FromRaidWins(ILoreSource source, IEnumerable<RealmEventRecord> wins)
        {
            var kills = new Dictionary<(int, string), LoreFirstKill>();
            foreach (RealmEventRecord win in (wins ?? []).Where(w => LoreChronicle.Kind(w.Outcome, w.Details) == "win" && !string.IsNullOrEmpty(w.EndedUtc))
                         .OrderBy(w => w.EndedUtc, StringComparer.Ordinal))
            {
                if (!LoreChronicle.RaidFinals.TryGetValue(win.EventId, out string[] finals)) continue;
                int realm = LoreRealm.Id(win.Realm);
                foreach (LoreBoss boss in finals.Select(source.FindBossByClass).Where(b => b != null))
                    kills.TryAdd((realm, boss.Id), new LoreFirstKill(realm, boss.Id, win.EndedUtc,
                        LoreChronicle.RaidLeaderIn(win.Details) is { } leader ? $"raid led by {leader}" : "realm raid", win.EventId));
            }
            return kills.Values.ToList();
        }

        public static void Load(IEnumerable<LoreFirstKill> kills)
        {
            lock (Sync)
            {
                Known.Clear();
                foreach (LoreFirstKill kill in (kills ?? []).OrderBy(k => k.KilledUtc, StringComparer.Ordinal))
                    Known.TryAdd((kill.Realm, kill.BossId), kill);
            }
        }

        private static void IndexClasses()
        {
            lock (Sync)
            {
                ByClass.Clear();
                ByName.Clear();
                foreach (LoreBoss boss in LoreChannelHub.Source.AllBosses)
                {
                    foreach (string name in boss.NpcNames)
                        ByName.TryAdd(name, boss);
                    if (string.IsNullOrEmpty(boss.CatalogKey)) continue;
                    string key = boss.CatalogKey ?? "";
                    int slash = key.LastIndexOf('/');
                    ByClass.TryAdd(slash >= 0 ? key[(slash + 1)..] : key, boss);
                }
            }
        }

        [GameServerStartedEvent]
        public static void OnServerStarted(DOLEvent e, object sender, EventArgs args)
        {
            IndexClasses();
            LoreChannelHub.SourceChanged -= IndexClasses;
            LoreChannelHub.SourceChanged += IndexClasses;
            GameEventMgr.AddHandler(GameLivingEvent.Dying, OnDying);
            string path = LedgerPath;
            ILoreSource source = LoreChannelHub.Source;
            Task.Run(() =>
            {
                try
                {
                    var kills = LoreFirstKillStore.Read(path);
                    kills.AddRange(FromRaidWins(source, LoreFirstKillStore.RaidWins(path)));
                    lock (Sync)
                        foreach (LoreFirstKill kill in kills.OrderBy(k => k.KilledUtc, StringComparer.Ordinal))
                            Known.TryAdd((kill.Realm, kill.BossId), kill);
                    Log.Info($"LORE_FIRST_KILLS loaded={Known.Count}");
                }
                catch (Exception exception)
                {
                    Log.Warn("Lore first kills could not be read from the realm event ledger; the bestiary starts empty", exception);
                }
            });
        }

        [GameServerStoppedEvent]
        public static void OnServerStopped(DOLEvent e, object sender, EventArgs args) =>
            GameEventMgr.RemoveHandler(GameLivingEvent.Dying, OnDying);

        private static void OnDying(DOLEvent e, object sender, EventArgs args)
        {
            if (sender is not GameNPC npc || npc is GameBot || args is not DyingEventArgs dying) return;
            LoreBoss boss;
            lock (Sync)
                if (!ByClass.TryGetValue(npc.GetType().Name, out boss) && !ByName.TryGetValue(npc.Name ?? "", out boss)) return;
            GameObject killer = dying.Killer;
            GameLiving credited = LoreBooks.PlayerOf(killer) as GameLiving ?? killer as GameLiving;
            if (credited is GameNPC { Brain: DOL.AI.Brain.IControlledBrain pet } && pet.Owner is GameLiving owner) credited = owner;
            if (credited == null) return;
            bool excluded = credited is GameBot { IsEpicSpecialForce: true } || AutonomousLivingServer.IsStaff(credited);
            LoreFirstKill kill;
            lock (Sync)
            {
                kill = Decide(boss, (int)credited.Realm, credited.Name, excluded, Known.Keys.ToHashSet(), DateTime.UtcNow);
                if (kill == null) return;
                Known[(kill.Realm, kill.BossId)] = kill;
            }
            Log.Info($"LORE_FIRST_KILL realm={LoreRealm.Name(kill.Realm)} boss={boss.Id} killer=\"{kill.Killer}\"");
            string path = LedgerPath;
            Task.Run(() =>
            {
                try { LoreFirstKillStore.Add(path, [kill]); }
                catch (Exception exception) { Log.Warn($"Lore first kill {boss.Id} could not be written; it stays known until the server stops", exception); }
            });
            try { FirstKill?.Invoke(kill, boss); }
            catch (Exception exception) { Log.Warn("A lore first-kill listener failed", exception); }
        }
    }
}
