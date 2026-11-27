﻿using System;
using System.Collections.Generic;
using System.Threading;
using DOL.AI;
using DOL.AI.Brain;
using DOL.Database;
using DOL.GS;
using DOL.GS.PacketHandler;
using DOL.Logging;

// Port of OpenDAoC-Core 0837f95e1 (ParthananFarm rewrite, GPL-3.0 code by Baptiste Marie), #565 step 4b.
//
// Three farms in Hibernia (region 200), one database-driven controller each: the controller row's PackageID names the
// farm (ParthananFarmConnacht, ParthananFarmLoughDerg, ParthananFarmLoughGur), its trash carries Parthanan<Farm> and the
// boss it spawns ParthananBoss<Farm>. After 60-120 trash kills the farm "sacrifices": the trash near the controller
// gathers, the Amalgamate Parthanan rises (immune while at least 5 still walk in and die), then fights with the trash
// hidden; its death starts the next cycle. The release world already holds these rows (3 controllers of this class,
// 64 trash rows); the old code only had the classes ParthananFarmController1/2/2b/3/3b, which no row uses, so until now
// the controllers loaded as plain NPCs and the boss never spawned.
//
// Our adaptations: the boss keeps the old damage rule (immune to magic, to archery and to NPC ranged attacks; an unarmed
// player or pet does no damage) so it isn't weakened; AmalgamateParthananDamage.UseUpstreamRule switches to upstream's
// (only magic immune). Brains are set in AddToWorld as our scripts do, the trash keeps its old aggro and its template row
// is saved once per session, and trash deaths are counted in ProcessDeath (once per death).

namespace DOL.GS
{
    public class Parthanan : TimeDependentSpawnNpc
    {
        public const int TEMPLATE_ID = 60164845;

        private bool _templateRowSaved;

        public override bool AddToWorld()
        {
            INpcTemplate npcTemplate = NpcTemplateMgr.GetTemplate(TEMPLATE_ID);

            if (npcTemplate != null)
                LoadTemplate(npcTemplate);

            LoadedFromScript = false; // load from database

            // Persist the template-loaded row once per session. Every respawn re-runs AddToWorld on this same object with
            // identical values, and the synchronous write stalled the respawn timer for hundreds of ms.
            if (!_templateRowSaved)
            {
                SaveIntoDatabase();
                _templateRowSaved = true;
            }

            return base.AddToWorld();
        }

        protected override ABrain CreateBrain()
        {
            return new ParthananBrain();
        }

        public override void ProcessDeath(GameObject killer)
        {
            ParthananFarmRegistry.GetByTrashPackageId(PackageID)?.OnTrashDeath(this);
            base.ProcessDeath(killer);
        }
    }

    public class AmalgamateParthanan : GameNPC
    {
        public const int TEMPLATE_ID = 60157792;

        public override int MaxHealth => 3000;

        private ParthananFarmState FarmState => ParthananFarmRegistry.GetByBossPackageId(PackageID);

        public override bool AddToWorld()
        {
            INpcTemplate npcTemplate = NpcTemplateMgr.GetTemplate(TEMPLATE_ID);

            if (npcTemplate != null)
                LoadTemplate(npcTemplate);

            SetOwnBrain(new AmalgamateParthananBrain());
            LoadedFromScript = true;
            RespawnInterval = -1;

            if (!base.AddToWorld())
                return false;

            _ = new ECSGameTimer(this, RitualEffectLoop, 500);
            return true;
        }

        public override void TakeDamage(GameObject source, eDamageType damageType, int damageAmount, int criticalAmount)
        {
            AmalgamateParthananDamage.Outcome outcome = AmalgamateParthananDamage.For(AmalgamateParthananDamage.Describe(source), damageType,
                AmalgamateParthananDamage.UseUpstreamRule);

            switch (outcome)
            {
                case AmalgamateParthananDamage.Outcome.Damage:
                {
                    base.TakeDamage(source, damageType, damageAmount, criticalAmount);
                    return;
                }
                case AmalgamateParthananDamage.Outcome.Immune:
                {
                    GamePlayer player = source as GamePlayer ?? (source as GameSummonedPet)?.Owner as GamePlayer;
                    player?.Out.SendMessage($"{Name} is immune to this form of attack!", eChatType.CT_SpellResisted, eChatLoc.CL_ChatWindow);
                    base.TakeDamage(source, damageType, 0, 0);
                    return;
                }
                default:
                    return; // the old rule: an unarmed player or pet, or a source that is no living, does nothing at all
            }
        }

        public override void StartAttack(GameObject target)
        {
            ParthananFarmState state = FarmState;

            if (state != null && state.BossIsImmuneToDamage)
                return;

            base.StartAttack(target);
        }

        public override bool HasAbility(string keyName)
        {
            if (IsAlive && keyName == GS.Abilities.CCImmunity)
                return true;

            if (IsAlive && keyName == GS.Abilities.DamageImmunity)
            {
                ParthananFarmState state = FarmState;

                if (state != null && state.BossIsImmuneToDamage)
                    return true;
            }

            return base.HasAbility(keyName);
        }

        public override void ProcessDeath(GameObject killer)
        {
            ParthananFarmRegistry.GetByBossPackageId(PackageID)?.OnBossDeath();
            base.ProcessDeath(killer);
        }

        private int RitualEffectLoop(ECSGameTimer timer)
        {
            if (!IsAlive)
                return 0;

            ParthananFarmState state = FarmState;

            if (state != null && state.BossIsImmuneToDamage)
            {
                foreach (GamePlayer player in GetPlayersInRadius(WorldMgr.VISIBILITY_DISTANCE))
                    player.Out.SendSpellCastAnimation(this, 2909, 1);

                return 1500;
            }

            foreach (GamePlayer player in GetPlayersInRadius(WorldMgr.VISIBILITY_DISTANCE))
                player?.Out.SendSpellEffectAnimation(this, this, 6159, 0, false, 0x01);

            return 0;
        }
    }

    /// <summary>
    /// What the Amalgamate Parthanan takes from an attack (pure). The old rule (before the rewrite) is kept by default:
    /// magic is always resisted; a player's or pet's recurve or short bow is resisted and an unarmed player or pet does
    /// nothing; an NPC's ranged attack (gamebots and charmed mobs included) is resisted. Upstream's rule (0837f95e1) resists
    /// magic only, so archery and unarmed attacks hurt it. Never weaken a boss without the owner (#565, AGENTS.md).
    /// </summary>
    public static class AmalgamateParthananDamage
    {
        /// <summary>
        /// Owner switch for #565: false keeps the old immunities (archery, NPC ranged attacks, unarmed); true takes
        /// upstream's rule (only magic resisted). The one line to flip if the owner chooses upstream's.
        /// </summary>
        public static bool UseUpstreamRule = false;

        public enum Outcome
        {
            Damage,
            Immune, // no damage, the attacker is told
            Ignored // nothing happens (no damage, no aggro): the old rule for an unarmed player or pet
        }

        public enum AttackerKind
        {
            NotLiving,
            Player,
            Pet,
            Npc,
            OtherLiving
        }

        public readonly record struct Attacker(AttackerKind Kind, bool HasWeapon, bool PlayerBow, bool NpcRanged);

        public static Attacker Describe(GameObject source)
        {
            switch (source)
            {
                case GamePlayer player:
                    return new(AttackerKind.Player, player.ActiveWeapon != null, IsOldImmuneBow(player.ActiveWeapon), false);
                case GameSummonedPet pet:
                    return new(AttackerKind.Pet, pet.ActiveWeapon != null, IsOldImmuneBow(pet.ActiveWeapon), false);
                case GameNPC npc:
                    return new(AttackerKind.Npc, npc.ActiveWeapon != null, false, npc.ActiveWeapon != null && npc.ActiveWeaponSlot == eActiveWeaponSlot.Distance);
                case GameLiving:
                    return new(AttackerKind.OtherLiving, false, false, false);
                default:
                    return new(AttackerKind.NotLiving, false, false, false);
            }
        }

        public static bool IsMagic(eDamageType damageType) => damageType is
            eDamageType.Body or
            eDamageType.Cold or
            eDamageType.Energy or
            eDamageType.Heat or
            eDamageType.Matter or
            eDamageType.Spirit;

        public static Outcome For(Attacker attacker, eDamageType damageType, bool upstreamRule)
        {
            if (upstreamRule)
            {
                if (attacker.Kind == AttackerKind.NotLiving)
                    return Outcome.Damage;

                return IsMagic(damageType) ? Outcome.Immune : Outcome.Damage;
            }

            switch (attacker.Kind)
            {
                case AttackerKind.Player:
                case AttackerKind.Pet:
                {
                    if (!attacker.HasWeapon)
                        return Outcome.Ignored;

                    return IsMagic(damageType) || attacker.PlayerBow ? Outcome.Immune : Outcome.Damage;
                }
                case AttackerKind.Npc:
                    return attacker.NpcRanged || IsMagic(damageType) ? Outcome.Immune : Outcome.Damage;
                default:
                    return Outcome.Ignored;
            }
        }

        private static bool IsOldImmuneBow(DbInventoryItem weapon) =>
            weapon != null && (weapon.Object_Type == (int) eObjectType.RecurvedBow || weapon.Object_Type == (int) eObjectType.Fired);
    }

    public class ParthananFarmController : GameNPC
    {
        public override bool IsVisibleToPlayers => true;

        public override bool AddToWorld()
        {
            SetOwnBrain(new ParthananFarmControllerBrain());
            return base.AddToWorld();
        }
    }
}

namespace DOL.AI.Brain
{
    public sealed class ParthananFarmConfig
    {
        public string FarmId { get; init; }
        public string TrashPackageId { get; init; }
        public string BossPackageId { get; init; }
        public int MinKillsToSacrifice { get; init; } = 60;
        public int MaxKillsToSacrifice { get; init; } = 120;
        public int MinionsRequiredForSacrifice { get; init; } = 5;
        public ushort GatherScanRadius { get; init; } = 3000;
        public ushort BossDuplicateCheckRadius { get; init; } = 8000;
    }

    public static class ParthananFarmDefinitions
    {
        public static readonly IReadOnlyList<ParthananFarmConfig> Configs =
        [
            new()
            {
                FarmId = "ParthananFarmConnacht",
                TrashPackageId = "ParthananConnacht",
                BossPackageId = "ParthananBossConnacht",
                MinKillsToSacrifice = 60,
                MaxKillsToSacrifice = 120,
            },
            new()
            {
                FarmId = "ParthananFarmLoughDerg",
                TrashPackageId = "ParthananLoughDerg",
                BossPackageId = "ParthananBossLoughDerg",
                MinKillsToSacrifice = 60,
                MaxKillsToSacrifice = 120,
            },
            new()
            {
                FarmId = "ParthananFarmLoughGur",
                TrashPackageId = "ParthananLoughGur",
                BossPackageId = "ParthananBossLoughGur",
                MinKillsToSacrifice = 60,
                MaxKillsToSacrifice = 120,
            }
        ];

        public static ParthananFarmConfig GetByFarmId(string farmId)
        {
            if (string.IsNullOrEmpty(farmId))
                return null;

            foreach (ParthananFarmConfig config in Configs)
            {
                if (string.Equals(config.FarmId, farmId, StringComparison.OrdinalIgnoreCase))
                    return config;
            }

            return null;
        }
    }

    /// <summary>
    /// One farm's cycle, behind one lock (the old code kept it in static fields shared by every thread). The world
    /// queries (the gather scan and the boss spawn) go through the controller body; the cycle itself is plain state.
    /// </summary>
    public sealed class ParthananFarmState
    {
        private readonly Lock _lock = new();
        private readonly HashSet<GameNPC> _gatheringMinions = new();
        private readonly Func<int, int, int> _random;

        private int _killCount;
        private int _sacrificeThreshold;
        private bool _canMarchAndDie;
        private bool _bossSpawned;

        private volatile bool _trashVisible = true;
        private volatile bool _sacrificing;

        public ParthananFarmConfig Config { get; }
        public GameNPC ControllerBody { get; }
        public ParthananFarmPhase Phase { get; private set; }

        public bool TrashIsVisible => _trashVisible;
        public bool BossIsImmuneToDamage => _sacrificing;
        public int KillCount { get { lock (_lock) return _killCount; } }
        public int SacrificeThreshold { get { lock (_lock) return _sacrificeThreshold; } }

        public ParthananFarmState(ParthananFarmConfig config, GameNPC controllerBody, Func<int, int, int> random = null)
        {
            Config = config;
            ControllerBody = controllerBody;
            _random = random ?? Util.Random;
            ResetCycleUnsafe();
        }

        public bool ShouldMarchAndDie(GameNPC npc)
        {
            lock (_lock)
            {
                return _canMarchAndDie && _gatheringMinions.Contains(npc);
            }
        }

        private void ResetCycleUnsafe()
        {
            _killCount = 0;
            _sacrificeThreshold = _random(Config.MinKillsToSacrifice, Config.MaxKillsToSacrifice);
            Phase = ParthananFarmPhase.Farming;
            _canMarchAndDie = false;
            _bossSpawned = false;
            _gatheringMinions.Clear();
            _trashVisible = true;
            _sacrificing = false;
        }

        public void OnTrashDeath(GameNPC npc)
        {
            lock (_lock)
            {
                if (Phase is ParthananFarmPhase.Farming)
                {
                    _killCount++;

                    if (_killCount >= _sacrificeThreshold)
                    {
                        Phase = ParthananFarmPhase.Sacrificing;
                        _sacrificing = true;
                    }
                }
                else if (Phase is ParthananFarmPhase.Sacrificing)
                {
                    if (_gatheringMinions.Remove(npc) && _gatheringMinions.Count == 0 && _bossSpawned)
                    {
                        Phase = ParthananFarmPhase.BossActive;
                        _canMarchAndDie = false;
                        _sacrificing = false;
                        _trashVisible = false;
                    }
                }
            }
        }

        public void ScanForMinions()
        {
            List<GameNPC> found = new();

            foreach (GameNPC npc in ControllerBody.GetNPCsInRadius(Config.GatherScanRadius))
            {
                if (npc is Parthanan trash && trash.IsAlive && trash.PackageID == Config.TrashPackageId)
                    found.Add(trash);
            }

            if (AddGatheringMinions(found))
                SpawnBoss();
        }

        /// <summary>The gather step of a scan: true when the boss has to be spawned now (once per cycle).</summary>
        public bool AddGatheringMinions(IEnumerable<GameNPC> minions)
        {
            lock (_lock)
            {
                if (Phase is not ParthananFarmPhase.Sacrificing)
                    return false;

                foreach (GameNPC minion in minions)
                    _gatheringMinions.Add(minion);

                if (_gatheringMinions.Count < Config.MinionsRequiredForSacrifice)
                    return false;

                _canMarchAndDie = true;

                if (_bossSpawned)
                    return false;

                _bossSpawned = true;
                return true;
            }
        }

        private void SpawnBoss()
        {
            foreach (GameNPC npc in ControllerBody.GetNPCsInRadius(Config.BossDuplicateCheckRadius))
            {
                if (npc is AmalgamateParthanan existing && existing.PackageID == Config.BossPackageId)
                    return;
            }

            new AmalgamateParthanan()
            {
                X = ControllerBody.X,
                Y = ControllerBody.Y,
                Z = ControllerBody.Z,
                Heading = ControllerBody.Heading,
                CurrentRegion = ControllerBody.CurrentRegion,
                PackageID = Config.BossPackageId
            }.AddToWorld();
        }

        public void OnBossDeath()
        {
            lock (_lock)
            {
                ResetCycleUnsafe();
            }
        }
    }

    public static class ParthananFarmRegistry
    {
        private static readonly Dictionary<string, ParthananFarmState> ByTrashPackageId = new();
        private static readonly Dictionary<string, ParthananFarmState> ByBossPackageId = new();
        private static readonly ReaderWriterLockSlim _lock = new();

        public static void Register(ParthananFarmState state)
        {
            _lock.EnterWriteLock();

            try
            {
                ByTrashPackageId[state.Config.TrashPackageId] = state;
                ByBossPackageId[state.Config.BossPackageId] = state;
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        public static ParthananFarmState GetByTrashPackageId(string packageId)
        {
            if (string.IsNullOrEmpty(packageId))
                return null;

            _lock.EnterReadLock();

            try
            {
                return ByTrashPackageId.TryGetValue(packageId, out ParthananFarmState state) ? state : null;
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        public static ParthananFarmState GetByBossPackageId(string packageId)
        {
            if (string.IsNullOrEmpty(packageId))
                return null;

            _lock.EnterReadLock();

            try
            {
                return ByBossPackageId.TryGetValue(packageId, out ParthananFarmState state) ? state : null;
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }
    }

    public class ParthananBrain : TimeDependentSpawnBrain
    {
        private ParthananFarmState FarmState => ParthananFarmRegistry.GetByTrashPackageId(Body.PackageID);

        public ParthananBrain() : base()
        {
            // As before the rewrite.
            AggroLevel = 100;
            AggroRange = 500;
            ThinkInterval = 1500;
        }

        public override void Think()
        {
            if (Body.IsAlive && !Body.IsControlledNPC(Body))
            {
                ParthananFarmState state = FarmState;

                if (state != null && state.ShouldMarchAndDie(Body))
                {
                    GameNPC controller = state.ControllerBody;

                    if (!Body.IsWithinRadius(controller, 50))
                        Body.PathTo(controller, Body.MaxSpeedBase);
                    else
                        Body.Die(controller);
                }
            }

            base.Think();
        }

        protected override bool ShouldBeVisible()
        {
            ParthananFarmState state = FarmState;
            return state == null || state.TrashIsVisible;
        }

        public override void AttackMostWanted()
        {
            ParthananFarmState state = FarmState;

            if (state != null && state.ShouldMarchAndDie(Body))
                return;

            base.AttackMostWanted();
        }
    }

    public class AmalgamateParthananBrain : StandardMobBrain
    {
        public AmalgamateParthananBrain()
        {
            AggroLevel = 100;
            AggroRange = 500;
            ThinkInterval = 1500;
        }
    }

    public class ParthananFarmControllerBrain : APlayerVicinityBrain
    {
        private static readonly Logger log = LoggerManager.Create(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        private ParthananFarmState _farmState;
        private bool _initFailed;

        public ParthananFarmControllerBrain() : base()
        {
            ThinkInterval = 1000;
        }

        public override void Think()
        {
            if (_farmState == null && !_initFailed)
                InitializeFarmState();

            if (_farmState != null && _farmState.Phase is ParthananFarmPhase.Sacrificing)
                _farmState.ScanForMinions();
        }

        public override void KillFSM() { }

        private void InitializeFarmState()
        {
            ParthananFarmConfig config = ParthananFarmDefinitions.GetByFarmId(Body.PackageID);

            if (config == null)
            {
                string knownIds = string.Empty;

                foreach (ParthananFarmConfig known in ParthananFarmDefinitions.Configs)
                    knownIds = $"{knownIds}{(knownIds.Length > 0 ? ", " : string.Empty)}{known.FarmId}";

                if (log.IsErrorEnabled)
                    log.Error($"ParthananFarmController '{Body.Name}' has an unrecognized PackageID ('{Body.PackageID}'). Set its PackageID to one of: {knownIds}. This farm will not function until that's fixed.");

                _initFailed = true;
                return;
            }

            _farmState = new(config, Body);
            ParthananFarmRegistry.Register(_farmState);
        }
    }

    public enum ParthananFarmPhase
    {
        // Trash is being farmed; kills count toward the sacrifice threshold.
        Farming,
        // Threshold reached: minions are gathering at the controller and will self-sacrifice;
        // the boss is spawned but immune while this is in progress.
        Sacrificing,
        // All gathered minions are dead: the boss is vulnerable and fighting, trash is hidden.
        BossActive
    }
}
