using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using DOL.Logging;
using DOL.AI.Brain;
using DOL.Database;
using DOL.Events;
using DOL.GS.Effects;
using DOL.GS.GameEvents;
using DOL.GS.Movement;
using DOL.GS.PacketHandler;
using DOL.GS.PropertyCalc;
using DOL.GS.Realm;
using DOL.GS.ServerRules;
using DOL.GS.RealmAbilities;
using DOL.GS.Styles;
using static DOL.GS.GamePlayer;

namespace DOL.GS
{
    public enum eBotStance : byte
    {
        Auto = 0,
        Melee = 1,
        Ranged = 2,
        Passive = 3
    }

    public class GameBot : GameNPC, IGamePlayer, IGameStaticItemOwner
    {
        private static new readonly Logger log = LoggerManager.Create(typeof(GameBot));

        #region Core Properties

        public GamePlayer Owner { get; private set; }
        public GamePlayer PlayerGroupLeader { get; private set; }

        /// <summary>Set by the brain only around the pet focus shield's own cast (BotBrain.TryStartPetFocusShield).</summary>
        internal bool PetFocusShieldApproved { get; set; }
        public bool IsPlayerLedGroup => PlayerGroupLeader != null;
        public string ClassName { get; private set; }
        public new string InternalID { get; set; }
        public byte ClassId { get; set; }
        public byte RaceId { get; set; }
        public byte GenderId { get; set; }
        public long DatabaseID { get; set; }
        /// <summary>The melee set a hired companion's player readied last in the companion window (BotWeaponSetPolicy,
        /// #569 step 7). Not saved: after a relog the companion's trained set decides again.</summary>
        public eActiveWeaponSlot? PlayerReadiedSlot { get; set; }
        public bool IsAutonomousWorldBot { get; private set; }
        /// <summary>
        /// The NPC Size that draws like a player of that height (1 Short, 2 Average, 3 Tall): 48 / 50 / 52, the new client's
        /// player scale. No bot is drawn bigger than a Tall player (owner 2026-10-31).
        /// </summary>
        public static byte SizeForHeight(int height) => height switch { 1 => 48, 3 => 52, _ => 50 };

        public bool IsTemporaryGroupHelper { get; private set; }
        /// <summary>
        /// A member of a realm's Epic Special Forces (AutonomousEpicSpecialForces): a level-50 gamebot made for one raid
        /// with the /spawn companions' endgame kit, the owner's one exception (2026-10-19) to gamebots earning their
        /// armor. It lives in memory only: nothing about it is ever saved, and it leaves when the raid ends.
        /// </summary>
        public bool IsEpicSpecialForce { get; private set; }
        /// <summary>
        /// A No Lifer's buffbot (NoLiferBuffBots, Living Server 1.2): a level-50 Cleric, Shaman or Druid "second account" that
        /// follows its main or parks at the nearest bind and keeps it buffed. It lives in memory only (never saved, never in the
        /// roster, the population count or any group), logs in and out with its main, and never fights or loots.
        /// </summary>
        public bool IsNoLiferBuffBot => BuffBotMainId > 0;
        /// <summary>The main's roster id for a buffbot (0 for every other bot).</summary>
        public long BuffBotMainId { get; private set; }
        /// <summary>Kept in memory only: never written to the database (the Epic Special Forces and the No Lifers' buffbots).</summary>
        public bool IsInMemoryOnly => IsEpicSpecialForce || IsNoLiferBuffBot;
        public bool IsEndgameCompanion => TemporaryCompanionBalance.IsEndgame(IsTemporaryGroupHelper || IsEpicSpecialForce, Level);
        /// <summary>Equipment is generated from the companion endgame kit (temporary helpers and the Special Forces).</summary>
        public bool UsesCompanionKit => IsTemporaryGroupHelper || IsEpicSpecialForce;

        /// <summary>
        /// A player's own companion (a /spawn helper, or a hired companion; never a world bot with goals): the companion
        /// follow, buff, song, stealth and PvP rules apply to it. Hired companions (HiredCompanions165)
        /// are persistent bots, but their player's (owner 2026-10-31: hired Shamans and Bards buffed instead of
        /// following a walking player).
        /// </summary>
        public bool IsPlayerCompanion => IsTemporaryGroupHelper && !IsAutonomousWorldBot || IsHiredCompanion;

        /// <summary>A player's hired companion (HiredCompanions165, the hired_companions switch): a persistent bot row
        /// owned by one player character. Never a gamebot with goals, never a /spawn helper.</summary>
        public bool IsHiredCompanion => IsAutonomousWorldBot && DatabaseID > 0 && HiredCompanions165.IsHired(this);

        /// <summary>The player a companion works for: a /spawn helper's owner, or a hired companion's player when it is in
        /// that player's group (null otherwise).</summary>
        public GamePlayer CompanionPlayer => IsTemporaryGroupHelper ? Owner :
            IsHiredCompanion ? PlayerGroupLeader ?? HiredCompanions165.OwnerOf(this) : null;
        private bool _endgameCompanionEquipped;
        /// <summary>The level of the kit EquipBot last generated; a level sync never rolls a lower one (#571).</summary>
        private int _generatedGearLevel;
        public bool SuppressLootAndProgress => IsTemporaryGroupHelper;
        internal int EquipmentLevelFloor { get; private set; }
        internal int EquipmentLevelCap { get; private set; }
        public OfflineWorldBotRecord PersistentRecord { get; private set; }
        internal AutonomousGoalAttempt GoalDiagnosticAttempt;
        private byte _lastAutonomousTrainedLevel = 1;
        // Living Server 1.2: this gamebot has had its starter gear (AutonomousStarterGear), saved as "|starter-gear|1".
        private bool _starterGearGiven;
        public bool HasPendingAutonomousTraining => IsAutonomousWorldBot && !IsHiredCompanion && _lastAutonomousTrainedLevel < Level;
        /// <summary>
        /// Spec points to spend, or enough realm ability points to be worth the trip (both at the class
        /// trainer; a few realm points are spent at the next level-up visit instead).
        /// </summary>
        public bool HasSpendableAutonomousTrainingPoints =>
            HasSpendableAutonomousSpecPoints || AutonomousBotRealmAbilities.WorthTrainerTrip(this);
        private bool HasSpendableAutonomousSpecPoints
        {
            get
            {
                if (!HasPendingAutonomousTraining || BotSpec == null || IsTemporaryGroupHelper)
                    return false;
                int points = m_leftOverSpecPoints;
                for (int level = _lastAutonomousTrainedLevel + 1; level <= Level; level++)
                    points += GetSpecPointsForLevel(level) + AutoTrainRefundAtLevel(level);
                return BotSpec.SpecLines.Any(line =>
                    (line.levelRatio > 0 || Level >= 50 || !string.IsNullOrEmpty(BotSpec.EraBuildId)) &&
                    GetSpecializationByName(line.Spec) is Specialization spec && spec.Trainable &&
                    spec.Level < Math.Min(Level, line.SpecCap) &&
                    points >= NextLevelCost(spec.Level, Level, IsAutoTrainLine(spec)));
            }
        }
        public long Experience { get; private set; }
        public long AutonomousRealmPoints { get; private set; }
        public bool AutonomousStateDirty { get; private set; }
        public eAutonomousFidelity AutonomousFidelity { get; set; } = eAutonomousFidelity.Efficient;
        public bool IsOnStableMasterRoute { get; private set; }
        public string StableRouteDestination { get; private set; } = string.Empty;
        private DateTime _stableRouteExpectedArrivalUtc;
        private GameTaxi _stableRouteMount;
        private ECSGameTimer _stableRouteDepartureTimer;
        private bool _stableRouteDeparturePending;
        private PathPoint _stableRouteStart;
        private Point3D _stableRouteEndpoint;
        private int _stableRouteRecoveryCount;
        private readonly ConcurrentDictionary<GamePlayer, StableRideObserver> _stableRideViewers = new();
        private long _nextStableViewerRefreshTick;
        private long _stableRouteLastMovementTick;
        private Point3D _stableRouteLastPosition;
        private ECSGameTimer _deathRecoveryTimer;
        private long _deathTick;
        /// <summary>Autonomous gamebots only: what killed this bot last, and where (AutonomousDeathDiagnostics, #537).</summary>
        public AutonomousDeathBlame.Killer? LastAutonomousKiller { get; set; }
        private ushort _deathRegionId;
        // #521: the last death was an RvR death by the player's rule (GamePlayer.Die: an enemy realm's kill, PvP combat or a
        // keep guard), so a raise gives the RvR resurrection illness (ApplyRvrResurrectionIllness).
        private bool _lastDeathRvr;
        // #521: the effectiveness the RvR resurrection illness takes away while it lasts (ResurrectionIllnessECSGameEffect).
        private double _resurrectionIllnessPenalty;
        internal void AddResurrectionIllnessPenalty(double amount) =>
            _resurrectionIllnessPenalty = Math.Max(0, _resurrectionIllnessPenalty + amount);
        private Point3D _deathLocation;
        private long _nextReturnRoleplayTick;
        private AutonomousStableRoutePlanner.Choice _stableReturnPlan;
        // GameObject.MoveTo removes and re-adds an actor. GameNPC.RemoveFromWorld
        // stops its brain, and brain shutdown can itself request a MoveTo. Keep a
        // depth rather than a boolean so a nested request cannot clear the outer
        // transfer scope and turn a legitimate zone change into a logout/save.
        private int _intentionalWorldMoveDepth;
        internal bool IsIntentionalWorldMove => Volatile.Read(ref _intentionalWorldMoveDepth) > 0;

        private void BeginIntentionalWorldMove() => Interlocked.Increment(ref _intentionalWorldMoveDepth);

        private bool EndIntentionalWorldMove() => Interlocked.Decrement(ref _intentionalWorldMoveDepth) == 0;
        public bool IsReturningAfterRelease { get; private set; }
        public BotSpec BotSpec { get; private set; }
        public eBotStance Stance { get; set; } = eBotStance.Auto;
        private int m_leftOverSpecPoints;
        private long _nextWeaponRepairWarningTick;

        public override IList GetExamineMessages(GamePlayer player)
        {
            if (IsAutonomousWorldBot && player != null && Realm != eRealm.None && player.Realm != eRealm.None &&
                Realm != player.Realm)
            {
                return new ArrayList(1)
                {
                    $"You examine {GetName(0, false)}. {GetPronoun(0, true)} is a member of an enemy faction."
                };
            }
            return base.GetExamineMessages(player);
        }

        #endregion

        #region IGamePlayer — DummyClient / DummyPacketLib

        private readonly BotDummyPacketLib _dummyLib;
        private readonly BotDummyClient _dummyClient;

        public IPacketLib Out => _dummyLib;
        public GameClient Client => _dummyClient;

        #endregion

        /// <summary>A tactic's melee position order (EncounterMeleeStations: Govannon's demon) sets how close the bot follows
        /// the target it fights; otherwise the NPC default.</summary>
        public override int StickMinimumRange =>
            EncounterMeleeStations.MinimumFollowFor(this, TargetObject, GameLoop.GameLoopTime) ?? base.StickMinimumRange;

        #region IGamePlayer — CharacterClass

        protected ICharacterClass m_characterClass;

        public virtual ICharacterClass CharacterClass => m_characterClass;

        public virtual bool SetCharacterClass(int id)
        {
            ICharacterClass cl = ScriptMgr.FindCharacterClass(id);
            if (cl == null)
            {
                if (log.IsErrorEnabled)
                    log.ErrorFormat("No CharacterClass with ID {0} found", id);
                return false;
            }

            m_characterClass = cl;
            // Note: ICharacterClass.Init() expects GamePlayer. We skip it for GameBot
            // since GameBot is not a GamePlayer. The class identity (ID, stats, etc.)
            // is still usable without Init().

            if (Group != null)
                Group.UpdateMember(this, false, true);

            return true;
        }

        #endregion

        #region IGamePlayer — ECS Components (Explicit Interface)

        // The ECS components are public fields on GameLiving (lowercase).
        // IGamePlayer expects PascalCase properties.
        AttackComponent IGamePlayer.AttackComponent => attackComponent;
        RangeAttackComponent IGamePlayer.RangeAttackComponent => rangeAttackComponent;
        StyleComponent IGamePlayer.StyleComponent => styleComponent;
        EffectListComponent IGamePlayer.EffectListComponent => effectListComponent;

        #endregion

        #region IGamePlayer — Property Indexers (Explicit)

        // GameLiving returns PropertyIndexer (concrete) but IGamePlayer expects IPropertyIndexer.
        IPropertyIndexer IGamePlayer.AbilityBonus => AbilityBonus;
        IPropertyIndexer IGamePlayer.ItemBonus => ItemBonus;
        IPropertyIndexer IGamePlayer.BaseBuffBonusCategory => BaseBuffBonusCategory;
        IPropertyIndexer IGamePlayer.SpecBuffBonusCategory => SpecBuffBonusCategory;
        IPropertyIndexer IGamePlayer.DebuffCategory => DebuffCategory;

        public PropertyIndexer BuffBonusCategory4 { get; } = new();

        IPropertyIndexer IGamePlayer.BuffBonusCategory4 => BuffBonusCategory4;
        IPropertyIndexer IGamePlayer.OtherBonus => OtherBonus;
        IPropertyIndexer IGamePlayer.SpecDebuffCategory => SpecDebuffCategory;

        #endregion

        #region IGamePlayer — Stats (Explicit Interface — short vs int mismatch)

        int IGamePlayer.Strength => Strength;
        int IGamePlayer.Dexterity => Dexterity;
        int IGamePlayer.Quickness => Quickness;
        int IGamePlayer.Intelligence => Intelligence;

        #endregion

        #region IGamePlayer — Health/Mana/Endurance Overrides

        public override int Health
        {
            get => base.Health;
            set
            {
                byte oldPercent = HealthPercent;
                bool wasDead = base.Health <= 0;
                base.Health = value;

                // 1.2.1: raised, released or revived after its fall was shown: the original clients get it again.
                if (BotDeathAnimation.EndsAtRaise(DeathFallShown, wasDead, base.Health > 0))
                    BotDeathAnimation.OnRaised(this);

                if (oldPercent != HealthPercent)
                    Group?.UpdateMember(this, false, false);
            }
        }

        /// <summary>1.2.1: the original clients in range were shown this bot's fall at its death and hold it back until it
        /// has health again (BotDeathAnimation).</summary>
        public bool DeathFallShown { get; set; }

        public override int Mana
        {
            get => m_mana;
            set
            {
                byte oldPercent = ManaPercent;
                int maxMana = MaxMana;
                m_mana = Math.Clamp(value, 0, maxMana);

                if (m_mana < maxMana)
                    StartPowerRegeneration();

                if (oldPercent != ManaPercent)
                    Group?.UpdateMember(this, false, false);
            }
        }

        public override int MaxMana => GetModified(eProperty.MaxMana);

        /// <summary>A player drags an item onto the bot: only a beggar takes it (AutonomousBeggars), into its own saved pack.</summary>
        public override bool ReceiveItem(GameLiving source, DbInventoryItem item) =>
            // A hired companion takes what its own player hands it: worn when it can, otherwise into its 10-slot bag.
            HiredCompanions165.TryReceiveItem(this, source as GamePlayer, item) ||
            AutonomousBeggars.TryReceiveItem(this, source as GamePlayer, item) ||
            // A seam (Crafting 2.0 WP0): crafter commerce (WP8) after the beggars; it takes nothing by default.
            CraftingEconomy.BotCommerceSeams.ReceiveItem(this, source as GamePlayer, item) ||
            base.ReceiveItem(source, item);

        /// <summary>A player drags coin onto the bot: only a beggar takes it, into its own saved purse.</summary>
        public override bool ReceiveMoney(GameLiving source, long money) =>
            AutonomousBeggars.TryReceiveMoney(this, source as GamePlayer, money) ||
            CraftingEconomy.BotCommerceSeams.ReceiveMoney(this, source as GamePlayer, money) ||
            base.ReceiveMoney(source, money);

        /// <summary>A bot's /say, recorded in the chat transcript (CHAT_LINE) when a player is near enough to hear it.</summary>
        public override bool Say(string str)
        {
            str = AutonomousRoleplayers.Voice(this, str);   // a roleplayer speaks in thee and thou (never a hired companion)
            str = ChatPromises.BeforeSend(Name, Realm, str);   // chat 1.2 follow-up: no "omw" whose promise ended unsaid
            // Issue #475: out through the say gate (a pass-through: it sends what it is given and records what a player heard).
            bool said = false;
            BotSayGate.Say(new BotSayGate.Utterance("say", Name, Realm, str, who: this), str =>
            {
                said = base.Say(str);
                if (said)
                    ChatPromises.NoteSaid(this, str);   // chat 1.2 follow-up: a promise's line said, heard by a player or not
                return said && AutonomousLivingServer.PlayerHears(this) ? str : null;
            });
            return said;
        }

        /// <summary>A bot's emote, recorded (CHAT_EMOTE) when a player is near enough to see it (chat audit TEXT-051).</summary>
        public override void Emote(eEmote emote) => Emote(emote, null);

        /// <summary>
        /// A bot's emote: the animation, and (Realism 4a, #487, tell 7) the line a player's emote prints, "Brannoc waves at
        /// you." to its target and "Brannoc waves." or "Brannoc waves at Maeve." to players in emote range. An enemy-realm
        /// gamebot is named as each viewer sees it (AutonomousNameMask). Animations with no player emote print nothing.
        /// </summary>
        public void Emote(eEmote emote, GameObject target)
        {
            base.Emote(emote);
            SendEmoteText(emote, target);
            if (AutonomousLivingServer.PlayerHears(this))
                BotChatTranscript.Emote(this, emote);
        }

        private void SendEmoteText(eEmote emote, GameObject target)
        {
            string[] messages = DOL.GS.Commands.EmoteCommandHandler.MessagesFor(emote);
            if (messages == null || ObjectState != eObjectState.Active || CurrentRegion == null)
                return;
            try
            {
                GamePlayer targetPlayer = target as GamePlayer;
                if (targetPlayer != null && !IsWithinRadius(targetPlayer, WorldMgr.VISIBILITY_DISTANCE))
                    targetPlayer = null;
                // One emote, one line through the say gate (#475): each viewer sees it with the names as they know them.
                string generic = BotIdleLife.EmoteLine(messages, Name, target?.Name, false);
                BotSayGate.Say(new BotSayGate.Utterance("emote", Name, Realm, generic ?? string.Empty) { Body = this }, _ =>
                {
                    string shown = null;
                    var viewers = new List<(GamePlayer Viewer, bool IsTarget)>();
                    if (targetPlayer != null)
                        viewers.Add((targetPlayer, true));
                    foreach (GamePlayer player in GetPlayersInRadius(DOL.GS.Commands.EmoteCommandHandler.EMOTE_RANGE_TO_OTHERS))
                        if (player != targetPlayer)
                            viewers.Add((player, false));
                    foreach ((GamePlayer viewer, bool isTarget) in viewers)
                    {
                        string line = BotIdleLife.EmoteLine(messages, AutonomousNameMask.NameFor(viewer, this, 0, true),
                            target == null ? null : AutonomousNameMask.NameFor(viewer, target, 0, false), isTarget);
                        if (line == null)
                            continue;
                        viewer.Out.SendMessage(line, eChatType.CT_Emote, eChatLoc.CL_SystemWindow);
                        shown ??= line;
                    }
                    return shown;
                });
            }
            catch (Exception)
            {
                // an emote's text is cosmetic: never let it break the bot's turn
            }
        }

        public override int MaxConcentration => GetModified(eProperty.MaxConcentration);
        public override int Concentration => MaxConcentration - (effectListComponent?.UsedConcentration ?? 0);

        // Bot buffs retain their real point budget, but not the human 20-effect cap.
        // Check before queuing a cast so exhausted buffers can move/rest instead of retrying.
        public bool CanAffordConcentration(Spell spell) => spell != null &&
            (spell.Concentration == 0 || spell.Concentration <= Concentration);

        public override int Endurance
        {
            get => m_endurance;
            set
            {
                byte oldPercent = EndurancePercent;
                int maxEndurance = MaxEndurance;
                m_endurance = Math.Clamp(value, 0, maxEndurance);

                if (m_endurance < maxEndurance)
                    StartEnduranceRegeneration();

                if (oldPercent != EndurancePercent)
                    Group?.UpdateMember(this, false, false);
            }
        }

        #endregion

        #region Resting seated (visual)

        // Bots sit down to rest, visibly (owner 2026-10-31). The NPC packets carry no sit state, so a seated bot is shown by
        // emote 0xF0 (sit down, then sit-idle) and stood up by 0xF1, sent on each change to the players who see it and
        // again by SendNPCCreate to a player who first sees it seated. Visual only: recovery and its timing are unchanged.
        // Realism 4a (#487): the animation ids and the switch that drops them live in BotIdleLife (open PC check).
        internal static eEmote SitEmote => BotIdleLife.SitAnimation;
        internal static eEmote StandEmote => BotIdleLife.StandAnimation;
        private bool _seatedForClients;

        public bool IsSeatedForClients => _seatedForClients;

        /// <summary>Seated and shown so: what a player who first sees the bot is sent (PacketLib1124.SendNPCCreate).</summary>
        public bool IsShownSeated => _seatedForClients && BotIdleLife.ShowSeat;

        private void SetSeatedForClients(bool seated)
        {
            if (!seated)
            {
                _seatedByHabit = false;
                _standSpreadAtTick = 0;
            }
            if (_seatedForClients == seated)
                return;
            _seatedForClients = seated;
            if (ObjectState != eObjectState.Active || !BotIdleLife.ShowSeat)
                return;
            foreach (GamePlayer player in GetPlayersInRadius(WorldMgr.VISIBILITY_DISTANCE))
                player.Out.SendEmoteAnimation(this, seated ? SitEmote : StandEmote);
        }

        // Realism 4a (#487): a watched seated bot told to go stands 0.4-1.5 s later (per bot), not all on one tick.
        private long _standSpreadAtTick;

        /// <summary>
        /// For a bot seated by habit (a wait, a wall or a kept rest seat) that is about to walk off: the milliseconds it
        /// still sits before standing, or 0 to stand now. The spread is drawn once per seat and only while a player can see
        /// the bot; anything to fight stands it at once.
        /// </summary>
        public int HoldSeatBeforeStanding(long now, bool watched)
        {
            if (!_seatedByHabit || !_seatedForClients)
                return 0;
            if (!BotIdleLife.SpreadsStanding(watched, InCombat || BotRestRecovery.BlocksRest(this)))
            {
                _standSpreadAtTick = 0;
                return 0;
            }
            if (_standSpreadAtTick == 0)
                _standSpreadAtTick = now + BotIdleLife.StandSpreadMs(Random.Shared.NextDouble());
            return _standSpreadAtTick > now ? (int)(_standSpreadAtTick - now) : 0;
        }

        // Bots as people for 1.2, work package 5 (BotMovementHabits): an autonomous gamebot also sits while it waits (for
        // its group, at a rendezvous, at the bank, on a held keep's wall). The same emote, visual only, and outside rest:
        // it stands at once when it moves (NpcMovementComponent.WalkToInternal), casts (CastSpell), attacks (StartAttack),
        // dies or is moved; a rest that begins takes the seat over. Only BotMovementHabitsRuntime sits a bot this way.
        private bool _seatedByHabit;

        /// <summary>Seated by a movement habit (not by resting).</summary>
        public bool IsSeatedByHabit => _seatedByHabit;

        /// <summary>Sits a standing, idle autonomous gamebot down by habit. False (nothing sent) when it may not sit now.</summary>
        internal bool SitByHabit()
        {
            if (_seatedForClients || !IsAlive || !IsAutonomousWorldBot || IsTemporaryGroupHelper || IsMoving || IsCasting ||
                IsAttacking || InCombat || IsStealthed || IsRecoveryResting || IsOnStableMasterRoute ||
                ObjectState != eObjectState.Active)
                return false;
            SetSeatedForClients(true);
            _seatedByHabit = true;
            return true;
        }

        /// <summary>Stands a bot seated by habit (0xF1); a resting bot keeps its seat.</summary>
        internal void StandFromHabit()
        {
            if (_seatedByHabit && !_recoveryRestLocked)
                SetSeatedForClients(false);
            _seatedByHabit = false;
        }

        #endregion

        #region Player-accurate regeneration

        private int GetHealthAndPowerRegenerationInterval()
        {
            if (IsEnhancedResting) return BotRestRecovery.TickMilliseconds;
            // GameBots recover through the internal recovery lock; never expose
            // GameLiving.IsSitting to clients. Outside enhanced recovery they
            // use ordinary standing/combat timing.
            // #521: in RvR an autonomous gamebot regenerates as the player does, and its rest seat counts as sitting (3 s).
            if (UsesPlayerRegeneration)
                return DOL.GS.ServerRules.PvpRules.PlayerRegenerationIntervalMs(IsRecoveryResting && !IsMoving, InCombat);
            return ClassicRestRegeneration.HealthAndPowerInterval(false, InCombat);
        }

        protected override int GetHealthRegenerationInterval() => GetHealthAndPowerRegenerationInterval();

        protected override int GetPowerRegenerationInterval() => GetHealthAndPowerRegenerationInterval();

        protected override int GetEnduranceRegenerationInterval() => 1000;

        public bool IsRecoveryResting => _recoveryRestLocked;

        /// <summary>#521 (owner 2026-11-24): an autonomous gamebot in RvR (PvpRules.InRvr) regenerates as the player does; the
        /// fast rest floor (BotRestRecovery) stays for PvE and for companions.</summary>
        public bool UsesPlayerRegeneration => !DOL.GS.ServerRules.PvpRules.UsesFastRest(DOL.GS.ServerRules.PvpRules.TakesPlayerRules(this));

        public bool IsEnhancedResting => !UsesPlayerRegeneration &&
            ((IsRecoveryResting && !IsMoving && !BotRestRecovery.BlocksRest(this)) ||
            BotRestRecovery.ShouldAutonomousPlayerBotRecover(this, GameLoop.GameLoopTime));

        /// <summary>
        /// Moves an already-running ordinary regeneration timer onto the
        /// bot-only fast cadence when the two-second quiet window opens. The
        /// callback still rechecks combat, aggro, and casting, so this never
        /// grants recovery during an active encounter or changes normal NPC or
        /// real-player timers.
        /// </summary>
        internal void EnsureAutonomousRecoveryTimers()
        {
            if (!BotRestRecovery.ShouldAutonomousPlayerBotRecover(this, GameLoop.GameLoopTime) ||
                ObjectState is not eObjectState.Active)
                return;

            static void ArmFastTimer(ECSGameTimer timer)
            {
                if (timer == null || !timer.IsAlive || timer.Interval != BotRestRecovery.TickMilliseconds ||
                    timer.TimeUntilElapsed > BotRestRecovery.TickMilliseconds)
                    timer?.Start(BotRestRecovery.TickMilliseconds);
            }

            if (Health < MaxHealth)
                ArmFastTimer(m_healthRegenerationTimer);
            if (Mana < MaxMana)
                ArmFastTimer(m_powerRegenerationTimer);
            if (Endurance < MaxEndurance)
                ArmFastTimer(m_enduRegenerationTimer);
        }

        // GameNPC forwards controlled-pet combat timestamps to its owner. Wake
        // the bot on that same event instead of waiting for its next idle turn.
        // Do not change human combat flags, clear aggro or interrupt any casts.
        private void WakeFromRestOnCombat(long tick)
        {
            if (!IsRecoveryResting || !BotRestRecovery.RecentlyFought(GameLoop.GameLoopTime, tick)) return;
            WakeRecoveryRest();
            if (Brain is BotBrain brain) brain.NextThinkTick = GameLoop.GameLoopTime;
        }

        public override long LastAttackTickPvE
        {
            set { base.LastAttackTickPvE = value; WakeFromRestOnCombat(value); }
        }

        public override long LastAttackTickPvP
        {
            set { base.LastAttackTickPvP = value; WakeFromRestOnCombat(value); }
        }

        public override long LastAttackedByEnemyTickPvE
        {
            set { base.LastAttackedByEnemyTickPvE = value; WakeFromRestOnCombat(value); }
        }

        public override long LastAttackedByEnemyTickPvP
        {
            set { base.LastAttackedByEnemyTickPvP = value; WakeFromRestOnCombat(value); }
        }

        private void StartEnhancedRecoveryTimers()
        {
            if (!IsEnhancedResting || ObjectState is not eObjectState.Active) return;
            // Entering rest is a one-time transition. Replace a pending 14s
            // combat tick, but never restart timers on each ordinary AI turn.
            if (Health < MaxHealth) m_healthRegenerationTimer?.Start(1);
            if (Mana < MaxMana) m_powerRegenerationTimer?.Start(1);
            if (Endurance < MaxEndurance) m_enduRegenerationTimer?.Start(1);
        }

        private void WakeAfterRecovery()
        {
            if (IsRecoveryResting && AutonomousRestPolicy.IsFullyRecovered(HealthPercent, ManaPercent, EndurancePercent, MaxMana > 0) &&
                Brain is BotBrain brain && brain.NextThinkTick > GameLoop.GameLoopTime)
                brain.NextThinkTick = GameLoop.GameLoopTime;
        }

        protected override int HealthRegenerationTimerCallback(ECSGameTimer timer)
        {
            if (!IsEnhancedResting || Health >= MaxHealth) return base.HealthRegenerationTimerCallback(timer);
            int native = GetModified(eProperty.HealthRegenerationAmount);
            // Disease/bleed regen suppression remains authoritative.
            if (native > 0) ChangeHealth(this, eHealthChangeType.Regenerate, BotRestRecovery.RecoveryAmount(native, MaxHealth));
            WakeAfterRecovery();
            return BotRestRecovery.TickMilliseconds;
        }

        protected override int PowerRegenerationTimerCallback(ECSGameTimer timer)
        {
            if (!IsEnhancedResting || Mana >= MaxMana) return base.PowerRegenerationTimerCallback(timer);
            ChangeMana(this, eManaChangeType.Regenerate,
                BotRestRecovery.RecoveryAmount(GetModified(eProperty.PowerRegenerationAmount), MaxMana));
            WakeAfterRecovery();
            return BotRestRecovery.TickMilliseconds;
        }

        public override void StartPowerRegeneration()
        {
            if (m_health == 0 || ObjectState is not eObjectState.Active || m_powerRegenerationTimer.IsAlive || MaxMana <= 0)
                return;

            m_powerRegenerationTimer.Start(GetPowerRegenerationInterval());
        }

        public override void StartEnduranceRegeneration()
        {
            if (m_health == 0 || ObjectState is not eObjectState.Active || m_enduRegenerationTimer.IsAlive)
                return;

            m_enduRegenerationTimer.Start(GetEnduranceRegenerationInterval());
        }

        protected override int EnduranceRegenerationTimerCallback(ECSGameTimer selfRegenerationTimer)
        {
            int maxEndurance = MaxEndurance;
            // GameBots have lightweight free-running travel. Endurance is a
            // combat resource for styles/abilities and is never consumed merely
            // because a persistent bot or /spawn companion is moving quickly.
            // #521: except sprint in RvR, which is real for an autonomous gamebot (+30%, MaxSpeedCalculator) and drains as
            // the player's does (GamePlayer.EnduranceRegenerationTimerCallback).
            bool sprintDrains = IsSprinting && DOL.GS.ServerRules.PvpRules.TakesPlayerRules(this);
            if (Endurance >= maxEndurance)
            {
                Endurance = maxEndurance;
                if (!sprintDrains)
                    return 0;
            }

            int regen = GetModified(eProperty.EnduranceRegenerationAmount);
            if (IsEnhancedResting) regen = BotRestRecovery.RecoveryAmount(regen, maxEndurance);
            if (sprintDrains && IsMoving && EffectListService.GetEffectOnTarget(this, eEffect.Charge) == null)
            {
                RealmAbilities.AtlasOF_LongWindAbility longWind = GetAbility<RealmAbilities.AtlasOF_LongWindAbility>();
                int drain = DOL.GS.ServerRules.PvpRules.SprintDrainPerTick(longWind != null ? longWind.GetAmountForLevel(longWind.Level) : 0);
                regen -= drain;
                int endChant = GetModified(eProperty.FatigueConsumption);
                if (endChant > 1)
                    regen = (int)Math.Ceiling(regen * endChant * 0.01);
                if (Endurance + regen > maxEndurance - drain)
                    regen -= Endurance + regen - (maxEndurance - drain);
            }
            if (regen != 0)
                ChangeEndurance(this, eEnduranceChangeType.Regenerate, regen);
            if (sprintDrains && Endurance - 5 <= 0)
                Sprint(false);

            WakeAfterRecovery();

            return GetEnduranceRegenerationInterval();
        }

        #endregion

        #region IGamePlayer — Health/Mana Calculations

        public virtual int CalculateMaxHealth(int level, int constitution)
        {
            if (CharacterClass == null)
                return 1;

            // Keep this identical to GamePlayer.CalculateMaxHealth. Playerbots
            // must not receive the old NPC-oriented approximation, especially
            // at the first few levels where it granted a large flat HP surplus.
            constitution -= 50;
            if (constitution < 0)
                constitution *= 2;

            int hpFromLevel = CharacterClass.BaseHP * level;
            int hpFromConstitution = hpFromLevel * constitution / 10000;
            int hpFromChampionLevels = ChampionLevel >= 1
                ? ServerProperties.Properties.HPS_PER_CHAMPIONLEVEL * ChampionLevel
                : 0;
            double health = 20 + hpFromLevel / 50 + hpFromConstitution + hpFromChampionLevels;
            if (GetModified(eProperty.ExtraHP) > 0)
                health += Math.Round(health * GetModified(eProperty.ExtraHP) / 100d);

            return Math.Max(1, (int)health);
        }

        public virtual int CalculateMaxMana(int level, int manaStat)
        {
            if (CharacterClass == null || CharacterClass.ManaStat == eStat.UNDEFINED)
                return 0;

            return Math.Max(5, level * 5 + (manaStat - 50));
        }

        #endregion

        #region IGamePlayer — PlayerDeck / Misc

        private PlayerDeck _randomNumberDeck;

        public PlayerDeck RandomNumberDeck
        {
            get
            {
                if (_randomNumberDeck == null)
                    _randomNumberDeck = new PlayerDeck();
                return _randomNumberDeck;
            }
            set { _randomNumberDeck = value; }
        }

        public List<int> SelfBuffChargeIDs { get; } = new List<int>();
        public int TotalConstitutionLostAtDeath { get; set; }
        public double SpecLock { get; set; }

        #endregion

        #region IGamePlayer — Level / Class Properties

        public byte MaxLevel => 50;
        public int RealmLevel { get; set; }
        public int MLLevel { get; set; }
        public bool Champion { get; set; }
        public int ChampionLevel { get; set; }

        #endregion

        #region IGamePlayer — Guild

        private Guild _guild;

        public Guild Guild
        {
            get => _guild;
            set => _guild = value;
        }

        /// <summary>The bot's rank in its guild (set by <see cref="Guild.AddBotMember"/>), or null.</summary>
        public DbGuildRank GuildRank { get; set; }

        #endregion

        #region IGamePlayer — Duel System

        private GameDuel Duel { get; set; }

        public GameLiving DuelPartner => Duel?.GetPartnerOf(this);

        public void OnDuelStart(GameDuel duel)
        {
            Duel = duel;
        }

        public void OnDuelStop()
        {
            Duel = null;
        }

        public bool IsDuelPartner(GameLiving living)
        {
            return DuelPartner == living;
        }

        public bool IsDuelReady { get; set; }

        #endregion

        #region IGamePlayer — Shade System

        protected ShadeECSGameEffect m_ShadeEffect;
        private ushort _creationModel;

        public ShadeECSGameEffect ShadeEffect
        {
            get => m_ShadeEffect;
            set => m_ShadeEffect = value;
        }

        public bool IsShade => m_ShadeEffect != null || Model == ShadeModel;

        public ushort CreationModel => _creationModel == 0 ? Model : _creationModel;

        /// <summary>A hired companion wears the classic look its player chose in the hire window.</summary>
        public void SetLookModel(ushort model)
        {
            if (model == 0)
                return;
            _creationModel = model;
            Model = model;
        }

        public ushort ShadeModel
        {
            get
            {
                if (CharacterClass != null && CharacterClass.ID == (int)eCharacterClass.Necromancer)
                    return 822;
                return Model;
            }
        }

        public virtual void Shade(bool state)
        {
            if (state)
            {
                if (m_ShadeEffect != null)
                    return;
                m_ShadeEffect = ECSGameEffectFactory.Create(
                    new(this, 0, 1),
                    static (in ECSGameEffectInitParams init) => new NecromancerShadeECSGameEffect(init)) as ShadeECSGameEffect;
                Model = ShadeModel;
            }
            else
            {
                ShadeECSGameEffect oldEffect = m_ShadeEffect;
                m_ShadeEffect = null;
                if (oldEffect?.IsActive == true)
                    _ = oldEffect.End();
                Model = CreationModel;
            }
        }

        #endregion

        #region IGamePlayer — Horse System

        public ControlledHorse ActiveHorse => null;

        private bool _isOnHorse;
        public virtual bool IsOnHorse
        {
            get => _isOnHorse;
            set => _isOnHorse = value;
        }

        /// <summary>
        /// Boards this bot on an existing, authoritative stable path.  A small
        /// optional delay lets player-led temporary companions visibly board in
        /// sequence after their real player while still using the same path and
        /// normal taxi movement.
        /// </summary>
        public bool BeginStableMasterRoute(
            string destination,
            TimeSpan expectedDuration,
            PathPoint route,
            DbItemTemplate ticket,
            int departureDelayMilliseconds = 0)
        {
            if (route == null || route.Next == null || route.Type is not EPathType.Once || CurrentRegion == null)
                return false;

            WakeRecoveryRest();
            CleanupStableRouteMount();
            StopAttack();
            StopFollowing();
            StopMovingOnPath();
            StopMoving();
            if (Brain is BotBrain brain)
                brain.ClearAggroList();
            // A player keeps their pet when boarding a horse (MountSteed releases nothing); so do the player's
            // companions, whose pet is placed beside them on arrival. Gamebots still release it.
            if (ControlledBrain != null && !IsTemporaryGroupHelper)
                ReleaseControlledPet(PetReleaseReason.StableTravel);
            AutonomousPetSupport.ReleaseFieldTurrets(this);

            // Unlike GamePlayer, GameBot is NPC-backed and cannot occupy the
            // native GameTaxi rider array. The taxi therefore owns the one and
            // only moving path while it synchronizes this bot's authoritative
            // server position. Running a second copy of the path on the bot was
            // the source of detached/riderless horses and arrival drift.
            var mount = ticket?.Color > 0
                ? new GameBotTaxi(this, NpcTemplateMgr.GetTemplate(ticket.Color))
                : new GameBotTaxi(this);
            mount.Realm = Realm;
            mount.X = route.X;
            mount.Y = route.Y;
            mount.Z = route.Z;
            mount.CurrentRegion = CurrentRegion;
            mount.Heading = route.GetHeading(route.Next);
            mount.FixedSpeed = true;
            mount.MaxSpeedBase = AutonomousStableRoutePlanner.StableSpeed;
            if (!mount.AddToWorld())
            {
                mount.Delete();
                return false;
            }

            _stableRouteMount = mount;
            _stableRouteMount.CurrentPathPoint = route;
            IsOnStableMasterRoute = true;
            IsOnHorse = true;
            StableRouteDestination = destination ?? string.Empty;
            _stableRouteExpectedArrivalUtc = DateTime.UtcNow + expectedDuration +
                                              TimeSpan.FromMilliseconds(Math.Max(0, departureDelayMilliseconds)) +
                                              TimeSpan.FromMinutes(2);
            _stableRouteLastMovementTick = GameLoop.GameLoopTime;
            _stableRouteLastPosition = new Point3D(route.X, route.Y, route.Z);
            _stableRouteStart = route;
            PathPoint endpoint = route;
            while (endpoint.Next != null)
                endpoint = endpoint.Next;
            _stableRouteEndpoint = new Point3D(endpoint.X, endpoint.Y, endpoint.Z);
            _stableRouteRecoveryCount = 0;
            CurrentPathPoint = null;
            mount.SynchronizeRider();
            RefreshStableRideViewers();
            _nextStableViewerRefreshTick = GameLoop.GameLoopTime + 2_000 + ObjectID % 750;
            if (departureDelayMilliseconds > 0)
            {
                _stableRouteDeparturePending = true;
                _stableRouteDepartureTimer = new ECSGameTimer(this, StartStableMasterRouteMotion);
                _stableRouteDepartureTimer.Start(departureDelayMilliseconds);
            }
            else
            {
                StartStableMasterRouteMotion(null);
            }
            if (PersistentRecord != null)
            {
                PersistentRecord.Activity = "Riding stablemaster horse route";
                PersistentRecord.CurrentGoal = $"Travel to {StableRouteDestination}";
                PersistentRecord.TravelDestination = StableRouteDestination;
            }
            AutonomousStuckWatchdog.MarkProgress(this, eAutonomousProgressKind.StableTravel);
            log.Info($"AUTONOMOUS_STABLE_ROUTE_BOARD bot=\"{Name}\" id={DatabaseID} level={Level} " +
                     $"realm={GlobalConstants.RealmToName(Realm)} destination=\"{StableRouteDestination}\" ticket=\"{ticket?.Name}\" " +
                     $"endpoint={_stableRouteEndpoint.X},{_stableRouteEndpoint.Y},{_stableRouteEndpoint.Z}");
            return true;
        }

        private int StartStableMasterRouteMotion(ECSGameTimer timer)
        {
            _stableRouteDepartureTimer?.Stop();
            _stableRouteDepartureTimer = null;
            _stableRouteDeparturePending = false;
            if (!IsOnStableMasterRoute)
                return 0;

            if (_stableRouteMount?.ObjectState is eObjectState.Active && _stableRouteMount.CurrentPathPoint != null)
                _stableRouteMount.MoveOnPath(AutonomousStableRoutePlanner.StableSpeed);
            _stableRouteLastMovementTick = GameLoop.GameLoopTime;
            return 0;
        }

        public bool IsProtectedStableMasterTravel(DateTime nowUtc) =>
            IsOnStableMasterRoute && IsOnHorse && nowUtc <= _stableRouteExpectedArrivalUtc;

        public void CompleteStableMasterRoute() => CompleteStableMasterRoute(false);

        private void CompleteStableMasterRoute(bool confirmedArrival)
        {
            bool wasStableTravel = IsOnStableMasterRoute;
            try { CompleteStableMasterRouteCore(confirmedArrival, wasStableTravel); }
            finally
            {
                // A companion's pet rejoins it at the end of the ride (it could not keep up with the horse).
                if (wasStableTravel && (IsTemporaryGroupHelper || IsHiredCompanion) && ControlledBrain?.Body is { IsAlive: true } pet &&
                    pet.ObjectState == eObjectState.Active && pet.CurrentRegionID == CurrentRegionID && pet.MaxSpeedBase > 0 &&
                    !IsWithinRadius(pet, 400))
                {
                    Point2D beside = GetPointFromHeading(Heading, 64);
                    pet.MoveInRegion(CurrentRegionID, beside.X, beside.Y, Z + 10, (ushort)((Heading + 2048) % 4096), false);
                }
            }
        }

        private void CompleteStableMasterRouteCore(bool confirmedArrival, bool wasStableTravel)
        {
            string completedDestination = StableRouteDestination;
            _stableRouteDepartureTimer?.Stop();
            _stableRouteDepartureTimer = null;
            _stableRouteDeparturePending = false;
            CleanupStableRouteMount();
            IsOnStableMasterRoute = false;
            IsOnHorse = false;
            StableRouteDestination = string.Empty;
            _stableRouteExpectedArrivalUtc = default;
            _stableRouteStart = null;
            _stableRouteEndpoint = null;
            _stableRouteRecoveryCount = 0;
            if (wasStableTravel && IsAutonomousWorldBot)
            {
                Vector3 arrival = new(X, Y, Z);
                // A horse lands on the ground. Route end points were recorded on another client's
                // terrain at some border keeps (Castle Sauvage ~150 units low, Svasud Faste ~450 units
                // high on the classic meshes), leaving the rider off the walkable floor; every route
                // then failed and the pocket escape sent it to its capital (164 Albion bots in 30
                // minutes, 2026-10-06). Same spot, real floor height.
                if ((AutonomousStableRoutePlanner.ShouldCorrectAuditedMularnLanding(
                         confirmedArrival, IsAlive, CurrentRegionID, arrival) &&
                     AutonomousRouteHotspotRepair.TryResolveFloor(PathfindingProvider.Instance,
                         CurrentZone, CurrentRegionID, arrival, out Vector3 floor) ||
                     IsAlive && AutonomousStableRoutePlanner.TryGroundLanding(PathfindingProvider.Instance, CurrentZone, arrival, out floor)) &&
                    MoveInRegion(CurrentRegionID, (int)Math.Round(floor.X), (int)Math.Round(floor.Y),
                        (int)Math.Round(floor.Z), Heading, true))
                    log.Info($"AUTONOMOUS_STABLE_LANDING_CORRECTION bot=\"{Name}\" id={DatabaseID} " +
                             $"region={CurrentRegionID} from={arrival} to={floor}");
                // #569 step 4: every rider of a route landed on its last point, next to a busy
                // stable master. A gamebot steps once to its own spot 60-140 units off, away from the master (one
                // radius query, one floor projection, one corridor check per arrival). Hired companions stay put.
                if (IsAlive && !IsHiredCompanion && TryStepOffStableLanding(out Vector3 stepFrom, out Vector3 stepTo) &&
                    log.IsDebugEnabled)
                    log.Debug($"AUTONOMOUS_STABLE_LANDING_STEP bot=\"{Name}\" id={DatabaseID} " +
                              $"region={CurrentRegionID} from={stepFrom} to={stepTo}");
                AutonomousStuckWatchdog.MarkProgress(this, eAutonomousProgressKind.StableTravel);
                log.Info($"AUTONOMOUS_STABLE_ROUTE_ARRIVE bot=\"{Name}\" id={DatabaseID} level={Level} " +
                         $"realm={GlobalConstants.RealmToName(Realm)} destination=\"{completedDestination}\" position={X},{Y},{Z}");
            }
        }

        private bool TryStepOffStableLanding(out Vector3 from, out Vector3 to)
        {
            from = new(X, Y, Z);
            to = from;
            GameStableMaster master = null;
            long best = long.MaxValue;
            foreach (GameNPC npc in GetNPCsInRadius(600))
            {
                if (npc is not GameStableMaster stable || npc.ObjectState != eObjectState.Active) continue;
                long distance = (long)(npc.X - X) * (npc.X - X) + (long)(npc.Y - Y) * (npc.Y - Y);
                if (distance >= best) continue;
                best = distance;
                master = stable;
            }
            Vector3? masterAt = master == null ? null : new Vector3(master.X, master.Y, master.Z);
            if (!AutonomousServiceNpcClearance.TryResolveLandingStepOff(PathfindingProvider.Instance, CurrentZone, from,
                    masterAt, DatabaseID, out to))
                return false;
            return MoveInRegion(CurrentRegionID, (int)Math.Round(to.X), (int)Math.Round(to.Y), (int)Math.Round(to.Z),
                Heading, true);
        }

        /// <summary>
        /// A transient loss of movement flags is not an arrival. The rider stays
        /// mounted until the real final ticket waypoint has actually been
        /// reached and the Once path has released its last point.
        /// </summary>
        public bool TryCompleteStableMasterRouteAfterArrival()
        {
            if (!IsOnStableMasterRoute)
                return true;
            bool atEndpoint = _stableRouteEndpoint != null &&
                              DistanceSquared(X, Y, _stableRouteEndpoint.X, _stableRouteEndpoint.Y) <= 90L * 90L;
            if (!AutonomousStableRouteLifecycle.ShouldComplete(
                    _stableRouteDeparturePending,
                    _stableRouteMount?.IsMovingOnPath == true,
                    _stableRouteMount?.CurrentPathPoint != null,
                    atEndpoint))
                return false;
            CompleteStableMasterRoute(true);
            return true;
        }

        public void MaintainStableMasterRoute()
        {
            if (!IsOnStableMasterRoute)
                return;

            if (_stableRouteMount?.ObjectState is not eObjectState.Active)
            {
                log.Warn($"AUTONOMOUS_STABLE_ROUTE_FAIL bot=\"{Name}\" id={DatabaseID} level={Level} " +
                         $"realm={GlobalConstants.RealmToName(Realm)} destination=\"{StableRouteDestination}\" reason=\"authoritative taxi left world before arrival\"");
                CompleteStableMasterRoute();
                return;
            }

            if (_stableRouteMount is GameBotTaxi botTaxi)
                botTaxi.SynchronizeRider();
            if (GameServiceUtils.ShouldTick(_nextStableViewerRefreshTick))
            {
                _nextStableViewerRefreshTick = GameLoop.GameLoopTime + 2_000 + ObjectID % 750;
                RefreshStableRideViewers();
            }
            if (_stableRouteDeparturePending)
                return;
            if (DistanceSquared(X, Y, _stableRouteLastPosition.X, _stableRouteLastPosition.Y) >= 24L * 24L)
            {
                _stableRouteLastPosition = new Point3D(X, Y, Z);
                _stableRouteLastMovementTick = GameLoop.GameLoopTime;
                AutonomousStuckWatchdog.MarkProgress(this, eAutonomousProgressKind.StableTravel);
            }

            if (_stableRouteMount.CurrentPathPoint != null && !_stableRouteMount.IsMovingOnPath &&
                GameLoop.GameLoopTime - _stableRouteLastMovementTick >= 5_000)
            {
                _stableRouteMount.MoveOnPath(AutonomousStableRoutePlanner.StableSpeed);
                _stableRouteLastMovementTick = GameLoop.GameLoopTime;
            }

            if (_stableRouteMount.CurrentPathPoint == null && !_stableRouteMount.IsMovingOnPath && _stableRouteEndpoint != null &&
                DistanceSquared(X, Y, _stableRouteEndpoint.X, _stableRouteEndpoint.Y) > 90L * 90L &&
                GameLoop.GameLoopTime - _stableRouteLastMovementTick >= 1_000)
            {
                _stableRouteMount.CurrentPathPoint = FindStableRouteRecoveryPoint();
                if (_stableRouteMount.CurrentPathPoint != null)
                {
                    _stableRouteRecoveryCount++;
                    _stableRouteMount.MoveOnPath(AutonomousStableRoutePlanner.StableSpeed);
                    _stableRouteLastMovementTick = GameLoop.GameLoopTime;
                    log.Warn($"AUTONOMOUS_STABLE_ROUTE_RESUME bot={Name} id={DatabaseID} " +
                             $"destination=\"{StableRouteDestination}\" recovery={_stableRouteRecoveryCount} " +
                             $"position={X},{Y},{Z}");
                }
            }

            if (_stableRouteMount?.ObjectState is eObjectState.Active &&
                _stableRouteMount.CurrentPathPoint != null && !_stableRouteMount.IsMovingOnPath)
            {
                _stableRouteMount.MoveOnPath(AutonomousStableRoutePlanner.StableSpeed);
            }
        }

        private PathPoint FindStableRouteRecoveryPoint()
        {
            PathPoint nearest = null;
            long bestDistance = long.MaxValue;
            for (PathPoint point = _stableRouteStart; point != null; point = point.Next)
            {
                long distance = DistanceSquared(X, Y, point.X, point.Y);
                if (distance >= bestDistance)
                    continue;
                bestDistance = distance;
                nearest = point;
            }
            return bestDistance <= 120L * 120L && nearest?.Next != null ? nearest.Next : nearest;
        }

        internal void SynchronizeStableRoutePosition(GameBotTaxi mount)
        {
            if (mount == null || !IsOnStableMasterRoute || mount != _stableRouteMount ||
                mount.CurrentRegion != CurrentRegion)
                return;

            m_x = mount.X;
            m_y = mount.Y;
            m_z = mount.Z;
            Heading = mount.Heading;
            movementComponent.ForceUpdatePosition();
            SubZoneObject.CheckForRelocation();
        }

        internal GameTaxi StableRouteMountForClient =>
            IsOnStableMasterRoute && ObjectState is eObjectState.Active &&
            _stableRouteMount?.ObjectState is eObjectState.Active &&
            _stableRouteMount.CurrentRegion == CurrentRegion ? _stableRouteMount : null;

        private void RefreshStableRideViewers()
        {
            if (_stableRouteMount?.ObjectState is not eObjectState.Active)
                return;

            GamePlayer[] viewers = GetPlayersInRadius(WorldMgr.VISIBILITY_DISTANCE)
                .Where(player => player?.Client?.ClientState is GameClient.eClientState.Playing &&
                                 player.IsWithinRadius(_stableRouteMount, WorldMgr.VISIBILITY_DISTANCE))
                .ToArray();
            HashSet<GamePlayer> present = viewers.ToHashSet();
            foreach (GamePlayer departed in _stableRideViewers.Keys.Where(player => !present.Contains(player)))
                _stableRideViewers.TryRemove(departed, out _);

            foreach (GamePlayer player in viewers)
                SendStableRideAssociation(player);
        }

        /// <summary>
        /// The native player path creates the horse before its rider. Do the same
        /// for NPC-backed bots without changing their server type or taxi movement.
        /// Reattach after recreation/position packets and retry at a bounded rate.
        /// </summary>
        internal void SendStableRideAssociation(GamePlayer player, bool force = false)
        {
            GameTaxi mount = StableRouteMountForClient;
            if (player?.Client?.ClientState is not GameClient.eClientState.Playing ||
                mount == null || !IsWithinRadius(player, WorldMgr.VISIBILITY_DISTANCE) ||
                !player.IsWithinRadius(mount, WorldMgr.VISIBILITY_DISTANCE) ||
                !IsVisibleTo(player) || !mount.IsVisibleTo(player) ||
                !player.CanDetect(this) || !player.CanDetect(mount))
                return;

            StableRideObserver observer = _stableRideViewers.GetOrAdd(player, static _ => new());
            observer.Refresh(GameLoop.GameLoopTime, force,
                () =>
                {
                    // Also refresh an already visible on-foot bot when it first boards.
                    // ClientService preserves equipment and its normal object caches.
                    ClientService.CreateObjectForPlayer(player, mount);
                    ClientService.CreateObjectForPlayer(player, this);
                },
                () =>
                {
                    if (StableRouteMountForClient == mount)
                        player.Out.SendRiding(this, mount, false);
                });
        }

        private void CleanupStableRouteMount()
        {
            if (_stableRouteMount != null)
            {
                foreach (GamePlayer player in GetPlayersInRadius(WorldMgr.VISIBILITY_DISTANCE))
                    player.Out.SendRiding(this, _stableRouteMount, true);
                if (_stableRouteMount.ObjectState is eObjectState.Active)
                    _stableRouteMount.RemoveFromWorld();
                _stableRouteMount.Delete();
            }
            _stableRouteMount = null;
            _stableRideViewers.Clear();
            _nextStableViewerRefreshTick = 0;
            _stableRouteLastMovementTick = 0;
            if (ObjectState is eObjectState.Active)
                ClientService.UpdateNpcForPlayers(this, false);
        }

        protected override bool RetainCorpseForResurrection => true;
        protected override bool RetainGroupWhenDead => true;

        public override void ProcessDeath(GameObject killer)
        {
            // #644: RAID_DEATH when this bot is a raider in a live raid attempt (logging only; read before the death clears anything).
            RaidAttemptRows.NoteDeath(this, killer);
            SetSeatedForClients(false);
            AutonomousPetSupport.CancelPendingCharm(this);
            _deathTick = GameLoop.GameLoopTime;
            _deathRegionId = CurrentRegionID;
            _deathLocation = new Point3D(X, Y, Z);
            GameLiving killerSide = killer is GameLiving killerLiving ? DOL.GS.ServerRules.PvpRules.Controller(killerLiving) : null;
            _lastDeathRvr = DOL.GS.ServerRules.PvpRules.IsAutonomousGamebot(this) && (InCombatPvP || killer is Keeps.GameKeepGuard ||
                killerSide != null && killerSide.Realm != eRealm.None && killerSide.Realm != Realm);
            IsReturningAfterRelease = false;
            _stableReturnPlan = null;
            CompleteStableMasterRoute();
            BotDutyLeases.OnDied(this); // a battleground rally drops its dead (BotDutyLeaseRules.EndsOnDeath)
            // 1.1.3, after RC6: how many were on it, read before the death clears the tracker, for how the death is told.
            int attackers = IsAutonomousWorldBot ? AutonomousDeathDiagnostics.Attackers(this) : 0;

            // A buffbot (NoLiferBuffBots) dies quietly: no record, no chat, no nemesis; it releases to a bind and goes back.
            if (IsNoLiferBuffBot)
                NoLiferBuffBots.OnDied(this, killer);
            else if (IsAutonomousWorldBot && PersistentRecord != null)
            {
                AutonomousDeathDiagnostics.Record(this, killer, PersistentRecord.Activity);
                PersistentRecord.DeathCount++;
                GoalDiagnosticAttempt?.Died();
                PersistentRecord.TargetName = (TargetObject as GameLiving)?.Name ?? killer?.Name ?? string.Empty;
                PersistentRecord.Activity = "Defeated; reassessing target difficulty";
                PersistentRecord.ObjectiveProgress = "The next grind target will be a lower con, never below green";
                MarkAutonomousStateDirty();
            }

            base.ProcessDeath(killer);
            BotDeathAnimation.OnDied(this); // 1.2.1: the fall in the original client (bot_death_animation)
            if (IsAutonomousWorldBot && !IsNoLiferBuffBot)
            {
                AutonomousBotStatusPersistence.Queue(this);
                // A hired companion has its death lines, rez asks and nemesis like any gamebot (owner 2026-11-18 09:14: the
                // same chat framework); it has no guild to grieve and no skill profile.
                if (!IsHiredCompanion)
                {
                    AutonomousBotGuildSocial.OnBotDeath(this);
                    BotSkill.OnDied(this); // Bots as people WP1: a death is a lesson (at most one in 10 minutes)
                }
                if (!CompanionChat165.IsFormal(this))
                    AutonomousBotVoices.OnBotDeath(this, killer, attackers);
                AutonomousLivingServer.OnBotDied(this, killer);
            }
            StartDeathRecoveryTimer();
            // a hired companion's player sees it DEAD in the roster at once (2026-11-01)
            if (IsHiredCompanion && HiredCompanions165.OwnerOf(this) is { } hirer)
                Hire165.SendRosterNumbers(hirer);
        }

        private void StartDeathRecoveryTimer()
        {
            _deathRecoveryTimer?.Stop();
            _deathRecoveryTimer = new ECSGameTimer(this)
            {
                Callback = HandleDeathRecovery
            };
            _deathRecoveryTimer.Start(3000);
        }

        private long _nextDeathRecoveryErrorTick;

        private int HandleDeathRecovery(ECSGameTimer timer)
        {
            if (ObjectState is not eObjectState.Active)
                return 0;
            if (IsAlive)
            {
                _deathRecoveryTimer = null;
                return 0;
            }

            long deadFor = GameLoop.GameLoopTime - _deathTick;
            // A hired companion's body stays where it fell, then fades to wait for a healer (HiredCompanions165; it never
            // releases to a bind as a gamebot does).
            int hiredNext = HiredCompanions165.DeathTick(this, deadFor);
            if (hiredNext >= 0)
            {
                if (hiredNext == 0)
                    _deathRecoveryTimer = null;
                return hiredNext;
            }
            AutonomousBotGroupCoordinator.PveCorpseDisposition groupDisposition =
                AutonomousBotGroupCoordinator.PveCorpseRecovery(this);
            if (groupDisposition == AutonomousBotGroupCoordinator.PveCorpseDisposition.HoldForResurrection)
                return 3000;
            // A siege attacker also waits for any healer of its army nearby (AutonomousSiegeResurrection), not only its party's.
            // Living Server: a real player of the realm who can rez, nearby but not grouped, also earns the long wait.
            bool resurrectionPossible = HasViableResurrector() || AutonomousLivingServer.HasNearbyPlayerRezzer(this) ||
                IsAutonomousWorldBot && AutonomousSiegeResurrection.IsSiegeAttacker(this) &&
                AutonomousSiegeResurrection.HasArmyResurrectorNear(this);
            // A siege attacker's release is a long walk back from its realm's muster (run 18, 2026-10-17: Dun Dagda,
            // 176,000 units), so with a resurrector in its party it waits longer for a raise, as players did.
            long releaseDelay = resurrectionPossible
                ? IsAutonomousWorldBot && AutonomousRvrEventLayer.TrySiegeRelease(AutonomousBotGroupCoordinator.RvrForceId(this), Realm,
                    GameLoop.GameLoopTime, out _, out _) ? SiegeCorpseWaitMilliseconds : 90_000
                // #485: a gamebot with nobody to rez it releases after its own 15-45 s, so a wipe does not vanish in one beat;
                // /spawn helpers keep their 20 s.
                : IsAutonomousWorldBot ? BotReleaseTiming.WipeReleaseMs(DatabaseID, PersistentRecord?.DeathCount ?? 0) : 20_000;
            if (groupDisposition is not (AutonomousBotGroupCoordinator.PveCorpseDisposition.ReleaseAndDisband or
                AutonomousBotGroupCoordinator.PveCorpseDisposition.ReleaseAndRejoin) &&
                deadFor < releaseDelay)
                return 3000;

            // Leave the corpse and its retry timer intact while the owner is
            // dead or the party is still fighting. Reviving first would send
            // a one-third-health helper straight back into the corpse fight.
            if (IsTemporaryGroupHelper && !TemporaryCompanionRecovery.CanReleaseToOwner(this))
                return 3000;

            try
            {
                ReleaseToNearestBindAndReturn(groupDisposition !=
                    AutonomousBotGroupCoordinator.PveCorpseDisposition.ReleaseAndDisband);
            }
            catch (Exception exception)
            {
                // A transient transfer failure must not kill the only release timer.
                // Rate limit per character; no DB retry/sleep on the game thread.
                if (GameLoop.GameLoopTime >= _nextDeathRecoveryErrorTick)
                {
                    _nextDeathRecoveryErrorTick = GameLoop.GameLoopTime + 60_000;
                    log.Error($"BOT_RELEASE_RETRY bot={Name} id={DatabaseID} region={_deathRegionId}", exception);
                }
                return 3000;
            }
            if (!IsAlive)
                return 3000; // A failed destination/transfer must retain the recovery timer.
            _deathRecoveryTimer = null;
            return 0;
        }

        public const long SiegeCorpseWaitMilliseconds = 240_000;

        // Fork: /allrelease runs the death timer's release at once; the timer ends itself once the bot is alive
        public bool ReleaseNow()
        {
            if (IsAlive || ObjectState != eObjectState.Active)
                return false;
            ReleaseToNearestBindAndReturn();
            return IsAlive;
        }

        private bool HasViableResurrector()
        {
            if (Group == null)
                return false;

            foreach (GameLiving member in Group.GetMembersInTheGroup())
            {
                if (member == this || !member.IsAlive || member.CurrentRegionID != CurrentRegionID)
                    continue;
                if (member is GameBot bot && bot.ResurrectionSpell != null && bot.Mana >= bot.PowerCost(bot.ResurrectionSpell))
                    return true;
                if (member is GamePlayer player && player.CharacterClass?.ID is
                    (int)eCharacterClass.Cleric or (int)eCharacterClass.Friar or
                    (int)eCharacterClass.Healer or (int)eCharacterClass.Shaman or
                    (int)eCharacterClass.Druid or (int)eCharacterClass.Warden or (int)eCharacterClass.Bard)
                    return true;
            }
            return false;
        }

        private void ReleaseToNearestBindAndReturn(bool returnToParty = true)
        {
            if (IsTemporaryGroupHelper)
            {
                if (!TemporaryCompanionRecovery.CanReleaseToOwner(this))
                    return;
                // Move the corpse FIRST. Reviving before a failed MoveTo left a
                // living helper at its killer with an expired recovery timer.
                Vector3 destination = TemporaryGroupStableTravel.FormationPoint(Owner, CompanionRaid.IsMember(this) ? Math.Max(0, GroupIndex - 1) : ObjectID % 6);
                StopCurrentSpellcast();
                (Brain as BotBrain)?.PrepareForTankPull();
                StopMoving();
                if (!MoveCorpseForRelease(Owner.CurrentRegionID, (int)Math.Round(destination.X),
                    (int)Math.Round(destination.Y), (int)Math.Round(destination.Z), Owner.Heading))
                    return;

                Health = Math.Max(1, MaxHealth);
                Mana = Math.Max(0, MaxMana);
                Endurance = Math.Max(0, MaxEndurance);
                IsReturningAfterRelease = false;
                _stableReturnPlan = null;
                EnterPlayerLedGroup(Owner);
                Brain?.FSM?.SetCurrentState(eFSMStateType.FOLLOW);
                Brain?.Start();
                ShowStandingToViewers();
                return;
            }

            Point3D release = FindNearestBindPoint();
            ushort releaseRegion = _deathRegionId;
            if (IsAutonomousWorldBot && release != null)
            {
                Zone releaseZone = WorldMgr.GetRegion(releaseRegion)?.GetZone(release.X, release.Y);
                if (releaseZone == null || !AutonomousRealmBoundary.Allows(Realm, releaseRegion, releaseZone.ID))
                    release = null;
            }
            if (release == null && IsAutonomousWorldBot) BotGroupRelease.TryPortalKeep(this, releaseRegion, returnToParty, out release); // Fork: battlegrounds have no bind points
            if (release == null && IsAutonomousWorldBot && returnToParty &&
                AutonomousRealmRaid.TryRaidRelease(this, out ushort hubRegion, out Point3D hub))
            {
                releaseRegion = hubRegion;
                release = hub;
            }
            if (release == null && IsAutonomousWorldBot &&
                AutonomousRvrEventLayer.TrySiegeRelease(AutonomousBotGroupCoordinator.RvrForceId(this), Realm,
                    GameLoop.GameLoopTime, out ushort musterRegion, out Point3D muster))
            {
                releaseRegion = musterRegion;
                release = muster;
            }
            if (release == null && IsAutonomousWorldBot)
            {
                var capital = AutonomousStuckWatchdog.SafeCapitalFor(Realm);
                releaseRegion = capital.RegionId;
                release = new Point3D(capital.X, capital.Y, capital.Z);
            }
            release ??= _deathLocation;
            // RC6 on the owner's PC: "long run back to Pennine Mountains" and "released, running back" in the same second.
            bool toldGroup = IsAutonomousWorldBot && AutonomousLivingServer.OnBotReleasing(this);
            // Transfer the corpse first: a failed MoveTo must not revive the bot
            // at its killer or cause HandleDeathRecovery to discard its timer.
            if (!MoveCorpseForRelease(releaseRegion, release.X, release.Y, release.Z, Heading))
                return;
            BotGroupRelease.NoteRelease(this, releaseRegion, release, returnToParty); // Fork: a release in a player's group shows in the log
            Health = Math.Max(1, MaxHealth / 3);
            Mana = Math.Max(0, MaxMana / 3);
            Endurance = Math.Max(0, MaxEndurance / 3);
            IsReturningAfterRelease = returnToParty;
            if (returnToParty) AutonomousRealmRaid.RejoinAfterRelease(this);
            _nextReturnRoleplayTick = GameLoop.GameLoopTime + Util.Random(45_000, 90_000);
            Brain?.FSM?.SetCurrentState(eFSMStateType.FOLLOW);
            Brain?.Start();
            ShowStandingToViewers();
            ApplyRvrResurrectionIllness();   // #521: a release after an RvR death gives it too, as GamePlayer.Release does
            if (returnToParty && !toldGroup)
                TellOwnGroup(ReturnDialogue("release"));

            if (PersistentRecord != null)
            {
                PersistentRecord.Activity = returnToParty ? "Released and returning on foot" : "Released after party disbanded";
                if (returnToParty)
                    PersistentRecord.CurrentGoal = $"Return to the party near {_deathLocation.X}, {_deathLocation.Y}";
                PersistentRecord.TravelDestination = CurrentZone?.Description ?? string.Empty;
                MarkAutonomousStateDirty();
            }
        }

        /// <summary>How far from a repeat-death spot a release bind must be when there is a choice.</summary>
        public const float ReleaseClearanceFromDeathSpot = 6_000;
        /// <summary>
        /// A release moves the corpse first and raises it after: the move re-created the bot for every viewer while it
        /// was still dead, and a client keeps an NPC created dead lying down whatever its health says later, so it showed
        /// as a second dead body (owner 2026-10-30). Re-send it standing.
        /// </summary>
        private void ShowStandingToViewers()
        {
            if (!IsAlive || ObjectState != eObjectState.Active)
                return;
            foreach (GamePlayer viewer in GetPlayersInRadius(WorldMgr.VISIBILITY_DISTANCE))
            {
                if (viewer.Client?.ClientState is not GameClient.eClientState.Playing || !viewer.CanDetect(this))
                    continue;
                viewer.Out.SendObjectRemove(this);
                ClientService.CreateObjectForPlayer(viewer, this);
                viewer.Out.SendLivingEquipmentUpdate(this);
            }
        }

        private Point3D? FindNearestBindPoint()
        {
            if (!IsAutonomousWorldBot)
                return BotReleaseBindPoints.Nearest(_deathRegionId, X, Y);
            // A bot that keeps dying in one place releases to a bind well away from it, not to the stone it walks back
            // into the same pack from (Sheeroe Hills, 2026-10-20: two bots died 72 and 61 times to one glimmer pack about
            // 2,300 units from their release bind).
            var spots = AutonomousRepeatDeathSpots.SpotsNear(DatabaseID, _deathRegionId, X, Y, DateTime.UtcNow,
                AutonomousRepeatDeathSpots.AvoidRadius);
            return BotReleaseBindPoints.NearestAwayFrom(_deathRegionId, X, Y, Realm, spots, ReleaseClearanceFromDeathSpot);
        }

        public void OnResurrectedBy(GameLiving caster)
        {
            AutonomousRealmRaid.ClearCorpseWait(this);
            _deathRecoveryTimer?.Stop();
            _deathRecoveryTimer = null;
            IsReturningAfterRelease = false;
            _stableReturnPlan = null;
            CompleteStableMasterRoute();
            if (IsTemporaryGroupHelper)
                TryReturnTemporaryCompanionToLeader(true);
            HiredCompanions165.OnRaised(this);   // back in its player's group and at their side
            ApplyRvrResurrectionIllness();
            Brain?.FSM?.SetCurrentState(eFSMStateType.FOLLOW);
            Brain?.Start();
            if (IsAutonomousWorldBot && AutonomousLivingServer.OnBotResurrected(this, caster))
                return;
            Say(Random.Shared.Next(3) switch
            {
                0 => $"My thanks, {caster?.Name ?? "friend"}. I am ready to continue.",
                1 => "That was too close. Give me a moment, then I will fall back into formation.",
                _ => "Back among the living. I will stay nearer the group this time."
            });
        }

        private long _nextCompanionRecallTick;

        public bool TryReturnTemporaryCompanionToLeader(bool afterRevival = false)
        {
            // Even persistent bots in a human's party retain their walk-back.
            if (!IsTemporaryGroupHelper || !IsAlive || Owner?.IsAlive != true ||
                Owner.ObjectState != eObjectState.Active || Owner.CurrentZone == null)
                return false;
            if ((afterRevival || IsReturningAfterRelease) && !TemporaryCompanionRecovery.CanReleaseToOwner(this))
                return false;
            if (!afterRevival && GameLoop.GameLoopTime < _nextCompanionRecallTick)
                return false;
            _nextCompanionRecallTick = GameLoop.GameLoopTime + TemporaryCompanionRecovery.CheckIntervalMilliseconds;
            bool sameGroup = Group != null && Group == Owner.Group && Group.IsInTheGroup(this) && Group.IsInTheGroup(Owner);
            if (!TemporaryCompanionRecovery.ShouldRecall(true, IsAlive, ObjectState == eObjectState.Active,
                sameGroup, PlayerGroupLeader == Owner, IsOnStableMasterRoute, Owner.IsOnHorse,
                CurrentRegionID == Owner.CurrentRegionID, GetDistanceTo(Owner), afterRevival || IsReturningAfterRelease))
                return false;

            // A combat knockback/dragon throw must not recall and reset an entire raid.
            // Genuine portal transfers use the separate transfer coordinator.
            if (DragonCombatGeometry.IsRecoveringFromThrow(Owner) ||
                CurrentRegionID == Owner.CurrentRegionID && TemporaryCompanionRecovery.HasPartyCombat(this) &&
                !TemporaryCompanionRecovery.FrontierChaseLeash(this))
                return false;
            // A raid squad on a keep siege holds its post until the event ends (goal 11: no leash during sieges).
            if (CurrentRegionID == Owner.CurrentRegionID && CompanionRaidSiege.Holds(this))
                return false;

            Vector3 destination = TemporaryGroupStableTravel.FormationPoint(Owner, CompanionRaid.IsMember(this) ? Math.Max(0, GroupIndex - 1) : ObjectID % 6);
            StopCurrentSpellcast();
            WakeRecoveryRest();
            (Brain as BotBrain)?.PrepareForTankPull();
            StopMoving();
            if (!MoveTo(Owner.CurrentRegionID, (int)Math.Round(destination.X), (int)Math.Round(destination.Y),
                (int)Math.Round(destination.Z), Owner.Heading))
                return false;

            IsReturningAfterRelease = false;
            _stableReturnPlan = null;
            EnterPlayerLedGroup(Owner);
            Brain?.Start();
            return true;
        }

        // The autonomous controller owns released solo and group journeys.
        // Human-led companions retain their existing return-to-player behavior.
        public void HandOverReleaseReturnToGroup()
        {
            if (!IsAutonomousWorldBot || IsTemporaryGroupHelper || IsPlayerLedGroup ||
                !IsAlive || !IsReturningAfterRelease || IsOnStableMasterRoute)
                return;
            IsReturningAfterRelease = false;
            _stableReturnPlan = null;
            StopMovingOnPath();
            StopMoving();
        }

        /// <summary>Ends a walk back to the party (a stuck recovery put the bot with its group).</summary>
        public void EndReturnJourney()
        {
            IsReturningAfterRelease = false;
            _stableReturnPlan = null;
        }

        /// <summary>
        /// Living Server: a gamebot in a player's group that stalled on its way back plans it again from where it stands
        /// (a stablemaster route when one is faster, else a walk with detours around hostile monsters) before
        /// BotGroupRelease lets it join the leader. False when that can't apply here.
        /// </summary>
        public bool RestartReturnToLeader(GamePlayer leader)
        {
            if (!IsAlive || IsOnStableMasterRoute || leader == null || leader.CurrentRegionID != CurrentRegionID)
                return false;
            StopFollowing();
            StopMovingOnPath();
            StopMoving();
            WakeRecoveryRest();
            (Brain as BotBrain)?.Disengage();
            _stableReturnPlan = null;
            IsReturningAfterRelease = true;
            Brain?.FSM?.SetCurrentState(eFSMStateType.FOLLOW);
            return true;
        }

        public bool ContinueReturnJourney()
        {
            if (!IsReturningAfterRelease || !IsAlive)
                return false;

            if (IsTemporaryGroupHelper)
            {
                TryReturnTemporaryCompanionToLeader(true);
                return IsReturningAfterRelease;
            }

            GamePlayer leader = PlayerGroupLeader ?? Owner;
            Point3D destination = leader?.IsAlive == true && leader.CurrentRegionID == CurrentRegionID
                ? new Point3D(leader.X, leader.Y, leader.Z)
                : _deathLocation;
            if (BotGroupRelease.WaitsForLeader(this)) return true; // Fork: its corpse is no goal, the group goes on where the player comes back
            if ((CurrentRegionID != _deathRegionId || BotGroupRelease.IsGroupReturn(this)) && (leader == null || leader.CurrentRegionID != CurrentRegionID)) // Fork: its leader left the region it died in
            {
                // Living Server: a stablemaster ride in progress ends at its stable before the bot may join its leader.
                if (IsOnStableMasterRoute)
                {
                    MaintainStableMasterRoute();
                    if (IsMovingOnPath || CurrentPathPoint != null || !TryCompleteStableMasterRouteAfterArrival())
                        return true;
                    _stableReturnPlan = null;
                }
                return BotGroupRelease.TryReturnToLeader(this, _deathTick); // Fork: it stood here for good, nothing else moves a bot in a player's group
            }

            if (GetDistanceTo(destination) <= 350)
            {
                IsReturningAfterRelease = false;
                _stableReturnPlan = null;
                CompleteStableMasterRoute();
                // 1.1.3 (RC5 on the owner's PC): "back" only after a death; a bot that walked to the player after an invite is "here".
                TellOwnGroup(ReturnDialogue(_deathTick > 0 && GameLoop.GameLoopTime - _deathTick < 20 * 60_000 ? "arrive" : "arrive_walk"));
                Brain?.FSM?.SetCurrentState(eFSMStateType.FOLLOW);
                BotGroupRelease.FinishReturn(this); // Fork: as an invitation does
                return false;
            }

            if (IsOnStableMasterRoute)
            {
                MaintainStableMasterRoute();
                if (IsMovingOnPath || CurrentPathPoint != null)
                    return true;
                if (!TryCompleteStableMasterRouteAfterArrival())
                    return true;
                _stableReturnPlan = null;
                TellOwnGroup(ReturnDialogue("dismount"));
            }

            _stableReturnPlan ??= FindFasterStableRoute(destination);
            if (_stableReturnPlan != null)
            {
                if (!IsWithinRadius(_stableReturnPlan.Master, 225))
                {
                    WalkReturnStep(new Point3D(_stableReturnPlan.Master.X, _stableReturnPlan.Master.Y, _stableReturnPlan.Master.Z));
                    MaybeExplainReturn("stable");
                    return true;
                }

                if (DistanceSquared(X, Y, _stableReturnPlan.Route.X, _stableReturnPlan.Route.Y) > 45L * 45L)
                {
                    WalkReturnStep(new Point3D(_stableReturnPlan.Route.X, _stableReturnPlan.Route.Y, _stableReturnPlan.Route.Z));
                    return true;
                }

                long price = Math.Max(0, _stableReturnPlan.Ticket.Price);
                if (price > 0 && !AutonomousBotEconomy.TrySpend(DatabaseID, price))
                {
                    _stableReturnPlan = null;
                    return true;
                }
                if (!BeginStableMasterRoute(_stableReturnPlan.DestinationName,
                        TimeSpan.FromSeconds(_stableReturnPlan.RideSeconds), _stableReturnPlan.Route, _stableReturnPlan.Ticket))
                {
                    if (price > 0)
                        AutonomousBotEconomy.AddMoney(DatabaseID, price);
                    _stableReturnPlan = null;
                    return true;
                }
                _stableReturnPlan = null;
                TellOwnGroup(ReturnDialogue("mount"));
                return true;
            }

            WalkReturnStep(destination);
            MaybeExplainReturn("walk");
            return true;
        }

        private void WalkReturnStep(Point3D destination)
        {
            Vector3 current = new(X, Y, Z);
            Vector3 desired = new(destination.X, destination.Y, destination.Z);
            // Fork: 900-unit steps stopped at the first wall, a planned route finds the door. Living Server: a detour around a
            // hostile monster still comes first, so only a clear way is walked as one planned route.
            AutonomousThreatAwarePathing.SafeStep step = AutonomousThreatAwarePathing.ChooseStep(this, current, desired, 900);
            PathTo(BotGroupRelease.ReturnStepTarget(BotGroupRelease.IsGroupReturn(this), step.Detouring, step.Position, desired), MaxSpeed);
            if (PersistentRecord != null)
            {
                PersistentRecord.Activity = step.Detouring ? step.Reason : "Walking back to the party";
                PersistentRecord.CurrentGoal = "Return to party after release";
                MarkAutonomousStateDirty();
            }
        }

        private AutonomousStableRoutePlanner.Choice FindFasterStableRoute(Point3D destination)
        {
            return CurrentRegion == null
                ? null
                : AutonomousStableRoutePlanner.FindBest(this, new Vector3(destination.X, destination.Y, destination.Z));
        }

        private void MaybeExplainReturn(string stage)
        {
            if (GameLoop.GameLoopTime < _nextReturnRoleplayTick || IsCasting || InCombat)
                return;
            _nextReturnRoleplayTick = GameLoop.GameLoopTime + Util.Random(60_000, 120_000);
            TellOwnGroup(ReturnDialogue(stage));
        }

        /// <summary>
        /// Where a line about the way back after a release goes: the bot's own group, and only when a player is in it (1.1.3 RC1,
        /// live check 2026-11-08 at Camelot's bind: about 25 "released, running back" lines in 45 s in /say, from bots of other
        /// groups, to everyone standing there). "group", or null when nobody needs to hear it. Pure.
        /// </summary>
        public static string ReturnLineChannel(bool inGroup, bool groupHasPlayer) => inGroup && groupHasPlayer ? "group" : null;

        private void TellOwnGroup(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || ReturnLineChannel(Group != null, AutonomousLivingServer.GroupHasPlayer(Group)) == null)
                return;
            text = AutonomousRoleplayers.Voice(this, text);
            BotSayGate.Say(new BotSayGate.Utterance("group", Name, Realm, text, who: this), said =>
            {
                Group.SendMessageToGroupMembers(this, said, eChatType.CT_Group, eChatLoc.CL_ChatWindow);
                return said;
            });
        }

        // Play test 2026-10-27: an autonomous gamebot in the player's group said "I have caught up. Returning to formation." —
        // the companion bots' formal voice. A gamebot talks like a player.
        // A hired companion talks as any gamebot (owner 2026-11-18 09:14); with its Speech option on Formal it keeps the
        // formal companion return lines.
        private string ReturnDialogue(string stage) => IsAutonomousWorldBot && !(IsHiredCompanion && CompanionChat165.IsFormal(this))
            ? GamebotReturnLine(stage, Random.Shared, StableRouteDestination, CurrentZone?.Description)
            : CompanionReturnDialogue(stage);

        /// <summary>What an autonomous gamebot says on its way back to the player's group. Pure.</summary>
        public static string GamebotReturnLine(string stage, Random random, string stableDestination, string zone) => stage switch
        {
            "release" => random.Next(3) switch
            {
                0 => "released, running back",
                1 => "at bind, omw back",   // promise: true (its real walk back to the group)
                _ => "no rez, released. coming back to you",   // promise: true (its real walk back to the group)
            },
            "stable" => "grabbing a horse, faster",
            "mount" => string.IsNullOrEmpty(stableDestination) ? "on a horse, be there soon" : $"horse to {stableDestination}, be there soon",   // promise: true (its real ride back to the group)
            "dismount" => "off the horse, almost there",
            "arrive_walk" => random.Next(3) switch
            {
                0 => "here",
                1 => "made it, ready",
                _ => "ok here, whats the plan",
            },
            "arrive" => random.Next(3) switch
            {
                0 => "back",
                1 => "here, ready",
                _ => "made it, whats next",
            },
            _ => random.Next(3) switch
            {
                0 => "still omw",   // promise: true (its real walk back to the group)
                1 => "taking the long way round some mobs, almost there",
                _ => string.IsNullOrEmpty(zone) ? "omw back" : $"in {zone}, omw back",   // promise: true (its real walk back to the group)
            },
        };

        private string CompanionReturnDialogue(string stage) => stage switch
        {
            "release" => Random.Shared.Next(3) switch
            {
                0 => "No resurrection is coming. I have released, and I am making my way back.",   // promise: none (a companion says no rez is coming)
                1 => "I woke at the bindstone. Hold the road; I will return on foot.",
                _ => "The healers could not reach me. I am alive again and starting the journey back."
            },
            "stable" => "The stable is on my route. A horse should save time from here.",
            "mount" => $"I found a faster horse route toward {StableRouteDestination}. I am taking it now.",
            "dismount" => "Off the horse. I will cover the rest of the road on foot.",
            "arrive" => "I have caught up. Returning to formation.",
            _ => Random.Shared.Next(4) switch
            {
                0 => "Still on the road. I am avoiding the stronger creatures along the way.",
                1 => "I know the direction now. Keep going; I will catch up.",
                2 => "The road is longer than it looked from the bindstone, but I am making progress.",
                _ => $"I am crossing {CurrentZone?.Description ?? "the wilds"} and heading back to the group."
            }
        };

        private static long DistanceSquared(int ax, int ay, int bx, int by)
        {
            long dx = (long)ax - bx;
            long dy = (long)ay - by;
            return dx * dx + dy * dy;
        }

        #endregion

        #region IGamePlayer — Sprint

        public bool IsSprinting => effectListComponent.ContainsEffectForEffectType(eEffect.Sprint);

        public virtual bool Sprint(bool state)
        {
            if (state == IsSprinting)
                return state;

            if (state)
            {
                if (Endurance <= 10 || IsStealthed || !IsAlive)
                    return false;

                ECSGameEffectFactory.Create(new ECSGameEffectInitParams(this, 0, 1),
                    static (in ECSGameEffectInitParams i) => new SprintECSGameEffect(i));
                return true;
            }
            else
            {
                ECSGameEffect effect = EffectListService.GetEffectOnTarget(this, eEffect.Sprint);
                effect?.End();
                return false;
            }
        }

        #endregion

        #region IGamePlayer — Status Flags

        public bool CanBreathUnderWater { get; set; }
        public bool IsOverencumbered { get; set; }
        public int Encumberance => 0;
        public int MaxEncumberance => 0;

        #endregion

        #region IGamePlayer — Skill System

        protected readonly Dictionary<string, Specialization> m_specialization = new Dictionary<string, Specialization>();
        protected readonly List<SpellLine> m_spellLines = new List<SpellLine>();

        public virtual bool HasSpecialization(string keyName)
        {
            lock (((ICollection)m_specialization).SyncRoot)
            {
                return m_specialization.ContainsKey(keyName);
            }
        }

        public virtual SpellLine GetSpellLine(string keyname)
        {
            lock (m_spellLines)
            {
                return m_spellLines.FirstOrDefault(sl => sl.KeyName == keyname);
            }
        }

        #endregion

        #region IGamePlayer — Equipment / Armor

        // Bot armor works exactly like a player's (owner 2026-10-31, major): the piece that is hit is chosen with the
        // player's odds (or the style's fixed location), its AF is capped by level and armor type, quality and condition
        // count, and its absorb comes from the armor type. Before this a bot defended like a monster of its level and
        // its worn armor did nothing. Copies of GamePlayer.CalculateArmorHitLocation / GetArmorFactorCap / GetArmorAF /
        // GetArmorAbsorb.
        public virtual eArmorSlot CalculateArmorHitLocation(AttackData ad)
        {
            if (ad?.Style != null && ad.Style.ArmorHitLocation != eArmorSlot.NOTSET)
                return ad.Style.ArmorHitLocation;
            int chance = Util.Random(1, 100);
            if (chance <= 40) return eArmorSlot.TORSO;
            if (chance <= 65) return eArmorSlot.LEGS;
            if (chance <= 80) return eArmorSlot.ARMS;
            if (chance <= 90) return eArmorSlot.HEAD;
            if (chance <= 95) return eArmorSlot.HAND;
            return eArmorSlot.FEET;
        }

        public int GetArmorFactorCap(eObjectType type, out int itemArmorFactorCap)
        {
            int modifiedCharacterLevel = Level;
            if (RealmLevel > 39)
                modifiedCharacterLevel++;
            int playerArmorFactorCap = modifiedCharacterLevel * 2;
            itemArmorFactorCap = type is eObjectType.Cloth ? modifiedCharacterLevel : playerArmorFactorCap;
            return playerArmorFactorCap;
        }

        public override double GetArmorAF(eArmorSlot slot)
        {
            if (slot is eArmorSlot.NOTSET)
                return 0;
            DbInventoryItem item = Inventory?.GetItem((eInventorySlot)slot);
            if (item == null || !GlobalConstants.IsArmor(item.Object_Type))
                return 0;
            int armorFactorCap = GetArmorFactorCap((eObjectType)item.Object_Type, out int itemArmorFactorCap);
            double armorFactor = Math.Min(item.DPS_AF, itemArmorFactorCap);
            armorFactor += BaseBuffBonusCategory[eProperty.ArmorFactor] / 5.0;
            armorFactor *= item.Quality * 0.01 * item.ConditionPercent * 0.01;
            armorFactor = Math.Min(armorFactor, armorFactorCap);
            armorFactor += GetModified(eProperty.ArmorFactor) / 5.0;
            return Math.Max(0, armorFactor);
        }

        public override double GetArmorAbsorb(eArmorSlot slot)
        {
            if (slot is eArmorSlot.NOTSET)
                return 0;
            DbInventoryItem item = Inventory?.GetItem((eInventorySlot)slot);
            if (item == null)
                return 0;
            double absorb = item.SPD_ABS * 0.01 * (1 + GetModified(eProperty.ArmorAbsorption) * 0.01);
            return Math.Clamp(absorb, 0, 1);
        }

        public double WeaponDamageWithoutQualityAndCondition(DbInventoryItem weapon)
        {
            if (weapon == null)
                return 0;

            double weaponDps = weapon.DPS_AF * 0.1;
            double dpsCap = 1.2 + 0.3 * Level;
            if (RealmLevel > 39)
                dpsCap += 0.3;

            double dps = Math.Min(weaponDps, dpsCap);
            return dps * (1 + GetModified(eProperty.DPS) * 0.01);
        }

        public override int GetWeaponStat(DbInventoryItem weapon)
        {
            if (weapon != null)
            {
                switch ((eObjectType)weapon.Object_Type)
                {
                    case eObjectType.Staff:
                    case eObjectType.Fired:
                    case eObjectType.Longbow:
                    case eObjectType.Crossbow:
                    case eObjectType.CompositeBow:
                    case eObjectType.RecurvedBow:
                    case eObjectType.Thrown:
                    case eObjectType.Shield:
                        return GetModified(eProperty.Dexterity);

                    case eObjectType.ThrustWeapon:
                    case eObjectType.Piercing:
                    case eObjectType.Spear:
                    case eObjectType.Flexible:
                    case eObjectType.HandToHand:
                        return (GetModified(eProperty.Strength) + GetModified(eProperty.Dexterity)) >> 1;
                }
            }

            return GetModified(eProperty.Strength);
        }

        public override double GetWeaponSkill(DbInventoryItem weapon)
        {
            if (weapon == null || CharacterClass == null)
                return 0;

            int classBaseWeaponSkill = (eInventorySlot)weapon.SlotPosition is eInventorySlot.DistanceWeapon
                ? CharacterClass.WeaponSkillRangedBase
                : CharacterClass.WeaponSkillBase;
            double weaponSkill = Level * classBaseWeaponSkill / 200.0 *
                                 (1 + 0.01 * GetWeaponStat(weapon) / 2) * Effectiveness;
            return Math.Max(1, weaponSkill * GetModified(eProperty.WeaponSkill) * 0.01);
        }

        public override int WeaponSpecLevel(eObjectType objectType, int slotPosition)
        {
            if (objectType is eObjectType.LeftAxe && slotPosition is not Slot.LEFTHAND)
                objectType = eObjectType.Axe;
            else if (slotPosition is Slot.LEFTHAND && objectType is eObjectType.Axe)
                objectType = eObjectType.LeftAxe;

            string specName = SkillBase.ObjectTypeToSpec(objectType);
            return string.IsNullOrWhiteSpace(specName) ? 0 : GetModifiedSpecLevel(specName);
        }

        public override int WeaponSpecLevel(DbInventoryItem weapon) =>
            weapon == null ? 0 : WeaponSpecLevel((eObjectType)weapon.Object_Type, weapon.SlotPosition);

        #endregion

        #region IGamePlayer — XP / Realm Points

        public object GameStaticItemOwnerComparand => DatabaseID > 0 ? $"offlinebot:{DatabaseID}" : this;

        public TryPickUpResult TryAutoPickUpMoney(GameMoney money)
        {
            money.AssertLockAcquisition();
            long share = AutonomousBotGuildEconomy.ShareAfterDues(this, money.Value, out long dues, out Guild duesGuild);
            // The whole coin must fit in the purse before it is split, so returned dues can never be lost (audit 2026-10-16).
            // The Epic Special Forces take nothing: the realm's own raiders keep every coin and drop.
            if (!IsAutonomousWorldBot || IsTemporaryGroupHelper || IsInMemoryOnly || DatabaseID <= 0 ||
                PersistentRecord != null && PersistentRecord.MoneyCopper > long.MaxValue - money.Value ||
                !AutonomousBotEconomy.AddMoney(DatabaseID, share))
                return TryPickUpResult.DoesNotWant;
            // Guild dues come out of the same real coin; a full bank leaves them with the bot.
            if (!AutonomousBotGuildEconomy.DepositDues(duesGuild, dues))
                AutonomousBotEconomy.AddMoney(DatabaseID, dues);

            money.RemoveFromWorld();
            AutonomousStuckWatchdog.MarkProgress(this, eAutonomousProgressKind.Loot);
            MarkAutonomousStateDirty();
            return TryPickUpResult.Success;
        }

        private string _reportedLootRejection;

        public TryPickUpResult TryAutoPickUpItem(WorldInventoryItem item)
        {
            item.AssertLockAcquisition();
            if (!IsAutonomousWorldBot || IsTemporaryGroupHelper || IsInMemoryOnly)
                return TryPickUpResult.DoesNotWant;
            if (Inventory == null)
            {
                ReportLootRejection(item, "missing_inventory");
                return TryPickUpResult.DoesNotWant;
            }

            bool stored = item.Item.IsStackable
                ? Inventory.AddTemplate(item.Item, item.Item.Count, eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack)
                : Inventory.AddItem(eInventorySlot.FirstEmptyBackpack, item.Item);
            if (!stored)
            {
                ReportLootRejection(item, Inventory.FindFirstEmptySlot(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack) == eInventorySlot.Invalid
                    ? "backpack_full" : "inventory_insert_failed");
                return TryPickUpResult.Blocked;
            }

            item.RemoveFromWorld();
            _reportedLootRejection = null;
            BotMilestoneLife.OnItemReceived(this, item.Item); // Bots as people WP6: in a high-end raid kill's loot, a dragon trophy
            AutonomousBotEconomy.MarkInventoryChanged(this);
            // Queue owned loot for the existing asynchronous save before appraisal.
            // This is not a synchronous commit; /spawn helpers never enter this path.
            MarkAutonomousStateDirty();
            AutonomousBotStatusPersistence.Queue(this, true);
            bool equipped = false;
            try
            {
                equipped = !item.Item.IsStackable && AutonomousBotEconomy.TryEquipOwnedUpgrade(this, item.Item, "loot");
            }
            catch (Exception exception)
            {
                // Receipt already succeeded. An appraisal failure must not make
                // the drop available twice or prevent the owned item being saved.
                const string appraisalFailure = "autonomous.telemetry.loot.appraisal_failed";
                if (!TempProperties.GetProperty<bool>(appraisalFailure))
                {
                    TempProperties.SetProperty(appraisalFailure, true);
                    if (log.IsErrorEnabled)
                        log.Error($"AUTONOMOUS_LOOT_APPRAISAL_FAILED bot=\"{Name}\" id={DatabaseID} item=\"{item.Item.Name}\" retainedInInventory=true", exception);
                }
            }
            AutonomousStuckWatchdog.MarkProgress(this, eAutonomousProgressKind.Loot);
            const string firstLootTelemetry = "autonomous.telemetry.first.loot";
            if (!TempProperties.GetProperty<bool>(firstLootTelemetry))
            {
                TempProperties.SetProperty(firstLootTelemetry, true);
                if (log.IsInfoEnabled)
                    log.Info($"AUTONOMOUS_FIRST_LOOT bot=\"{Name}\" id={DatabaseID} level={Level} " +
                         $"item=\"{item.Item.Name}\" count={item.Item.Count} equipped={equipped}");
            }
            return TryPickUpResult.Success;
        }

        private void ReportLootRejection(WorldInventoryItem item, string reason)
        {
            // One line until the rejection changes or a later pickup succeeds.
            // A full backpack remains full: never delete existing loot to make room.
            if (_reportedLootRejection == reason)
                return;
            _reportedLootRejection = reason;
            if (log.IsInfoEnabled)
                log.Info($"AUTONOMOUS_LOOT_REJECTED bot=\"{Name}\" id={DatabaseID} level={Level} " +
                     $"item=\"{item.Item.Name}\" reason={reason} dropRetained=true");
        }

        public TryPickUpResult TryPickUpMoney(GamePlayer source, GameMoney money) => TryPickUpResult.DoesNotWant;
        public TryPickUpResult TryPickUpItem(GamePlayer source, WorldInventoryItem item) => TryPickUpResult.DoesNotWant;

        public void AddXPGainer(GameObject xpGainer, float damageAmount)
        {
        }

        public void GainRealmPoints(long amount, bool modify)
        {
            if (!IsAutonomousWorldBot || amount <= 0)
                return;
            AutonomousRealmPoints += amount;
            int previousRealmLevel = RealmLevel;
            RealmLevel = Math.Max(RealmLevel, AutonomousBotRealmPointRewards.RealmLevelFor(AutonomousRealmPoints));
            AutonomousStuckWatchdog.MarkProgress(this, eAutonomousProgressKind.RealmPoints);
            MarkAutonomousStateDirty();
            BotMilestoneLife.OnRealmPoints(this); // Bots as people WP6: realm rank 5 (its talk points and the moment)
            if (RealmLevel != previousRealmLevel)
            {
                // Realm ranks earn the bot's guild merit points as a player's do (GuildEvents.RealmRankUp).
                AutonomousBotGuildEconomy.OnBotRealmLevelsGained(this, previousRealmLevel);
                AutonomousLivingServer.OnBotRealmLevel(this, previousRealmLevel);
            }
        }

        public override void GainRealmPoints(long amount) => GainRealmPoints(amount, true);

        public override void EnemyKilled(GameLiving enemy)
        {
            base.EnemyKilled(enemy);
            if (IsAutonomousWorldBot)
                AutonomousLivingServer.OnBotKilled(this, enemy);
            if (IsAutonomousWorldBot)
                BotSkill.OnFightWon(this, enemy); // Bots as people WP1: it and its groupmates near it learn
            DOL.GS.CraftingEconomy.BotPotions.AfterKill(this); // Crafting 2.0 WP7: a draught with an add still on (bot_upkeep)
        }

        public override void GainExperience(GainedExperienceEventArgs arguments, bool notify = true)
        {
            if (!IsAutonomousWorldBot || arguments == null || arguments.ExpTotal <= 0)
                return;
            if (Level >= MaxLevel)
            {
                // The cap discards the experience, but a kill that earned it is still proof the bot is working: a
                // level-50 caster or pet class farming from one spot was "rescued" to its capital every fifteen
                // minutes (241 of 268 such recoveries in run 2026-10-13 were level 50).
                AutonomousStuckWatchdog.MarkProgress(this, eAutonomousProgressKind.Experience);
                return;
            }

            long experienceGained = arguments.ExpTotal;
            long baseExperience = arguments.ExpBase;
            // A hired companion earns at its player's XP rate, other bots at the gamebot rate (owner 2026-11-10).
            bool hiredCompanion = IsHiredCompanion;
            if (arguments.AllowMultiply)
            {
                experienceGained -= baseExperience;

                if (ServerProperties.Properties.ENABLE_ZONE_BONUSES && CurrentZone != null)
                {
                    long zoneBonus = baseExperience * CurrentZone.BonusExperience / 100;
                    if (zoneBonus > 0)
                        experienceGained += ScaleExperienceFor(zoneBonus, false, Level, hiredCompanion);
                }

                baseExperience = ScaleExperienceFor(baseExperience,
                    CurrentRegion?.IsRvR == true || CurrentZone?.IsRvR == true, Level, hiredCompanion);

                long itemExperienceBonus = GetModified(eProperty.XpPoints);
                if (itemExperienceBonus != 0)
                    baseExperience += baseExperience * itemExperienceBonus / 100;

                experienceGained += baseExperience;
            }

            // A hired companion never passes its player's level (owner 2026-10-31): at the player's level it fills
            // its bar and waits; once the player levels, its next experience levels it too.
            byte? hiredCap = hiredCompanion ? HiredCompanionLevelCap() : null;
            experienceGained = HiredCompanions165.CappedExperience(experienceGained, Experience, Level, hiredCap);

            if (experienceGained <= 0)
                return;

            long previousExperience = Experience;
            byte previousLevel = Level;
            Experience += experienceGained;
            AutonomousStuckWatchdog.MarkProgress(this, eAutonomousProgressKind.Experience);
            bool leveled = false;
            while (Level < MaxLevel && Experience >= GamePlayer.GetExperienceAmountForLevel(Level) &&
                   !(hiredCap is byte cap && Level >= cap))
            {
                Level++;
                ApplyLevelStatGrowth(Level);
                leveled = true;
            }

            if (leveled)
            {
                // New specialization points remain unspent until the bot chooses
                // to reach and interact with its real class trainer.
                RefreshSpecDependantSkills(false);
                SetBotSpells();
                SortStyles();
                SortSpells();
                Health = MaxHealth;
                Mana = MaxMana;
                Endurance = MaxEndurance;
                AutonomousBotStatusPersistence.Queue(this, true);
                LogCompanionSkills($"level {Level}");
                if (hiredCompanion)
                {
                    // the group window and the roster show the new level
                    Group?.UpdateGroupWindow();
                    if (HiredCompanions165.OwnerOf(this) is { } hirer)
                        Hire165.Refresh(hirer);
                }
                else if (IsAutonomousWorldBot)
                {
                    AutonomousBotGuildEconomy.OnBotLevelsGained(this, previousLevel);
                    AutonomousBotGuildSocial.OnBotLevelUp(this);
                    AutonomousBotVoices.OnBotLevelUp(this);
                    AutonomousLivingServer.OnLevelUp(this, Level);
                }
            }
            else
            {
                MarkAutonomousStateDirty();
                // XP is a real progression event, so coalesce it into the next
                // status batch instead of waiting for a server-wide save.
                AutonomousBotStatusPersistence.Queue(this);
                // A hired companion's x.5 points come at its second stage, as a player's do (CompanionSpecPoints).
                if (hiredCompanion && !ReachedSecondStage(Level, previousExperience) && ReachedSecondStage(Level, Experience) &&
                    HiredCompanions165.OwnerOf(this) is { } hirer)
                    Hire165.Refresh(hirer);
            }
            if (!hiredCompanion)
                BotMilestoneLife.OnExperience(this); // Bots as people WP6: the ding to 50 (its talk points and the moment)
            if (previousExperience == 0 || previousLevel != Level)
            {
                log.Info($"AUTONOMOUS_XP_PROGRESS bot=\"{Name}\" id={DatabaseID} class=\"{ClassName}\" " +
                         $"amount={experienceGained} xp={previousExperience}->{Experience} level={previousLevel}->{Level}");
            }
        }

        /// <summary>A hired companion's level cap: its player's level (null when the player is not online).</summary>
        private byte? HiredCompanionLevelCap() => CompanionPlayer is { } player ? player.Level : null;

        private static long ScaleExperienceFor(long experience, bool isRvR, int level, bool hiredCompanion) =>
            hiredCompanion ? ScaleHiredCompanionExperience(experience, isRvR, level) : ScaleAutonomousExperience(experience, isRvR, level);

        /// <summary>A hired companion earns at the player rate (xp_rate, or xp_rate_by_level), as its player does.</summary>
        private static long ScaleHiredCompanionExperience(long experience, bool isRvR, int level)
        {
            double rate = LevelRangeXpRates.PlayerRate(level);
            if (isRvR)
                rate *= ServerProperties.Properties.RvR_XP_RATE;
            return (long)(experience * rate);
        }

        private static long ScaleAutonomousExperience(long experience, bool isRvR, int level)
        {
            // bot_xp_rate, or the bot_xp_rate_by_level rate for this level when that setting is used.
            double rate = LevelRangeXpRates.BotRate(level);
            if (isRvR)
                rate *= ServerProperties.Properties.RvR_XP_RATE;
            return (long)(experience * rate);
        }

        #endregion

        #region IGamePlayer — Stealth

        public override void Stealth(bool goStealth)
        {
            if (IsStealthed == goStealth)
                return;

            if (goStealth)
            {
                // A relic carrier cannot hide: the relic would fall at once (2026-10-30 livestream).
                if (GameRelic.IsPlayerCarryingRelic(this))
                    return;

                // Songs and pulsing spells are mutually exclusive with stealth.
                // Keep the same legality rule without using GamePlayer-only effects.
                if (effectListComponent.ContainsEffectForEffectType(eEffect.Pulse))
                    return;

                // #522: the player's "too close to hide" rule (StealthRules.EnemyTooClose; StealthSpecHandler for the
                // player): no bot hides inside a live enemy's circle. Before, only companions kept it.
                if (StealthRules.EnemyTooClose(this) != null)
                    return;

                if (IsOnHorse)
                    IsOnHorse = false;

                Flags |= eFlags.STEALTH;
                OnMaxSpeedChange();
                // Vanish from enemy players who cannot detect it (GamePlayer.CanDetect), as a stealthing player does.
                foreach (GamePlayer viewer in GetPlayersInRadius(WorldMgr.VISIBILITY_DISTANCE))
                    if (!viewer.CanDetect(this))
                        viewer.Out.SendObjectRemove(this);
                StartStealthUncoverAction();
                return;
            }

            Flags &= ~eFlags.STEALTH;
            OnMaxSpeedChange();
            StopStealthUncoverAction();
        }

        private ECSGameTimer _stealthUncoverTimer;

        /// <summary>
        /// #522: the monster and guard uncover timer for an autonomous gamebot stealther, the player's (GamePlayer
        /// UncoverStealthAction) through the one rule, StealthRules.Look: once a second, a hostile monster or guard close
        /// enough, in its front cone and winning its roll, uncovers the bot if it has line of sight on the navmesh (the
        /// player's client answers that check for him); one in a side cone turns to look. Companions keep today's rules
        /// (the owner's #521 decision names autonomous gamebots only).
        /// </summary>
        public void StartStealthUncoverAction()
        {
            if (!PvpRules.IsAutonomousGamebot(this) || _stealthUncoverTimer?.IsAlive == true)
                return;
            _stealthUncoverTimer = new ECSGameTimer(this, StealthUncoverTick, StealthRules.UncoverIntervalMilliseconds);
        }

        public void StopStealthUncoverAction()
        {
            _stealthUncoverTimer?.Stop();
            _stealthUncoverTimer = null;
        }

        private int StealthUncoverTick(ECSGameTimer timer)
        {
            if (!IsStealthed || !IsAlive || ObjectState != eObjectState.Active)
            {
                _stealthUncoverTimer = null;
                return 0;
            }
            StealthRules.UncoverRoll? uncovered = null;
            StealthRules.Look(this, roll =>
            {
                if (uncovered != null)
                    return;
                if (roll.Sense == UncoverSense.Hearing)
                    roll.By.TurnTo(this, StealthRules.HearingTurnMilliseconds);
                else if (UncoverSightOnNavmesh(roll.By))
                    uncovered = roll;
            });
            if (uncovered is not { } found)
                return StealthRules.UncoverIntervalMilliseconds;
            Stealth(false);
            RvrStealthRecord.Uncovered(this, found.By, found.Distance, UncoverSense.Sight);
            _stealthUncoverTimer = null;
            return 0;
        }

        /// <summary>The sight check the player's client makes for him, made on the navmesh for a bot (true with no mesh).</summary>
        private bool UncoverSightOnNavmesh(GameNPC by)
        {
            var nav = DOL.GS.PathfindingProvider.Instance;
            if (nav == null || !nav.IsAvailable || CurrentZone == null || !nav.HasNavmesh(CurrentZone))
                return true;
            return nav.HasLineOfSight(CurrentZone, new(by.X, by.Y, by.Z + 48), new(X, Y, Z + 48), nav.DefaultFilters);
        }

        #endregion

        #region IGamePlayer — Pet Control

        public void CommandNpcRelease()
        {
            ReleaseControlledPet(PetReleaseReason.Voluntary);
        }

        private enum PetReleaseReason { Voluntary, StableTravel, WorldRemoval }

        private void ReleaseControlledPet(PetReleaseReason reason)
        {
            // AI maintenance and companion-level upgrades cannot recycle a
            // living Necromancer servant. Required travel/world teardown is
            // explicit so this guard cannot leak pets or trap a shade.
            if (reason == PetReleaseReason.Voluntary && IsAlive &&
                ObjectState == eObjectState.Active &&
                ControlledBrain?.Body is NecromancerPet { IsAlive: true, ObjectState: eObjectState.Active })
                return;

            AutonomousPetSupport.CancelPendingCharm(this);
            bool necromancerPetDied = false;
            if (ControlledBrain != null)
            {
                var brain = ControlledBrain;
                GameNPC petBody = brain.Body;
                if (petBody is NecromancerPet servant)
                    servant.BotReleaseReason = !servant.IsAlive ? "death" : reason.ToString();
                necromancerPetDied = CharacterClass?.ID == (int)eCharacterClass.Necromancer &&
                                      petBody != null && !petBody.IsAlive;
                bool syntheticCharm = petBody?.TempProperties.GetProperty<bool>(AutonomousPetSupport.SyntheticCharmPetProperty) == true;
                // Let the summon effect perform its normal release cleanup.
                // This removes caster/pet effects and, for a Bonedancer
                // commander, releases every sub-pet before deleting the body.
                if (brain is ControlledMobBrain controlledBrain)
                    controlledBrain.OnRelease();
                else if (brain.Body != null && brain.Body != this)
                    brain.Body.Delete();
                if (ControlledBrain == brain)
                    RemoveControlledBrain(brain);
                if (syntheticCharm && petBody?.ObjectState is eObjectState.Active)
                    petBody.Delete();
            }
            if (CharacterClass?.ID == (int)eCharacterClass.Necromancer && IsShade)
            {
                Shade(false);

                // In classic 1.65 a Necromancer survives the loss of its
                // zombie only barely. Match that failure state for autonomous
                // and temporary bots, while leaving normal voluntary releases
                // and stable-travel despawns untouched.
                if (necromancerPetDied && IsAlive)
                    Health = 1;
            }
        }

        public override bool RemoveControlledBrain(IControlledBrain controlledBrain)
        {
            // PetECSGameEffect removes the controlled brain before deleting a
            // dead summon. GameBot does not use GamePlayer's character-class
            // callback, so without this hook a Necromancer bot could remain an
            // invulnerable orphaned shade until a later AI maintenance pulse.
            bool deadNecromancerPet = CharacterClass?.ID == (int)eCharacterClass.Necromancer &&
                                      controlledBrain?.Body is NecromancerPet { IsAlive: false };

            bool removed = base.RemoveControlledBrain(controlledBrain);
            if (!removed || CharacterClass?.ID != (int)eCharacterClass.Necromancer)
                return removed;

            if (IsShade)
                Shade(false);
            if (deadNecromancerPet && IsAlive)
                Health = 1;
            return true;
        }

        #endregion

        #region IGamePlayer — TargetInView

        protected bool m_targetInView = true;

        public override bool TargetInView
        {
            get
            {
                if (TargetObject != null && GetDistanceTo(TargetObject) <= TargetInViewAlwaysTrueMinRange)
                    return true;
                return m_targetInView;
            }
            set => m_targetInView = value;
        }

        public override int TargetInViewAlwaysTrueMinRange =>
            (TargetObject is GamePlayer targetPlayer && targetPlayer.IsMoving) ? 100 : 64;

        protected bool _groundTargetInView = true;

        public override bool GroundTargetInView
        {
            get => _groundTargetInView;
            set => _groundTargetInView = value;
        }

        #endregion

        #region Combat Styles and Spell Lists

        public new List<Style> StylesChain { get; protected set; }
        public new List<Style> StylesDefensive { get; protected set; }
        public new List<Style> StylesBack { get; protected set; }
        public new List<Style> StylesSide { get; protected set; }
        public new List<Style> StylesFront { get; protected set; }
        public new List<Style> StylesAnytime { get; protected set; }
        public List<Style> StylesTaunt { get; protected set; }
        public List<Style> StylesDetaunt { get; protected set; }
        public List<Style> StylesShield { get; protected set; }

        public List<Spell> InstantCrowdControlSpells { get; set; }
        public List<Spell> CrowdControlSpells { get; set; }
        public List<Spell> BoltSpells { get; set; }

        public bool CanUseSideStyles => StylesSide != null && StylesSide.Count > 0;
        public bool CanUseBackStyles => StylesBack != null && StylesBack.Count > 0;
        public bool CanUseFrontStyle => StylesFront != null && StylesFront.Count > 0;
        public bool CanUseAnytimeStyles => StylesAnytime != null && StylesAnytime.Count > 0;
        public bool CanUsePositionalStyles => CanUseSideStyles || CanUseBackStyles;
        public bool CanCastCrowdControlSpells => CrowdControlSpells != null && CrowdControlSpells.Count > 0;
        public bool CanCastBolts => BoltSpells != null && BoltSpells.Count > 0;

        // Heal spell properties
        public Spell HealBig { get; protected set; }
        public Spell HealEfficient { get; protected set; }
        public Spell HealGroup { get; protected set; }
        public Spell HealInstant { get; protected set; }
        public Spell HealInstantGroup { get; protected set; }
        public Spell HealOverTime { get; protected set; }
        public Spell HealOverTimeGroup { get; protected set; }
        public Spell HealOverTimeInstant { get; protected set; }
        public Spell HealOverTimeInstantGroup { get; protected set; }
        public Spell CureMezz { get; protected set; }
        public Spell CureDisease { get; protected set; }
        public Spell CureDiseaseGroup { get; protected set; }
        public Spell CurePoison { get; protected set; }
        public Spell CurePoisonGroup { get; protected set; }
        public Spell ResurrectionSpell { get; protected set; }

        protected const int HEALTH_REGEN_PERIOD = 6000;

        public static double HealAmount(Spell spell, GameLiving target)
        {
            return spell.Value >= 0
                ? spell.Value
                : target.MaxHealth * spell.Value * -0.01d;
        }

        public int PowerCost(Spell spell) => BotSpellPower.Cost(this, spell, null);

        private BotPlaybook _playbook;

        /// <summary>
        /// #568 C6: the bot's veteran playbook (BotPlaybook), built once from its known spells and rebuilt after SortSpells
        /// or a level change. Gamebots and companions alike.
        /// </summary>
        public BotPlaybook Playbook
        {
            get
            {
                BotPlaybook playbook = _playbook;
                if (playbook != null && playbook.Level == Level)
                    return playbook;
                playbook = BotPlaybook.Build(Spells, Level, (eCharacterClass)(CharacterClass?.ID ?? 0));
                _playbook = playbook;
                return playbook;
            }
        }

        private Dictionary<int, SpellLine> _powerSpellLines = new();

        // Combat still uses its existing dispatch line (no damage/spec behavior
        // changes). Power alone resolves the learned line for staff focus. The
        // cache is rebuilt with the spell list, not searched on every AI turn.
        internal SpellLine ResolvePowerSpellLine(Spell spell, SpellLine supplied)
        {
            if (supplied != null && supplied.KeyName != GlobalSpellsLines.Mob_Spells)
                return supplied;
            return spell != null && _powerSpellLines != null && _powerSpellLines.TryGetValue(spell.ID, out SpellLine learned)
                ? learned : supplied;
        }

        public override void SortSpells()
        {
            _playbook = null;   // #568 C6: rebuilt from the new spell list on its next read
            if (Spells.Count < 1)
                return;

            InstantHarmfulSpells?.Clear();
            HarmfulSpells?.Clear();
            InstantHealSpells?.Clear();
            HealSpells?.Clear();
            InstantCrowdControlSpells?.Clear();
            CrowdControlSpells?.Clear();
            BoltSpells?.Clear();
            InstantMiscSpells?.Clear();
            MiscSpells?.Clear();

            HealBig = null;
            HealEfficient = null;
            HealGroup = null;
            HealInstant = null;
            HealInstantGroup = null;
            HealOverTime = null;
            HealOverTimeGroup = null;
            HealOverTimeInstant = null;
            HealOverTimeInstantGroup = null;
            CureMezz = null;
            CureDisease = null;
            CureDiseaseGroup = null;
            CurePoison = null;
            CurePoisonGroup = null;
            ResurrectionSpell = null;

            foreach (Spell spell in Spells)
            {
                if (spell == null || AutonomousPetSupport.IsDisabledBotDamageShield(this, spell) ||
                    !AnimistSingleTargetPolicy.AllowsAutomatedSpell(this, spell))
                    continue;

                if (spell.SpellType == eSpellType.Resurrect)
                {
                    if (ResurrectionSpell == null || spell.Level > ResurrectionSpell.Level ||
                        spell.Level == ResurrectionSpell.Level && spell.ResurrectHealth > ResurrectionSpell.ResurrectHealth)
                        ResurrectionSpell = spell;
                }
                else if (spell.SpellType == eSpellType.Bolt)
                {
                    BoltSpells ??= new List<Spell>(1);
                    BoltSpells.Add(spell);
                }
                else if (spell.SpellType == eSpellType.Mesmerize ||
                        (spell.SpellType == eSpellType.SpeedDecrease && spell.Value >= 99))
                {
                    CrowdControlSpells ??= new List<Spell>(1);
                    CrowdControlSpells.Add(spell);
                }
                else if (spell.IsHarmful)
                {
                    if (spell.IsInstantCast)
                    {
                        InstantHarmfulSpells ??= new List<Spell>(1);
                        InstantHarmfulSpells.Add(spell);
                    }
                    else
                    {
                        HarmfulSpells ??= new List<Spell>(1);
                        HarmfulSpells.Add(spell);
                    }
                }
                else if (spell.IsHealing && !spell.IsPulsing && !spell.IsConcentration)
                {
                    if (spell.Target == eSpellTarget.PET)
                        continue;

                    double valueNew = HealAmount(spell, this);

                    if (spell.SpellType == eSpellType.CureMezz)
                        CureMezz = spell;
                    else if (spell.SpellType == eSpellType.CureDisease)
                    {
                        if ((spell.Target == eSpellTarget.GROUP || spell.Radius > 0)
                            && (CureDiseaseGroup == null || spell.Duration > CureDiseaseGroup.Duration))
                            CureDiseaseGroup = spell;
                        else if (spell.Target == eSpellTarget.REALM && (CureDisease == null || spell.Duration > CureDisease.Duration))
                            CureDisease = spell;
                    }
                    else if (spell.SpellType == eSpellType.CurePoison)
                    {
                        if ((spell.Target == eSpellTarget.GROUP || spell.Radius > 0)
                            && (CurePoisonGroup == null || spell.Duration > CurePoisonGroup.Duration))
                            CurePoisonGroup = spell;
                        else if (spell.Target == eSpellTarget.REALM && (CurePoison == null || spell.Duration > CurePoison.Duration))
                            CurePoison = spell;
                    }
                    else if (spell.IsInstantCast)
                    {
                        InstantHealSpells ??= new List<Spell>(1);
                        InstantHealSpells.Add(spell);

                        if (spell.SpellType == eSpellType.HealOverTime || spell.SpellType == eSpellType.HealthRegenBuff)
                        {
                            if (spell.Target == eSpellTarget.GROUP || spell.Radius > 0)
                            {
                                if (HealOverTimeInstantGroup == null)
                                    HealOverTimeInstantGroup = spell;
                                else
                                {
                                    double perSecondOld = HealOverTimeInstantGroup.SpellType == eSpellType.HealOverTime
                                        ? HealAmount(HealOverTimeInstantGroup, this) / HealOverTimeInstantGroup.Frequency
                                        : HealAmount(HealOverTimeInstantGroup, this) / HEALTH_REGEN_PERIOD / 2;
                                    double perSecondNew = spell.SpellType == eSpellType.HealOverTime
                                        ? valueNew / spell.Frequency
                                        : valueNew / HEALTH_REGEN_PERIOD / 2;

                                    if (perSecondNew > perSecondOld)
                                        HealOverTimeInstantGroup = spell;
                                }
                            }
                            else if (spell.Target == eSpellTarget.REALM)
                            {
                                if (HealOverTimeInstant == null)
                                    HealOverTimeInstant = spell;
                                else
                                {
                                    double perSecondOld = HealOverTimeInstant.SpellType == eSpellType.HealOverTime
                                        ? HealAmount(HealOverTimeInstant, this) / HealOverTimeInstant.Frequency
                                        : HealAmount(HealOverTimeInstant, this) / HEALTH_REGEN_PERIOD / 2;
                                    double perSecondNew = spell.SpellType == eSpellType.HealOverTime
                                        ? valueNew / spell.Frequency
                                        : valueNew / HEALTH_REGEN_PERIOD / 2;

                                    if (perSecondNew > perSecondOld)
                                        HealOverTimeInstant = spell;
                                }
                            }
                        }
                        else if (spell.SpellType == eSpellType.Heal)
                        {
                            if (spell.Target == eSpellTarget.GROUP || spell.Radius > 0)
                            {
                                if (HealInstantGroup == null || valueNew > HealAmount(HealInstantGroup, this))
                                    HealInstantGroup = spell;
                            }
                            else if (spell.Target == eSpellTarget.REALM)
                            {
                                if (HealInstant == null || valueNew > HealAmount(HealInstant, this))
                                    HealInstant = spell;
                            }
                        }
                    }
                    else
                    {
                        HealSpells ??= new List<Spell>(1);
                        HealSpells.Add(spell);

                        if (spell.SpellType == eSpellType.HealOverTime || spell.SpellType == eSpellType.HealthRegenBuff)
                        {
                            if (spell.Target == eSpellTarget.GROUP || spell.Radius > 0)
                            {
                                if (HealOverTimeGroup == null)
                                    HealOverTimeGroup = spell;
                                else
                                {
                                    double perSecondOld = HealOverTimeGroup.SpellType == eSpellType.HealOverTime
                                        ? HealAmount(HealOverTimeGroup, this) / HealOverTimeGroup.Frequency
                                        : HealAmount(HealOverTimeGroup, this) / HEALTH_REGEN_PERIOD / 2;
                                    double perSecondNew = spell.SpellType == eSpellType.HealOverTime
                                        ? valueNew / spell.Frequency
                                        : valueNew / HEALTH_REGEN_PERIOD / 2;

                                    if (perSecondNew > perSecondOld)
                                        HealOverTimeGroup = spell;
                                }
                            }
                            else if (spell.Target == eSpellTarget.REALM)
                            {
                                if (HealOverTime == null)
                                    HealOverTime = spell;
                                else
                                {
                                    double perSecondOld = HealOverTime.SpellType == eSpellType.HealOverTime
                                        ? HealAmount(HealOverTime, this) / HealOverTime.Frequency
                                        : HealAmount(HealOverTime, this) / HEALTH_REGEN_PERIOD / 2;
                                    double perSecondNew = spell.SpellType == eSpellType.HealOverTime
                                        ? valueNew / spell.Frequency
                                        : valueNew / HEALTH_REGEN_PERIOD / 2;

                                    if (perSecondNew > perSecondOld)
                                        HealOverTime = spell;
                                }
                            }
                        }
                        else if (spell.SpellType == eSpellType.Heal)
                        {
                            if (spell.Target == eSpellTarget.GROUP || spell.Radius > 0)
                            {
                                if (HealGroup == null || valueNew / spell.CastTime > HealAmount(HealGroup, this) / HealGroup.CastTime)
                                    HealGroup = spell;
                            }
                            else if (spell.Target == eSpellTarget.REALM)
                            {
                                if (HealEfficient == null)
                                    HealEfficient = spell;
                                else
                                {
                                    double perSecondNew = valueNew / spell.CastTime;
                                    double perSecondEff = HealAmount(HealEfficient, this) / HealEfficient.CastTime;
                                    double perSecondBig = HealBig == null ? 0.0 : HealAmount(HealBig, this) / HealBig.CastTime;

                                    double effNew = valueNew / PowerCost(spell) + 0.25;
                                    double effOld = HealAmount(HealEfficient, this) / PowerCost(HealEfficient);

                                    if (effNew > effOld)
                                    {
                                        if (perSecondEff > perSecondNew && perSecondEff > perSecondBig)
                                            HealBig = HealEfficient;

                                        HealEfficient = spell;
                                    }
                                    else if (perSecondNew > perSecondEff && perSecondNew > perSecondBig)
                                        HealBig = spell;
                                }
                            }
                        }
                    }
                }
                else
                {
                    if (spell.IsInstantCast && spell.SpellType != eSpellType.SpeedEnhancement)
                    {
                        InstantMiscSpells ??= new List<Spell>(1);
                        InstantMiscSpells.Add(spell);
                    }
                    else
                    {
                        MiscSpells ??= new List<Spell>(1);
                        MiscSpells.Add(spell);
                    }
                }
            }
        }

        public override void SortStyles()
        {
            StylesChain?.Clear();
            StylesDefensive?.Clear();
            StylesBack?.Clear();
            StylesSide?.Clear();
            StylesFront?.Clear();
            StylesAnytime?.Clear();
            StylesTaunt?.Clear();
            StylesDetaunt?.Clear();
            StylesShield?.Clear();

            if (Styles == null)
                return;

            foreach (Style s in Styles)
            {
                if (s == null)
                    continue;

                if (s.WeaponTypeRequirement != (int)eObjectType.Shield ||
                    s.WeaponTypeRequirement == (int)eObjectType.Shield && s.OpeningRequirementType == Style.eOpening.Defensive)
                {
                    switch (s.OpeningRequirementType)
                    {
                        case Style.eOpening.Defensive:
                            StylesDefensive ??= new List<Style>(1);
                            StylesDefensive.Add(s);
                            break;

                        case Style.eOpening.Positional:
                            switch ((Style.eOpeningPosition)s.OpeningRequirementValue)
                            {
                                case Style.eOpeningPosition.Back:
                                    StylesBack ??= new List<Style>(1);
                                    StylesBack.Add(s);
                                    break;

                                case Style.eOpeningPosition.Side:
                                    StylesSide ??= new List<Style>(1);
                                    StylesSide.Add(s);
                                    break;

                                case Style.eOpeningPosition.Front:
                                    StylesFront ??= new List<Style>(1);
                                    StylesFront.Add(s);
                                    break;
                            }
                            break;

                        default:
                            if (s.OpeningRequirementValue > 0)
                            {
                                StylesChain ??= new List<Style>(1);
                                StylesChain.Add(s);
                            }
                            else
                            {
                                bool added = false;

                                if (s.Procs.Count > 0)
                                {
                                    foreach (StyleProcInfo proc in s.Procs)
                                    {
                                        if (proc.Spell.SpellType == eSpellType.StyleTaunt)
                                        {
                                            if (proc.Spell.ID == 20000)
                                            {
                                                StylesTaunt ??= new List<Style>(1);
                                                StylesTaunt.Add(s);
                                                added = true;
                                            }
                                            else if (proc.Spell.ID == 20001)
                                            {
                                                StylesDetaunt ??= new List<Style>(1);
                                                StylesDetaunt.Add(s);
                                                added = true;
                                            }
                                        }
                                    }
                                }

                                if (!added)
                                {
                                    StylesAnytime ??= new List<Style>(1);
                                    StylesAnytime.Add(s);
                                }
                            }
                            break;
                    }
                }
                else
                {
                    StylesShield ??= new List<Style>(1);
                    StylesShield.Add(s);
                }
            }
        }

        #endregion

        #region Constructor

        public GameBot(
            GamePlayer owner,
            byte classId,
            string name = null,
            byte raceId = 0,
            byte genderId = 0,
            bool temporaryGroupHelper = false,
            byte equipmentLevel = 0,
            byte botLevel = 0,
            BotSpec chosenSpec = null)
        {
            Owner = owner ?? throw new ArgumentNullException(nameof(owner));
            IsTemporaryGroupHelper = temporaryGroupHelper;
            ClassId = classId;
            RaceId = raceId;
            GenderId = genderId;
            ClassName = BotManager.GetClassNameById(classId);
            InternalID = Guid.NewGuid().ToString();
            _dummyClient = new BotDummyClient();
            _dummyLib = new BotDummyPacketLib();

            // Set class identity first — needed for stat init and specs
            if (!SetCharacterClass(ClassId))
                throw new InvalidOperationException($"Failed to set character class for class ID {ClassId}. Ensure the class ID is valid.");
            SetRaceAndRealm(owner);
            Level = BotAttributeProgression.InitialLevel(owner.Level, botLevel, IsTemporaryGroupHelper);
            _creationModel = Model;

            Name = string.IsNullOrEmpty(name) ? $"{owner.Name}'s {ClassName} Bot" : name;
            MaxSpeedBase = PLAYER_BASE_SPEED;

            // Stat and spec initialization — order matters
            InitializeBotStats();
            eCharacterClass temporaryClass = (eCharacterClass)ClassId;
            // /spawn <class> <spec> picks one of the class's existing builds (TemporaryGroupSpecCatalog); otherwise random.
            BotSpec = chosenSpec ?? (temporaryClass == eCharacterClass.Bonedancer
                ? new BonedancerBotSpec(BotSpec.ChooseRandomSpecialization(temporaryClass), 0, false)
                : temporaryClass == eCharacterClass.Sluaghbinder
                    ? new SluaghbinderBotSpec(BotSpec.ChooseRandomSpecialization(temporaryClass), 0, false)
                : BotSpec.GetSpec(temporaryClass, BotSpec.ChooseRandomSpecialization(temporaryClass)));
            LoadClassSpecializations(false);
            SpendSpecPoints(Level, 0);
            RefreshSpecDependantSkills(false);
            SetBotSpells();
            SortStyles();
            SortSpells();
            EquipBot(equipmentLevel);

            Health = MaxHealth;
            Endurance = MaxEndurance;
            Mana = MaxMana;

            RespawnInterval = -1;

            // Set up BotBrain — replaces old BotAI system
            var brain = temporaryClass == eCharacterClass.Sluaghbinder
                ? new DOL.AI.Brain.SluaghbinderBotBrain()
                : new DOL.AI.Brain.BotBrain();
            brain.IsHealer = IsHealerClass();
            SetOwnBrain(brain);

            // ControlledBrain is reserved for an actual class pet. The bot's own
            // combat AI belongs in Brain via SetOwnBrain.
            InitControlledBrainArray(1);

            GameEventMgr.AddHandler(Owner, GamePlayerEvent.Quit, new DOLEventHandler(OnOwnerQuit));
            if (IsTemporaryGroupHelper)
                GameEventMgr.AddHandler(Owner, GamePlayerEvent.RegionChanged, new DOLEventHandler(OnOwnerRegionChanged));
        }

        /// <summary>
        /// Loads an ownerless autonomous character from its persistent record.
        /// No generated equipment and no field auto-training are allowed here.
        /// </summary>
        public GameBot(OfflineWorldBotRecord record) : this(record, null)
        {
        }

        /// <summary>An Epic Special Forces member from an in-memory record (never saved; see <see cref="IsEpicSpecialForce"/>).</summary>
        public static GameBot CreateEpicSpecialForce(OfflineWorldBotRecord record) => new(record, null, true);

        /// <summary>A No Lifer's buffbot from an in-memory record (never saved; see <see cref="IsNoLiferBuffBot"/>).</summary>
        public static GameBot CreateNoLiferBuffBot(OfflineWorldBotRecord record, long mainId) =>
            mainId > 0 ? new(record, null, false, mainId) : throw new ArgumentOutOfRangeException(nameof(mainId));

        public GameBot(OfflineWorldBotRecord record, System.Collections.IList preparedInventory) : this(record, preparedInventory, false)
        {
        }

        private GameBot(OfflineWorldBotRecord record, System.Collections.IList preparedInventory, bool epicSpecialForce,
            long buffBotMainId = 0)
        {
            PersistentRecord = record ?? throw new ArgumentNullException(nameof(record));
            IsEpicSpecialForce = epicSpecialForce;
            BuffBotMainId = Math.Max(0, buffBotMainId);
            AutonomousBotGoalPolicy.ReconcileSavedAssignment(record);
            // Autonomous Group instances are process-local and are rebuilt
            // after every server start. Never expose a former process's leader,
            // roster, or absolute meetup deadline while this actor is loading.
            if (record.ItineraryJson?.StartsWith(AutonomousBotGroupCoordinator.MetadataPrefix,
                    StringComparison.Ordinal) == true)
            {
                record.ItineraryJson = string.Empty;
                record.Dirty = true;
            }
            IsAutonomousWorldBot = true;
            DatabaseID = record.BotId;
            InternalID = AutonomousBotEconomy.GetOwnerId(record.BotId);
            ClassId = (byte)record.ClassId;
            RaceId = (byte)record.RaceId;
            GenderId = (byte)record.Gender;
            ClassName = string.IsNullOrWhiteSpace(record.ClassName) ? BotManager.GetClassNameById(ClassId) : record.ClassName;
            _dummyClient = new BotDummyClient();
            _dummyLib = new BotDummyPacketLib();
            if (!SetCharacterClass(ClassId))
                throw new InvalidOperationException($"Failed to load autonomous class ID {ClassId} for {record.Name}.");
            SetRaceAndRealm(null);
            Level = (byte)Math.Clamp(record.Level, 1, MaxLevel);
            _creationModel = Model;
            if ((eRealm)record.Realm != eRealm.None)
                Realm = (eRealm)record.Realm;
            Name = record.Name;
            MaxSpeedBase = PLAYER_BASE_SPEED;

            InitializeBotStats();
            eCharacterClass persistentClass = (eCharacterClass)ClassId;
            BotSpec = IsNoLiferBuffBot ? NoLiferBuffBotSpec.For(persistentClass) : persistentClass switch
            {
                eCharacterClass.Bonedancer => new BonedancerBotSpec(
                    BotSpec.ChoosePersistentSpecialization(persistentClass, record.BotId), record.BotId, true),
                eCharacterClass.Savage => new SavageBotSpec(
                    BotSpec.ChoosePersistentSpecialization(persistentClass, record.BotId),
                    SavageBotSpec.WeaponFromPersistedSpecs(record.SerializedSpecs)),
                eCharacterClass.Sluaghbinder => new SluaghbinderBotSpec(
                    BotSpec.ChoosePersistentSpecialization(persistentClass, record.BotId), record.BotId, true),
                _ => BotSpec.GetSpec(persistentClass,
                    BotSpec.ChoosePersistentSpecialization(persistentClass, record.BotId)),
            };
            LoadClassSpecializations(false);
            if (IsInMemoryOnly)
                SpendSpecPoints(Level, 0); // fully trained on arrival, like a level-50 /spawn companion (a buffbot: its buff spec)
            else
                LoadPersistedSpecs(record.SerializedSpecs);
            bool buildLocked = IsInMemoryOnly || BotLifetimeBuild.Restore(BotSpec, record.SerializedBuildPlan);
            bool hiredCompanion = HiredCompanions165.IsHired(record.BotId);
            // Era builds 1.2 (WP3, docs/ERA-BUILDS-1.2.md "The build draw"): a gamebot with no saved plan takes an era build
            // for life (or the nearest one to the lines it already trained). A saved plan was restored above and is never
            // re-drawn (D1). Epic Special Forces and buffbots (in memory only), hired companions and the custom class keep
            // today's plans; /spawn and /bot companions use the other constructor.
            EraBuildDecision eraBuild = null;
            if (!buildLocked && EraLifetimeBuild.Applies(ServerProperties.Properties.BOT_ERA_BUILDS, ClassId, IsInMemoryOnly,
                    hiredCompanion, DAoCLivingServer.Shared.EraBuildCatalog.Default))
            {
                eraBuild = EraLifetimeBuild.Choose(DAoCLivingServer.Shared.EraBuildCatalog.Default, persistentClass, record.BotId,
                    TrainedSpecLevels(), ServerProperties.Properties.BOT_ERA_OFFMETA_PERCENT);
                if (eraBuild.Plan != null)
                    BotSpec = eraBuild.Plan;
            }
            if (!buildLocked && eraBuild?.Plan == null)
                BotLifetimeBuild.AlignWithInvestedWeapons(BotSpec, GetBaseSpecLevel);
            // A saved build can predate the Savage's trained weapon line. Its
            // old weapon field must not override the specialization restored
            // from the character record.
            bool savagePlanCorrected = !IsInMemoryOnly && persistentClass == eCharacterClass.Savage &&
                SavageBotSpec.AlignWithPersistedSpecs(BotSpec, record.SerializedSpecs);
            if (!buildLocked || savagePlanCorrected)
            {
                record.SerializedBuildPlan = BotLifetimeBuild.Encode(BotSpec);
                MarkAutonomousStateDirty();
            }
            if (eraBuild != null)
                log.Info(EraLifetimeBuild.AssignedLine(Name, DatabaseID, ClassName, Level, BotSpec, eraBuild));
            // An Epic Special Force or buffbot keeps what its training above left unspent (a player's total, all of it
            // counted); a saved bot takes its saved count.
            if (!IsInMemoryOnly)
                m_leftOverSpecPoints = Math.Max(0, record.UnspentSpecPoints);
            _lastAutonomousTrainedLevel = IsInMemoryOnly ? Level : ParseLastTrainedLevel(record.SerializedAbilities, Level, record.SerializedSpecs);
            // A bot the launcher made at level 50 ("+ Lv.50") has its lifetime build but has never stood at a trainer: it
            // arrives trained in that build, as a player who leveled there would be. PC 2026-11-11 (build e90bf21): 900 such
            // bots a realm loaded with every line at 1, 792 of 2,700 had found a trainer after 80 minutes, and raids staged
            // 20 minutes in were fought by level 50s with level-1 skills (9 of the 72 sent to Golestandt had trained).
            // Living Server 1.2: so does one the launcher made for the Thidranki battleground ("+ Lv.20", levels 20-24), which would
            // otherwise fight its battleground with every line at 1 (it trains only at a break between PvE tasks).
            // A hired companion is trained below instead, once, by its own rule (HiredCompanions165.PlanArrival).
            bool arrivedTrained = !IsInMemoryOnly && !hiredCompanion &&
                ArrivesTrained(Level, _lastAutonomousTrainedLevel, string.IsNullOrWhiteSpace(record.LastSavedUtc));
            if (arrivedTrained)
            {
                byte arrivalTrainedFrom = _lastAutonomousTrainedLevel;
                SpendSpecPoints(Level, _lastAutonomousTrainedLevel);
                _lastAutonomousTrainedLevel = Level;
                MarkAutonomousStateDirty();
                if (EraLifetimeBuild.FirstTrainedAtFifty(BotSpec, arrivalTrainedFrom, Level))
                    log.Info(EraLifetimeBuild.TrainedLine(Name, DatabaseID, Level, BotSpec, GetBaseSpecLevel, m_leftOverSpecPoints));
                if (AutonomousDiagnosticsProperties.Training)
                    log.Info($"AUTONOMOUS_TRAINING bot=\"{Name}\" id={DatabaseID} class=\"{ClassName}\" level={Level} " +
                             $"trainer=\"(arrived trained)\" trainedFrom=1 trainedThrough={Level} " +
                             $"specializations=\"{string.Join(",", GetSpecList().Where(spec => spec.Trainable && spec.Level > 1).Select(spec => $"{spec.KeyName}:1->{spec.Level}"))}\" unspent={m_leftOverSpecPoints}");
            }
            else if (!IsInMemoryOnly && !hiredCompanion && _lastAutonomousTrainedLevel > 1)
                ApplySavedSpecPointRefund();
            // Living Server 1.2.1 (owner 2026-11-21): a hired companion that joins at its player's level, or one hired on 1.2
            // that is raised to it, arrives trained in its build (and in plain merchant gear above level 5, below).
            HiredCompanions165.Arrival hiredArrival = hiredCompanion && !IsInMemoryOnly ? HiredCompanions165.TakeArrival(record.BotId) : null;
            if (hiredArrival != null)
                TrainHiredCompanionArrival(hiredArrival);
            RefreshSpecDependantSkills(false);
            SetBotSpells();
            SortStyles();
            SortSpells();

            if (IsEpicSpecialForce)
                EquipBot(50); // the endgame kit, in an inventory that is never saved
            else if (IsNoLiferBuffBot)
            {
                // The launcher's level-50 rule (AutonomousStarterGear): it arrives trained, so it wears the best plain merchant
                // item of its realm for its level in each slot, free, in an inventory that is never saved. No coin, no loot.
                Inventory = new BotInventory(InternalID);
                Inventory.LoadInventory(InternalID, new System.Collections.ArrayList());
                AutonomousStarterGear.Result gear = PlanAndGiveStarterGear();
                EnsureAutonomousStarterWeapon();
                RefreshItemBonuses();
                log.Info($"BUFFBOT_GEAR bot=\"{record.Name}\" main={buffBotMainId} class=\"{ClassName}\" level={Level} " +
                         $"pieces={gear.Pieces.Count} items=\"{AutonomousStarterGear.Describe(gear.Pieces)}\"" +
                         (gear.Given ? string.Empty : $" reason=\"{gear.Reason}\""));
            }
            else
            {
                Inventory = new BotInventory(InternalID);
                if (preparedInventory == null)
                    Inventory.LoadFromDatabase(InternalID);
                else
                    Inventory.LoadInventory(InternalID, preparedInventory);
                // Before the starter weapon, so a bot that gets starter gear holds a merchant weapon in an empty weapon slot
                // and the server makes its own starter weapon only where none fitted.
                bool starterGearGiven = TryGiveStarterGear(record, arrivedTrained, hiredArrival);
                bool starterWeaponAdded = EnsureAutonomousStarterWeapon();
                starterWeaponAdded |= BotRangedCombat.EnsureStarter(this);
                starterWeaponAdded |= BotStarterInstruments.Ensure(this);
                RefreshItemBonuses();
                if (starterWeaponAdded || !buildLocked || arrivedTrained || starterGearGiven || hiredArrival != null)
                    AutonomousBotStatusPersistence.Queue(this, starterWeaponAdded || arrivedTrained || starterGearGiven || hiredArrival != null);
            }
            Experience = Math.Max(0, record.Experience);
            AutonomousRealmPoints = Math.Max(0, record.RealmPoints);
            RealmLevel = AutonomousBotRealmPointRewards.RealmLevelFor(AutonomousRealmPoints);
            AutonomousBotRealmAbilities.Restore(this, ParseTrainedRealmPoints(record.SerializedAbilities));
            CurrentRegionID = (ushort)Math.Clamp(record.RegionId, 0, ushort.MaxValue);
            X = record.X;
            Y = record.Y;
            Z = record.Z;
            Health = IsInMemoryOnly ? MaxHealth : Math.Clamp(record.Health, 1, MaxHealth);
            Mana = IsInMemoryOnly ? MaxMana : Math.Clamp(record.Mana, 0, MaxMana);
            Endurance = IsInMemoryOnly ? MaxEndurance : Math.Clamp(record.Endurance, 0, MaxEndurance);
            RespawnInterval = -1;

            var brain = persistentClass == eCharacterClass.Sluaghbinder
                ? new DOL.AI.Brain.SluaghbinderBotBrain { IsHealer = IsHealerClass() }
                : new DOL.AI.Brain.BotBrain { IsHealer = IsHealerClass() };
            SetOwnBrain(brain);
            InitControlledBrainArray(1);
        }

        /// <summary>
        /// Living Server 1.2 (owner 2026-11-15, "Arrive with starter gear"): a gamebot that arrives already trained above
        /// level 1, or a "+ Lv.20" bot saved before this rule that still wears no armour, gets the best plain merchant item
        /// of its realm for its level in each empty armour slot and its build's empty weapon and shield slots, free, once
        /// (AutonomousStarterGear.Qualifies). Level-1 starters, gamebots that wear armour, Epic Special Forces (never
        /// saved, the constructor's other branch), /spawn companions and players never get here.
        /// <para>A hired companion (a different bot type) gets the same plain merchant gear only on the arrival its summon
        /// planned with gear (1.2.1, owner 2026-11-21: hired or raised above level 5), in its empty slots only.</para>
        /// </summary>
        private bool TryGiveStarterGear(OfflineWorldBotRecord record, bool arrivedTrained, HiredCompanions165.Arrival hiredArrival = null)
        {
            _starterGearGiven = AutonomousStarterGear.HasMarker(record.SerializedAbilities);
            if (HiredCompanions165.IsHired(record.BotId))
                return hiredArrival is { BasicGear: true } && !IsEpicSpecialForce && !IsTemporaryGroupHelper && Inventory != null &&
                       GiveHiredCompanionGear(hiredArrival);
            if (IsEpicSpecialForce || IsTemporaryGroupHelper || Inventory == null ||
                !AutonomousStarterGear.Qualifies(Level, arrivedTrained, _starterGearGiven,
                    AutonomousStarterGear.ArmorPiecesWorn(Inventory.GetItem), Realm, record.BindRegionId))
                return false;

            AutonomousStarterGear.Result result;
            try
            {
                result = PlanAndGiveStarterGearCore();
            }
            catch (Exception exception)
            {
                log.Error($"AUTONOMOUS_STARTER_GEAR bot=\"{Name}\" id={DatabaseID} failed; it keeps what it has and tries again next login", exception);
                return false;
            }

            string path = arrivedTrained ? "arrived-trained" : "earlier-launcher-bot";
            log.Info($"AUTONOMOUS_STARTER_GEAR bot=\"{Name}\" id={DatabaseID} level={Level} class=\"{ClassName}\" " +
                     $"realm={GlobalConstants.RealmToName(Realm)} path={path} pieces={result.Pieces.Count} " +
                     $"items=\"{AutonomousStarterGear.Describe(result.Pieces)}\"" +
                     (result.Given ? " coin=unchanged" : $" reason=\"{result.Reason}\" retry=next-login"));
            if (!result.Given)
                return false;
            _starterGearGiven = true;
            MarkAutonomousStateDirty();
            AutonomousBotEconomy.MarkInventoryChanged(this);
            return true;
        }

        /// <summary>A hired companion's basic gear: the gamebots' starter gear (the best plain item its realm's merchants sell at
        /// its level) in each empty slot of its build. Nothing it wears or carries is moved or replaced, except its training
        /// weapon (HiredCompanions165.IsTrainingWeapon; owner 2026-11-21: it counts as an empty slot): put in its bag while
        /// the gear is chosen, deleted once a merchant weapon took its place, put back where it was otherwise. Its coin is
        /// untouched. No merchant of its realm found (or an error): nothing changes and the gear is tried at its next summon.</summary>
        private bool GiveHiredCompanionGear(HiredCompanions165.Arrival arrival)
        {
            if (AutonomousStarterGear.RealmCatalog(Realm).Length == 0)
            {
                arrival.GearRetry = true;
                arrival.Gear = "none (no merchant of its realm found)";
                return false;
            }
            // the training weapons out of their slots, so the plan sees those slots empty
            var training = new List<(eInventorySlot Slot, DbInventoryItem Item)>();
            foreach (eInventorySlot slot in HiredCompanions165.TrainingWeaponSlots)
            {
                if (Inventory.GetItem(slot) is not { } item || !HiredCompanions165.IsTrainingWeapon(item))
                    continue;
                eInventorySlot bag = Inventory.FindFirstEmptySlot(HiredCompanions165.FirstBagSlot, HiredCompanions165.LastBagSlot);
                if (bag != eInventorySlot.Invalid && Inventory.MoveItem(slot, bag, Math.Max(1, item.Count)))
                    training.Add((slot, item));
                else if (Inventory.RemoveItem(item))
                    training.Add((slot, null));   // a full bag: deleted now; the server makes a new one if no merchant weapon fits
            }
            AutonomousStarterGear.Result result = null;
            try
            {
                result = PlanAndGiveStarterGearCore();
            }
            catch (Exception exception)
            {
                log.Error($"HIRED_COMPANION_GEAR bot=\"{Name}\" id={DatabaseID} failed; it keeps what it has", exception);
                arrival.GearRetry = true;
                arrival.Gear = "none (error)";
            }
            var replaced = new List<string>();
            foreach ((eInventorySlot slot, DbInventoryItem item) in training)
            {
                bool leftHand = slot == eInventorySlot.LeftHandWeapon;
                bool weaponGiven = result?.Pieces.Any(piece => leftHand
                    ? piece.Slot == eInventorySlot.LeftHandWeapon
                    : piece.Slot is eInventorySlot.RightHandWeapon or eInventorySlot.TwoHandWeapon) == true;
                if (weaponGiven || item == null)
                {
                    if (item != null)
                        Inventory.RemoveItem(item);
                    replaced.Add(AutonomousStarterGear.SlotName(slot) + (weaponGiven ? string.Empty : "(deleted, bag full)"));
                }
                else if (Inventory.GetItem(slot) == null)
                    Inventory.MoveItem((eInventorySlot)item.SlotPosition, slot, Math.Max(1, item.Count));   // no merchant weapon: back in its hand
            }
            if (replaced.Count > 0)
                arrival.TrainingWeapon = string.Join(",", replaced);
            if (result != null)
                arrival.Gear = result.Given ? AutonomousStarterGear.Describe(result.Pieces) : $"none ({result.Reason})";
            if (result?.Given != true && replaced.Count == 0)
                return false;
            MarkAutonomousStateDirty();
            AutonomousBotEconomy.MarkInventoryChanged(this);
            return true;
        }

        /// <summary>The best plain merchant item of its realm for its level in each empty slot of its build (AutonomousStarterGear).</summary>
        private AutonomousStarterGear.Result PlanAndGiveStarterGearCore()
        {
            bool savage = CharacterClass?.ID == (int)eCharacterClass.Savage;
            eInventorySlot primarySlot = PrimaryWeaponUsesTwoHands ? eInventorySlot.TwoHandWeapon : eInventorySlot.RightHandWeapon;
            bool shieldBuild = !RequiresMeleeOffhand && !SluaghbinderUsesScythe && BestShieldLevel > 0;
            var wants = AutonomousStarterGear.PlanSlots(slot => Inventory.GetItem(slot) != null, primarySlot,
                PreferredPrimaryWeaponType, RequiresMeleeOffhand ? PreferredOffhandWeaponType : 0, shieldBuild, savage);
            return AutonomousStarterGear.Give(this, wants);
        }

        /// <summary>A buffbot's gear on arrival; a failure leaves it in what it has (it never fights).</summary>
        private AutonomousStarterGear.Result PlanAndGiveStarterGear()
        {
            try
            {
                return PlanAndGiveStarterGearCore();
            }
            catch (Exception exception)
            {
                log.Error($"BUFFBOT_GEAR bot=\"{Name}\" failed; it arrives in what it has", exception);
                return new AutonomousStarterGear.Result(false, [], "error");
            }
        }

        private bool IsHealerClass()
        {
            return CharacterClass != null && BotPartyRoles.IsHealingClass((eCharacterClass)CharacterClass.ID);
        }

        #endregion

        #region Bot Management

        private void OnOwnerQuit(DOLEvent e, object sender, EventArgs arguments)
        {
            if (IsTemporaryGroupHelper)
                Delete();
            else
                BotManager.RemoveBot(this);
        }

        private void OnOwnerRegionChanged(DOLEvent e, object sender, EventArgs arguments)
        {
            if (IsTemporaryGroupHelper && sender is GamePlayer player)
                TemporaryGroupStableTravel.RelocateHelpersAfterPlayerTransfer(player);
        }

        public bool EnterPlayerLedGroup(GamePlayer leader)
        {
            if (leader == null || leader.Realm != Realm)
                return false;
            // Play with friends (1.2): a companion follows its owner and nobody else (BotHumanBinding). Another human's
            // /pull, pilot or leadership change never takes it.
            if (Owner != null && leader != Owner)
                return false;

            PlayerGroupLeader = leader;
            // The player now has this gamebot's time: no raid, siege, RvR event or rally takes it (docs/DUTY-LEASE.md).
            BotDutyLeases.Claim(this, eDutyLeaseKind.PlayerGroup, leader.Name, DutyLeaseReason.PlayerGroup, leader.Name);
            if (Brain is BotBrain brain)
            {
                brain.RebindAssistedPlayer();
                brain.FSM.SetCurrentState(eFSMStateType.FOLLOW);
            }
            return true;
        }

        public void LeavePlayerLedGroup()
        {
            if (IsTemporaryGroupHelper)
                CompleteStableMasterRoute();
            PlayerGroupLeader = null;
            BotDutyLeases.Release(this, eDutyLeaseKind.PlayerGroup);
            if (Brain is BotBrain brain)
                brain.RebindAssistedPlayer();
            HiredCompanions165.OnLeftPlayerGroup(this);   // a hired companion goes back to wait, it doesn't roam
        }

        private bool _recoveryRestLocked;
        private long _autonomousRecoveryStartTick;

        internal long AutonomousRecoveryStartTick => _autonomousRecoveryStartTick;

        /// <summary>
        /// Temporary companions expose their general recovery lock to the
        /// player-led coordination layer. The underlying recovery state is also
        /// used by autonomous playerbots and never changes actor presentation.
        /// </summary>
        public bool IsTemporaryCompanionRestLocked =>
            IsTemporaryGroupHelper && _recoveryRestLocked;

        public bool BeginRecoveryRest()
        {
            if (!IsAlive || IsOnStableMasterRoute)
                return false;

            if (_recoveryRestLocked)
            {
                // stood up for a song while resting: sits back down once it's done
                if (!_seatedForClients && !IsCasting && !IsMoving && !InCombat)
                    SetSeatedForClients(true);
                return true;
            }

            // Harmless mobile songs are allowed to continue during logical
            // recovery. Ordinary casts are cancelled so recovery still owns
            // movement and combat activity.
            if ((IsCasting || castingComponent?.HasPendingSkillRequests == true) &&
                !BotSongTwistPolicy.HasMobileSongCast(this))
                StopCurrentSpellcast();
            if (IsAttacking)
                StopAttack();
            StopFollowing();
            StopMovingOnPath();
            StopMoving();
            _recoveryRestLocked = true;
            _seatedByHabit = false;         // the rest takes over a seat taken by habit (WP5)
            SetSeatedForClients(true);      // sits down for the rest, visibly (0xF0)
            UpdateHealthManaEndu();
            StartEnhancedRecoveryTimers();
            return true;
        }

        public bool BeginTemporaryCompanionRest()
        {
            return IsTemporaryGroupHelper && BeginRecoveryRest();
        }

        public void WakeTemporaryCompanionRest()
        {
            // hired companions too (audit 2026-11-03): /pull or an attack order left them seated and frozen
            if (IsTemporaryGroupHelper || IsHiredCompanion)
                WakeRecoveryRest();
        }

        /// <summary>Right-clicking your own hired companion opens the hire window on its roster row.</summary>
        public override bool Interact(GamePlayer player)
        {
            if (player != null && IsHiredCompanion &&
                HiredCompanions165.RosterOf(player).FirstOrDefault(h => h.BotId == DatabaseID) is { } hired)
            {
                if (!IsWithinRadius(player, WorldMgr.VISIBILITY_DISTANCE))
                    return false;
                Hire165.OpenForCompanion(player, hired.Row);
                HireDialog165.ShowCompanion(player, this);   // the original client's own dialog, with links
                return true;
            }
            return base.Interact(player);
        }

        /// <summary>A link clicked in one's own hired companion's dialog (HireDialog165).</summary>
        public override bool WhisperReceive(GameLiving source, string text) =>
            source is GamePlayer player && HireDialog165.CompanionWhisper(this, player, text) || base.WhisperReceive(source, text);

        public void WakeRecoveryRest()
        {
            bool wasResting = _recoveryRestLocked;
            _recoveryRestLocked = false;
            if (_seatedByHabit)             // a seat taken by habit (WP5) stands on the bot's own move, cast or attack
                return;
            // Realism 4a (#487, tell 4): a rest that ends because the bot is full, with nothing to fight, keeps its seat
            // until the bot's next move, cast or attack (the habit seat's rules), instead of a 3-10 s sit-bounce. Visual only.
            // (The cheap reads first: a companion or a standing bot never reaches the fight check.)
            if (wasResting && _seatedForClients && IsAutonomousWorldBot && !IsTemporaryGroupHelper && !IsHiredCompanion &&
                !IsPlayerLedGroup && IsAlive && BotIdleLife.KeepsSeatAfterRest(true, true, true,
                    InCombat || BotRestRecovery.BlocksRest(this), IsMoving, IsCasting, IsAttacking))
            {
                _seatedByHabit = true;
                return;
            }
            SetSeatedForClients(false);     // stands up before it moves, casts or fights (0xF1)
        }

        /// <summary>
        /// Clears logical recovery as soon as this bot, its controlled pets, or
        /// a nearby member of its actual group enters combat. Support-only bots
        /// therefore heal and buff normally instead of retaining fast recovery
        /// while their party fights.
        /// </summary>
        public bool WakeRecoveryRestIfCombatBlocked()
        {
            if (!IsRecoveryResting || !BotRestRecovery.BlocksRest(this))
                return false;
            WakeRecoveryRest();
            return true;
        }

        // Wake only when an actual affordable spell is requested, not during
        // spell selection. Mobile song maintenance remains compatible with
        // out-of-combat logical recovery.
        public override bool CastSpell(Spell spell, SpellLine line, ISpellCastingAbilityHandler spellCastingAbilityHandler = null, bool checkLos = true)
        {
            if (CompanionFollowPolicy.DeferBuff(this, spell)) return false;
            // A hired companion's AI Options (CompanionSpellOptions165; false for every other bot).
            if (CompanionSpellOptions165.Blocks(this, spell)) return false;
            // Darkness Falls (1.2): no pet or turret near a fighting prince who eats it (autonomous gamebots only).
            if (EpicEncounterTactics.SummonFeedsPrince(this, spell)) return false;
            // A cast reveals the caster: a companion hidden with its player casts only heals until a fight starts.
            if (spell?.IsHealing != true && CompanionStealth.SuppressesCasting(this)) return false;
            if (AutonomousGroupFormationFollow.DeferBuff(this, spell)) return false;
            // Final boundary as well as selection filtering: no rank of the
            // disabled shield can keep a Cabalist/Enchanter bot channeling.
            // The pet focus shield is cast only by its own decision (BotBrain.TryStartPetFocusShield), never by upkeep.
            bool approvedPetShield = PetFocusShieldApproved && BotPetFocusShieldPolicy.IsPetFocusShield(spell);
            if (AutonomousPetSupport.IsDisabledBotDamageShield(this, spell) && !approvedPetShield ||
                BotSpellPower.BlocksAttackerRotation(this, spell) ||
                !AnimistSingleTargetPolicy.AllowsAutomatedSpell(this, spell) ||
                !BotAnimistPolicy.MayPlant(this, spell))
            {
                CabalistRestDiagnostics.Attempt(this, spell, false);
                return false;
            }

            if (spell == null || !CanAffordConcentration(spell) || Mana < BotSpellPower.Cost(this, spell, line) ||
                castingComponent.HasPendingSkillRequests ||
                BotSpellPower.IsOffensiveCaster(this) && castingComponent.IsCasting && !castingComponent.IsFocusChanneling)
            {
                CabalistRestDiagnostics.Attempt(this, spell, false);
                return false;
            }

            // Song maintenance may continue while recovering and must not wake
            // a resting companion. A real, otherwise-valid buff, pet summon, or
            // combat cast is an intentional action and wakes it exactly once.
            if (IsTemporaryCompanionRestLocked)
            {
                if (!BotSongTwistPolicy.IsMobileSong(this, spell))
                {
                    (Brain as BotBrain)?.MarkTemporaryCompanionRestActivity();
                    WakeTemporaryCompanionRest();
                }
            }
            else if (IsTemporaryGroupHelper && !BotSongTwistPolicy.IsMobileSong(this, spell))
                (Brain as BotBrain)?.MarkTemporaryCompanionRestActivity();

            // Quickcast stays off for every bot cast except an open-field gamebot's key cast under interrupt, which
            // BotQuickCast arms (#531 step 2d). Any other Quickcast effect is cleared, without changing human casts.
            if (!BotQuickCast.TryArm(this, spell))
                EffectListService.GetAbilityEffectOnTarget(this, eEffect.QuickCast)?.End();

            if (IsRecoveryResting && !IsTemporaryGroupHelper &&
                !BotSongTwistPolicy.IsMobileSong(this, spell))
                WakeRecoveryRest();
            // a seated bot stands to cast, as a player does (a song keeps the rest; it sits again after)
            if (_seatedForClients)
                SetSeatedForClients(false);
            bool started = base.CastSpell(spell, line, spellCastingAbilityHandler, checkLos);
            CabalistRestDiagnostics.Attempt(this, spell, started);
            if (started && CharacterClass?.ID == (int)eCharacterClass.Animist)
                AnimistSingleTargetPolicy.CastOwnerSpell(this, spell);
            return started;
        }

        public override bool CastSpell(Spell spell, SpellLine line, bool checkLos)
        {
            return CastSpell(spell, line, null, checkLos);
        }

        public override void StartAttack(GameObject target)
        {
            // Verified damage/group-threat paths clear the lock before asking
            // the bot to attack. Reject stale AI/pet work that arrives while a
            // parked party is still recovering.
            if (target != null && IsRecoveryResting)
                return;
            if (target != null && _seatedByHabit)
                StandFromHabit();           // stands to fight (WP5 sitting habit)

            // 1.2 epic rule (Funus with his weapon puzzle): a bot never starts an attack on a boss it must not hit (its hits
            // heal him to full), and an archer that may fight him with its distance weapon only starts with that weapon.
            // Play test duo14: reavers he turned on fought back in melee (40 of his 110 heals).
            if (target is GameLiving living && EpicEncounterTactics.MustNotHit(this, living))
            {
                if (RefusesAttackOn(this, living))
                    return;
                if (ActiveWeaponSlot != eActiveWeaponSlot.Distance)
                    SwitchWeapon(eActiveWeaponSlot.Distance);
            }

            // A ranged shot owns its aim/release animation.  Reissuing the
            // same target every think pulse used to reset that state, producing
            // repeated bow preparation with no completed shot.  Suppress only
            // duplicate GameBot requests while a real ranged action is active;
            // a new target or a completed/cleared state still starts normally.
            if (target != null && ActiveWeaponSlot == eActiveWeaponSlot.Distance &&
                attackComponent.AttackState &&
                rangeAttackComponent.RangedAttackState != eRangedAttackState.None &&
                (TargetObject == target || rangeAttackComponent.AutoFireTarget == target))
                return;

            base.StartAttack(target);
        }

        /// <summary>The bot must not hit <paramref name="target"/> at all and has no distance weapon it may fight it with (an
        /// epic rule: Funus while his puzzle is on, for a class he takes no damage from), so it starts no attack on it.</summary>
        public static bool RefusesAttackOn(GameBot bot, GameLiving target) =>
            EpicEncounterTactics.MustNotHit(bot, target) && !EpicEncounterTactics.FightsAtRange(bot, target);

        public override bool AddToWorld()
        {
            // The guild name under the bot: its own guild (gamebot) or its owner's (/spawn companion), set before it
            // appears so no extra update is sent (AutonomousBotGuilds).
            GuildName = AutonomousBotGuilds.GuildNameFor(this);
            if (!base.AddToWorld())
                return false;

            RandomNumberDeck = new PlayerDeck();
            // An intentional MoveTo keeps both registrations alive across the
            // brief remove/add cycle. Re-registering here reset watchdog clocks
            // and dirtied the persistent record on every region transition.
            if (IsAutonomousWorldBot && !IsIntentionalWorldMove)
            {
                if (_autonomousRecoveryStartTick <= 0)
                    _autonomousRecoveryStartTick = GameLoop.GameLoopTime;
                AutonomousBotRegistry.Register(this);
                AutonomousStuckWatchdog.Register(this);
            }
            return true;
        }

        /// <summary>
        /// Generic NPC movement temporarily removes and re-adds the object.
        /// Keep an autonomous character registered as online across that
        /// intentional transfer so population state and watchdog clocks survive.
        /// The ordinary RemoveFromWorld pet release is retained, allowing the
        /// pet manager to re-summon a legal pet in the destination region.
        /// </summary>
        private int _corpseReleaseMove;

        /// <summary>The release's own corpse move (the bot is raised right after): the one move a dead bot may make.</summary>
        private bool MoveCorpseForRelease(ushort regionID, int x, int y, int z, ushort heading)
        {
            _corpseReleaseMove++;
            try { return MoveTo(regionID, x, y, z, heading); }
            finally { _corpseReleaseMove--; }
        }

        /// <summary>
        /// A body stays where it fell until it is raised or released (owner 2026-10-30: a companion killed outside an inn
        /// showed up dead beside the innkeeper 6 seconds later, by an in-region teleport). Any other move of a dead bot is
        /// refused and logged with its caller.
        /// </summary>
        private bool RefuseCorpseMove(string kind, ushort regionID, int x, int y, int z)
        {
            if (Health > 0 || ObjectState != eObjectState.Active || _corpseReleaseMove > 0)
                return false;
            if (log.IsWarnEnabled)
                log.Warn($"BOT_CORPSE_MOVE_REFUSED {kind} bot=\"{Name}\" id={DatabaseID} from={CurrentRegionID}:{X},{Y},{Z} " +
                     $"to={regionID}:{x},{y},{z}{Environment.NewLine}{Environment.StackTrace}");
            return true;
        }

        public override bool MoveInRegion(ushort regionID, int x, int y, int z, ushort heading, bool forceMove)
        {
            if (RefuseCorpseMove("MoveInRegion", regionID, x, y, z))
                return false;
            if (forceMove || !InCombat)
                RvrTeleports.Before(this, regionID, x, y, z, forceMove ? "moveInRegionForced" : "moveInRegion");   // #505: logging only
            return base.MoveInRegion(regionID, x, y, z, heading, forceMove);
        }

        public override bool MoveTo(ushort regionID, int x, int y, int z, ushort heading)
        {
            if (RefuseCorpseMove("MoveTo", regionID, x, y, z))
                return false;
            RvrTeleports.Before(this, regionID, x, y, z, "moveTo");   // #505: logging only
            // 1.2.2: a move within the region removes the bot from the world and adds it again; a dead bot's removal is
            // still held back from the original clients then (BotDeathAnimation.LeavesWorld).
            bool withinRegion = regionID == CurrentRegionID;
            if (withinRegion) _movingWithinRegion++;
            try { return MoveToWithPet(regionID, x, y, z, heading); }
            finally { if (withinRegion) _movingWithinRegion--; }
        }

        private int _movingWithinRegion;

        /// <summary>1.2.2: the bot is in a <see cref="MoveTo"/> within its region (removed and added again, not leaving).</summary>
        public bool MovingWithinRegion => _movingWithinRegion > 0;

        private bool MoveToWithPet(ushort regionID, int x, int y, int z, ushort heading)
        {
            SetSeatedForClients(false);     // never arrives seated
            // Owner 2026-10-07: travelling the old frontiers, the pets of the player's /spawn and /raid companions died
            // on every move (the move removes the bot from the world, which released its pet) and their pet classes kept
            // re-summoning. Within a region a companion's pet now comes along and is placed behind it, as a player's pet
            // is on a teleport (GamePlayer.MoveTo). Across regions pets are released as for players. Gamebots unchanged.
            GameNPC carriedPet = IsTemporaryGroupHelper && regionID == CurrentRegionID && ControlledBrain?.Body is { IsAlive: true } body &&
                body.ObjectState == eObjectState.Active ? body : null;
            if (carriedPet == null)
                return MoveToCore(regionID, x, y, z, heading);
            _keepPetThroughMove++;
            bool result;
            try { result = MoveToCore(regionID, x, y, z, heading); }
            finally { _keepPetThroughMove--; }
            if (result && ControlledBrain?.Body == carriedPet && carriedPet.IsAlive && carriedPet.ObjectState == eObjectState.Active &&
                carriedPet.MaxSpeedBase > 0)
            {
                Point2D behind = GetPointFromHeading(Heading, 64);
                carriedPet.MoveInRegion(CurrentRegionID, behind.X, behind.Y, Z + 10, (ushort)((Heading + 2048) % 4096), false);
            }
            return result;
        }

        private int _keepPetThroughMove;

        private bool MoveToCore(ushort regionID, int x, int y, int z, ushort heading)
        {
            if (!IsAutonomousWorldBot)
                return base.MoveTo(regionID, x, y, z, heading);

            if (regionID != CurrentRegionID)
            {
                AutonomousPetSupport.CancelPendingCharm(this);
                AutonomousPetSupport.ReleaseFieldTurrets(this);
            }

            bool moved = false;
            BeginIntentionalWorldMove();
            try
            {
                moved = base.MoveTo(regionID, x, y, z, heading);
                return moved;
            }
            finally
            {
                bool outermostMove = EndIntentionalWorldMove();

                // GameObject.MoveTo removes the actor before AddToWorld tries
                // the destination.  If that final add fails, the ordinary
                // removal callback was intentionally suppressed and the old
                // registry entry would otherwise remain forever: the launcher
                // showed an "Offline" bot as online, the objective timer kept
                // advancing, and the population controller could not reload it.
                // A rejected move which leaves the actor active is harmless and
                // stays in-world for the caller to replan.
                if (outermostMove && ObjectState != eObjectState.Active)
                {
                    AutonomousStuckWatchdog.Unregister(this);
                    AutonomousBotRegistry.Unregister(this);
                    if (PersistentRecord != null)
                    {
                        PersistentRecord.IsOnline = false;
                        PersistentRecord.Activity = "Queued after a failed world transfer";
                        PersistentRecord.ObjectiveProgress =
                            $"Region transfer to {regionID} was rejected after world removal; waiting for a clean reload";
                        PersistentRecord.LastUpdateUtc = DateTime.UtcNow.ToString("O");
                        PersistentRecord.Dirty = true;
                        MarkAutonomousStateDirty();
                        AutonomousBotStatusPersistence.Queue(this);
                    }
                    log.Warn($"AUTONOMOUS_TRANSFER_REQUEUE bot={Name} id={DatabaseID} " +
                             $"target_region={regionID} moved={moved}");
                }
            }
        }

        public override void ForgetDeparted()
        {
            base.ForgetDeparted();
            (Brain as BotBrain)?.ForgetDeparted();
        }

        public override void Delete()
        {
            if (ObjectState is eObjectState.Deleted)
                return;

            // Group.RemoveMember owns deletion of temporary helpers after its
            // bookkeeping finishes. If Delete was the entry point, allow that
            // one nested call to finish the object and do not clean it twice.
            if (Group != null)
            {
                Group group = Group;
                group.RemoveMember(this);
                if (ObjectState is eObjectState.Deleted)
                    return;
            }

            _deathRecoveryTimer?.Stop();
            _deathRecoveryTimer = null;
            AutonomousPetSupport.CancelPendingCharm(this);
            AutonomousPetSupport.ReleaseFieldTurrets(this);
            CompleteStableMasterRoute();
            if (IsAutonomousWorldBot && !IsIntentionalWorldMove)
                SaveAutonomousState(true);
            if (Owner != null)
            {
                GameEventMgr.RemoveHandler(Owner, GamePlayerEvent.Quit, new DOLEventHandler(OnOwnerQuit));
                if (IsTemporaryGroupHelper)
                    GameEventMgr.RemoveHandler(Owner, GamePlayerEvent.RegionChanged, new DOLEventHandler(OnOwnerRegionChanged));
            }
            base.Delete();
        }

        public override bool RemoveFromWorld()
        {
            // Capture this before base.RemoveFromWorld stops the brain. The stop
            // path is allowed to invoke virtual methods and must never change how
            // this removal is classified after it has begun.
            bool intentionalWorldMove = IsIntentionalWorldMove;
            if (!intentionalWorldMove)
                TempProperties.GetProperty<GameRelic>(GameRelic.PLAYER_CARRY_RELIC_WEAK)?.DropFromCarrier(this, "bot-removed");
            if (IsAutonomousWorldBot && !intentionalWorldMove)
                SaveAutonomousState(true);
            // A charm cast can have created its tagged candidate before the
            // ownership effect installs ControlledBrain. World removal must
            // clean that pending body even during this short handshake.
            AutonomousPetSupport.CancelPendingCharm(this);
            if (!base.RemoveFromWorld())
                return false;

            Duel?.Stop();

            if (ControlledBrain != null && _keepPetThroughMove == 0)
                ReleaseControlledPet(PetReleaseReason.WorldRemoval);

            if (IsAutonomousWorldBot && !intentionalWorldMove)
            {
                AutonomousStuckWatchdog.Unregister(this);
                AutonomousBotRegistry.Unregister(this);
                SaveOfflineAtRemoval();
            }

            return true;
        }

        /// <summary>
        /// At a gamebot's removal from the world: its roster row is marked offline and saved; true when it was. An in-memory
        /// bot (an Epic Special Force or a No Lifer's buffbot, IsInMemoryOnly) has no roster row and saves nothing. Owner's PC,
        /// build b1b6846, 2026-11-18: seven "Error saving data object (0 rows affected) in table offline_world_bots", each
        /// in the second of a BUFFBOT_LOGOUT, because this save skipped only the Special Forces (#334 fixed
        /// SaveAutonomousState only).
        /// </summary>
        public bool SaveOfflineAtRemoval()
        {
            if (PersistentRecord == null || IsInMemoryOnly)
                return false;
            PersistentRecord.IsOnline = false;
            PersistentRecord.Activity = "Offline";
            PersistentRecord.LastUpdateUtc = DateTime.UtcNow.ToString("O");
            PersistentRecord.Dirty = true;
            GameServer.Database.SaveObject(PersistentRecord);
            return true;
        }

        public override void OnAttackedByEnemy(AttackData ad)
        {
            // Notify BotBrain of the attack so it can add aggro and transition to combat state
            if (Brain is BotBrain botBrain)
                botBrain.OnAttackedByEnemy(ad);

            // A defensive class pet may retaliate when its owner is attacked,
            // while remaining non-aggressive on ordinary pulls.
            if (ControlledBrain is ControlledMobBrain controlledBrain)
                controlledBrain.OnOwnerAttacked(ad);

            BotBrain.NotifyNearbyGroupBots(this, ad);
            AutonomousBotSchedule.OnAttacked(this);
            base.OnAttackedByEnemy(ad);
            if (HealthPercent <= DOL.GS.CraftingEconomy.BotPotions.HealWhenHitPercent)
                DOL.GS.CraftingEconomy.BotPotions.OnAttacked(this); // Crafting 2.0 WP7: an emergency heal draught (bot_upkeep)
        }

        #endregion

        #region Race and Realm

        private void SetRaceAndRealm(GamePlayer owner)
        {
            if (CharacterClass == null)
                return;

            var eligibleRaces = CharacterClass.EligibleRaces;
            eGender gender = GenderId == (byte)eGender.Female ? eGender.Female : eGender.Male;

            if (RaceId != 0 && eligibleRaces != null)
            {
                // Use the user-provided race if it's eligible for this class
                var requestedRace = eligibleRaces.FirstOrDefault(r => (byte)r.ID == RaceId);
                if (requestedRace != null)
                {
                    Race = (short)requestedRace.ID;
                    Model = (ushort)requestedRace.GetModel(gender);
                    Gender = gender;
                }
                else
                {
                    // Provided race not eligible — fall back to random
                    PlayerRace playerRace = eligibleRaces[Util.Random(eligibleRaces.Count - 1)];
                    Race = (short)playerRace.ID;
                    Model = (ushort)playerRace.GetModel(gender);
                    Gender = gender;
                }
            }
            else if (eligibleRaces != null && eligibleRaces.Count > 0)
            {
                // No race specified — pick random eligible race
                PlayerRace playerRace = eligibleRaces[Util.Random(eligibleRaces.Count - 1)];
                Race = (short)playerRace.ID;
                Model = (ushort)playerRace.GetModel(gender);
                Gender = gender;
            }
            else
            {
                Race = owner?.Race ?? 1;
                Model = owner?.Model ?? 32;
                Gender = gender;
            }

            // Determine realm from class
            foreach (var kvp in GlobalConstants.STARTING_CLASSES_DICT)
            {
                if (kvp.Value.Contains((eCharacterClass)CharacterClass.ID))
                {
                    Realm = kvp.Key;
                    break;
                }
            }

            // A player's height, Short / Average / Tall, never bigger than a real player model (owner 2026-10-31: a
            // companion Bard stood far above its player; it was a random 45-60).
            Size = SizeForHeight(Util.Random(1, 3));
        }

        #endregion

        #region Stats

        private void InitializeBotStats()
        {
            RebuildPlayerBaseStats();
        }

        private void ApplyLevelStatGrowth(int level)
        {
            // Level is authoritative, including multi-level promotion callers.
            // Never add historical growth to the values SetStats already rebuilt.
            RebuildPlayerBaseStats();
        }

        /// <summary>
        /// Migrates an already-running Bonedancer from the legacy mixed-ratio
        /// plan to the focused 50-point primary/secondary plan.  This changes
        /// only the future allocation policy; the current specialization
        /// levels and leftover points are deliberately left untouched.
        /// </summary>
        internal void EnsureBonedancerFocusedSpecPlan()
        {
            if (IsAutonomousWorldBot && !string.IsNullOrWhiteSpace(PersistentRecord?.SerializedBuildPlan))
                return; // A saved lifetime build is not a maintenance-time respec opportunity.
            if (CharacterClass?.ID != (int)eCharacterClass.Bonedancer || BotSpec == null)
                return;

            bool alreadyFocused = BotSpec.SpecLines.Count == 2 &&
                BotSpec.SpecLines.Any(line => line.levelRatio >= 1.0f && line.SpecCap >= 50) &&
                BotSpec.SpecLines.Any(line => line.levelRatio <= 0.0f && line.SpecCap >= 50);
            eSpecType primary = BotSpec.SpecType is eSpecType.DarkBone or eSpecType.SuppBone or eSpecType.ArmyBone
                ? BotSpec.SpecType
                : BotSpec.ChooseRandomSpecialization(eCharacterClass.Bonedancer);
            bool deterministic = IsAutonomousWorldBot;
            long seed = deterministic ? DatabaseID : ObjectID;
            if (!alreadyFocused)
                BotSpec = new BonedancerBotSpec(primary, seed, deterministic);

            string primaryName = primary switch
            {
                eSpecType.DarkBone => Specs.Darkness,
                eSpecType.ArmyBone => Specs.BoneArmy,
                _ => Specs.Suppression,
            };
            Specialization primarySpec = GetSpecializationByName(primaryName);
            Specialization[] boneSpecs = [
                GetSpecializationByName(Specs.Darkness),
                GetSpecializationByName(Specs.Suppression),
                GetSpecializationByName(Specs.BoneArmy)
            ];

            int expectedPrimary = Math.Min(50, (int)Level);
            bool legacySpread = boneSpecs.Count(spec => spec?.Level > 1) > 1;
            bool freshCompanionMissedFocusedTraining = IsTemporaryGroupHelper && primarySpec?.Level < expectedPrimary;
            bool legacyBotCannotLearnFirstMinion = IsAutonomousWorldBot && Level >= 15 &&
                primarySpec?.Level < 15 && boneSpecs.Any(spec => spec?.Level > 1);
            if (!legacySpread && !freshCompanionMissedFocusedTraining && !legacyBotCannotLearnFirstMinion)
                return;

            // Rebuild only a legacy/malformed Bonedancer allocation, using the
            // same normal point-spending routine as a fresh bot. No points are
            // invented: one randomly locked primary is bought to the legal 50
            // cap, and only points left at level 50 flow into its one locked
            // secondary. Other classes and real players never enter this path.
            foreach (Specialization spec in boneSpecs.Where(spec => spec != null))
                spec.Level = 1;
            m_leftOverSpecPoints = 0;
            SpendSpecPoints(Level, 0);
            RefreshSpecDependantSkills(false);
            GetAllUsableListSpells(true);
            SetBotSpells();
            SortSpells();
            TempProperties.RemoveProperty(AutonomousPetSupport.BonedancerSpellRefreshProperty);
            if (IsAutonomousWorldBot)
                MarkAutonomousStateDirty();
        }

        /// <summary>
        /// One-time migration for the legacy Savage plan which trained the
        /// health-cost buff line ahead of its weapon. Re-spend the same earned
        /// points through the normal trainer algorithm; no levels, points or
        /// item bonuses are invented.
        /// </summary>
        internal bool EnsureSavageWeaponSpecPlan()
        {
            if (CharacterClass?.ID != (int)eCharacterClass.Savage || BotSpec == null ||
                BotSpec.WeaponOneType == 0)
                return false;

            Specialization weapon = GetSpecializationByName(SkillBase.ObjectTypeToSpec(BotSpec.WeaponOneType));
            Specialization savagery = GetSpecializationByName(Specs.Savagery);
            if (weapon == null || savagery == null ||
                !SavageBotCombatPolicy.NeedsWeaponTrainingRepair(weapon.Level, savagery.Level))
                return false;

            foreach (Specialization spec in GetSpecList().Where(spec => spec.Trainable))
                spec.Level = 1;
            m_leftOverSpecPoints = 0;
            SpendSpecPoints(Level, 0);
            MarkAutonomousStateDirty();
            return true;
        }

        public void RebuildPlayerBaseStats()
        {
            if (!GlobalConstants.STARTING_STATS_DICT.TryGetValue((eRace)Race, out var racialStats))
                GlobalConstants.STARTING_STATS_DICT.TryGetValue(eRace.Unknown, out racialStats);

            ICharacterClass characterClass = CharacterClass;
            eStat primary = characterClass?.PrimaryStat ?? eStat.UNDEFINED;
            eStat secondary = characterClass?.SecondaryStat ?? eStat.UNDEFINED;
            eStat tertiary = characterClass?.TertiaryStat ?? eStat.UNDEFINED;
            bool advanced = characterClass?.HasAdvancedFromBaseClass() == true;
            int previousHealth = m_health;
            try
            {
                for (eStat stat = eStat._First; stat <= eStat._Last; stat++)
                {
                    int racialBase = racialStats != null && racialStats.TryGetValue(stat, out int value) ? value : 60;
                    int target = BotAttributeProgression.Calculate(racialBase, Level, stat, primary, secondary, tertiary, advanced);
                    int delta = target - GetBaseStat(stat);
                    if (delta != 0) ChangeBaseStat(stat, (short)delta);
                }
            }
            finally
            {
                // GameNPC.ChangeBaseStat(CON) invokes a setter that refills HP.
                // A stat rebuild must not heal; real level-up refills happen later.
                m_health = Math.Min(previousHealth, MaxHealth);
            }
            int maximumMana = MaxMana;
            if (Mana > maximumMana) Mana = maximumMana;
        }

        /// <summary>The trainable lines and their levels now (the era draw's "no trained line above 1" and nearest build).</summary>
        private List<KeyValuePair<string, int>> TrainedSpecLevels() =>
            GetSpecList().Where(spec => spec.Trainable).Select(spec => new KeyValuePair<string, int>(spec.KeyName, spec.Level)).ToList();

        private void LoadPersistedSpecs(string serialized)
        {
            if (string.IsNullOrWhiteSpace(serialized))
                return;
            foreach (string entry in serialized.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                string[] parts = entry.Split('|', 2);
                if (parts.Length == 2 && int.TryParse(parts[1], out int level) && GetSpecializationByName(parts[0]) is Specialization spec)
                    spec.Level = Math.Clamp(level, 1, Level);
            }
        }

        private static byte ParseLastTrainedLevel(string serialized, byte currentLevel, string serializedSpecs)
        {
            const string prefix = "trained-level|";
            if (!string.IsNullOrWhiteSpace(serialized) && serialized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                byte.TryParse(serialized.Split('|')[1], out byte trained))
                return (byte)Math.Clamp((int)trained, 1, currentLevel);

            // Existing characters already have their trained spec levels saved.
            // Treat those as current so the migration never double-spends points.
            return string.IsNullOrWhiteSpace(serializedSpecs) ? (byte)1 : currentLevel;
        }

        /// <summary>A bot that leveled up trains on its way. One that never trained arrives trained in its lifetime build
        /// when the launcher made it above level 1: at level 50 ("+ Lv.50"), or at any level above 1 when it has never been
        /// saved ("+ Lv.20" makes levels 20-24; a saved bot above 1 got there by playing, and keeps visiting its trainer). Pure.</summary>
        public static bool ArrivesTrained(int level, int trainedLevel, bool neverSaved = false) =>
            trainedLevel <= 1 && (level >= 50 || neverSaved && level > 1);

        /// <summary>"trained-level|N|realm-points|M": realm ability points already spent at a trainer (0 when absent).</summary>
        public static int ParseTrainedRealmPoints(string serialized)
        {
            string[] parts = serialized?.Split('|') ?? Array.Empty<string>();
            for (int i = 0; i + 1 < parts.Length; i++)
                if (parts[i].Equals("realm-points", StringComparison.OrdinalIgnoreCase) && int.TryParse(parts[i + 1], out int points))
                    return Math.Max(0, points);
            return 0;
        }

        // ---- hired companions: their player trains their specs (owner 2026-10-30)

        /// <summary>
        /// Spec points the companion can spend now, counted as a player's are (GamePlayer.VerifySpecPoints): every point a
        /// player of its class has earned at its level - levels 2-5, each level above 5, each half level 40.5-49.5 it has
        /// passed, and the current one once its experience reaches the second stage - less what its trained lines cost a
        /// player: the class's autotrain lines are free up to a quarter of its level (LineCost).
        /// Owner 2026-11-17: "companions and bots should be the same as a player". Before Living Server 1.2 a hired
        /// companion got no half-level points (220/330/445 fewer at level 50 for x1.0/x1.5/x2.0) and paid for its autotrain
        /// levels (77 points a line at 50); counting from its level gives a saved companion those points to spend, and
        /// never takes a trained point away.
        /// <para>
        /// A hired companion never trains as a gamebot again, so a half level its player has spent is not earned twice by
        /// SpendSpecPoints at a later level: dismissing only parks it (it stays hired), releasing deletes its record
        /// (HiredCompanions165.AskRelease; a delete that fails retires it, and a retired bot never logs in), and the
        /// launcher deletes a hire row only together with its bot (BotPurge).
        /// </para>
        /// </summary>
        public int CompanionSpecPoints => Math.Max(0, CompanionEarnedSpecPoints - TrainedSpecPointCost());

        /// <summary>Every spec point a player of this class has earned at this level and experience.</summary>
        private int CompanionEarnedSpecPoints =>
            PlayerSpecPointTotal(CharacterClass.SpecPointsMultiplier, Level, ReachedSecondStage(Level, Experience));

        /// <summary>What the trainable lines cost a player of this class and level to train from 1 to their levels now.</summary>
        private int TrainedSpecPointCost() =>
            GetSpecList().Where(spec => spec.Trainable).Sum(spec => LineCost(spec.Level, Level, IsAutoTrainLine(spec)));

        // ---- autotrain, as a player's (GamePlayer.GetAutoTrainPoints, VerifySpecPoints and /train): the class's autotrain
        // lines (ICharacterClass.GetAutotrainableSkills: Slash and Thrust for an Armsman, Stealth for the assassins...) are
        // free up to a quarter of the character's level, whenever they are trained. This server's rule is the later
        // "autotrain refund" (its code says patch 1.87): nothing is raised by itself and nothing is lost by training early.
        // In 1.65 itself only a line left untrained got the free levels; the most a 1.65 player could have is what every
        // player has here, 77 points a line at 50.

        /// <summary>One of this class's autotrain lines (the player's own list and comparison).</summary>
        public bool IsAutoTrainLine(Specialization spec) =>
            spec != null && CharacterClass?.GetAutotrainableSkills() is { Count: > 0 } lines && lines.Contains(spec.KeyName);

        /// <summary>The level an autotrain line is free up to: a quarter of the character's level, at least 1. Pure.</summary>
        public static int AutoTrainLevel(int characterLevel) => Math.Max(1, characterLevel / 4);

        /// <summary>What a line at <paramref name="specLevel"/> costs a player of <paramref name="characterLevel"/>: the
        /// points to train it, less the autotrain levels of an autotrain line (GamePlayer.VerifySpecPoints). Pure.</summary>
        public static int LineCost(int specLevel, int characterLevel, bool autoTrain) =>
            PointsToTrain(specLevel) - (autoTrain ? PointsToTrain(Math.Min(AutoTrainLevel(characterLevel), specLevel)) : 0);

        /// <summary>What the next level of a line costs a player of <paramref name="characterLevel"/>: nothing while an
        /// autotrain line is below a quarter of the level (/train), else the new level. Pure.</summary>
        public static int NextLevelCost(int specLevel, int characterLevel, bool autoTrain) =>
            autoTrain && specLevel < characterLevel / 4 ? 0 : specLevel + 1;

        /// <summary>The points an autotrain line already at <paramref name="specLevel"/> gets back on reaching
        /// <paramref name="characterLevel"/>: at every fourth level the free level rises by one, and a line already there
        /// has paid for it (the step in GamePlayer.GetAutoTrainPoints mode 0 from one level to the next). Pure.</summary>
        public static int AutoTrainRefundAtLevel(int specLevel, int characterLevel) =>
            LineCost(specLevel, characterLevel - 1, true) - LineCost(specLevel, characterLevel, true);

        /// <summary>The points this bot's autotrain lines get back on reaching <paramref name="characterLevel"/>.</summary>
        private int AutoTrainRefundAtLevel(int characterLevel) =>
            characterLevel < 2 || CharacterClass?.GetAutotrainableSkills() is not { Count: > 0 } ? 0 :
                GetSpecList().Where(spec => spec.Trainable && IsAutoTrainLine(spec))
                    .Sum(spec => AutoTrainRefundAtLevel(spec.Level, characterLevel));

        /// <summary>What the next level of this line costs the companion (0 for a free autotrain level).</summary>
        public int CompanionTrainCost(Specialization spec) => NextLevelCost(spec.Level, Level, IsAutoTrainLine(spec));

        /// <summary>What raising this line to <paramref name="specLevel"/> costs the companion.</summary>
        public int CompanionTrainCost(Specialization spec, int specLevel) =>
            Math.Max(0, LineCost(specLevel, Level, IsAutoTrainLine(spec)) - LineCost(spec.Level, Level, IsAutoTrainLine(spec)));

        /// <summary>The points to train a line from 1 to <paramref name="specLevel"/> (2 + 3 + ... + level), as
        /// GamePlayer.VerifySpecPoints counts them. Pure.</summary>
        public static int PointsToTrain(int specLevel) => specLevel <= 1 ? 0 : specLevel * (specLevel + 1) / 2 - 1;

        /// <summary>The spec points a player earns by <paramref name="level"/> (GamePlayer.OnLevelUp, OnLevelSecondStage):
        /// with <paramref name="secondStage"/>, the half level <paramref name="level"/>.5 too (levels 40-49). Pure.</summary>
        public static int PlayerSpecPointTotal(int multiplier, int level, bool secondStage)
        {
            int total = 0;
            for (int reached = 2; reached <= level; reached++)
                total += SpecPointsForLevel(multiplier, reached, true);
            if (secondStage && level >= 40 && level < 50)
                total += multiplier * level / 20;
            return total;
        }

        /// <summary>A player of <paramref name="level"/> with this experience is at the level's second stage ("x.5", levels
        /// 40-49; GamePlayer.ExperienceForCurrentLevelSecondStage, whose bar the bot's levels share). Pure.</summary>
        public static bool ReachedSecondStage(int level, long experience)
        {
            if (level < 40 || level >= 50)
                return false;
            long current = GamePlayer.GetExperienceAmountForLevel(level - 1);
            long next = GamePlayer.GetExperienceAmountForLevel(level);
            return experience >= 1 + current + (next - current) / 2;
        }

        /// <summary>
        /// Living Server 1.2.1 (owner 2026-11-21): a hired companion's training on the arrival its summon planned
        /// (HiredCompanions165.PlanArrival), in its build, as a gamebot that arrives trained. A new hire spends every point
        /// of its level level by level (SpendSpecPoints, the "+ Lv.50" path); one hired on 1.2 and raised to its player's
        /// level keeps every line it has and spends only the points it gained (HiredCompanions165.RaisedSpecBudget): what
        /// its player left unspent stays unspent. The free respec still lets the player redo it.
        /// </summary>
        internal void TrainHiredCompanionArrival(HiredCompanions165.Arrival arrival)
        {
            if (arrival == null || BotSpec == null || CharacterClass == null)
                return;
            int costBefore = TrainedSpecPointCost();
            if (arrival.Kind == HiredCompanions165.ArrivalKind.NewHire && costBefore == 0)
            {
                m_leftOverSpecPoints = 0;
                SpendSpecPoints(Level, 1);
            }
            else if (arrival.Kind == HiredCompanions165.ArrivalKind.NewHire)
            {
                // trained already (a first summon that was saved but never joined): only what is left, never twice
                m_leftOverSpecPoints = CompanionSpecPoints;
                SpendSpecPoints(Level, Level, addLevelPoints: false);
            }
            else if (arrival.Kind == HiredCompanions165.ArrivalKind.Raise)
            {
                var lines = GetSpecList().Where(spec => spec.Trainable).Select(spec => (spec.Level, IsAutoTrainLine(spec))).ToList();
                m_leftOverSpecPoints = HiredCompanions165.RaisedSpecBudget(CharacterClass.SpecPointsMultiplier, arrival.FromLevel,
                    ReachedSecondStage(arrival.FromLevel, arrival.FromExperience), Level, lines);
                SpendSpecPoints(Level, Level, addLevelPoints: false);
            }
            else
                return;
            m_leftOverSpecPoints = CompanionSpecPoints;   // a player's count, as every later training reads it
            _lastAutonomousTrainedLevel = Level;
            MarkAutonomousStateDirty();
            arrival.Spent = Math.Max(0, TrainedSpecPointCost() - costBefore);
            arrival.Unspent = m_leftOverSpecPoints;
            arrival.Specs = string.Join(",", GetSpecList().Where(spec => spec.Trainable && spec.Level > 1).Select(spec => $"{spec.KeyName}:{spec.Level}"));
        }

        /// <summary>Raises one spec line by one level, as the player chose. Returns the problem, or null.</summary>
        public string CompanionTrain(Specialization spec)
        {
            if (spec == null || !spec.Trainable)
                return "That line can't be trained.";
            if (spec.Level >= Level)
                return $"{spec.Name} can't go above {Name}'s level ({Level}).";
            m_leftOverSpecPoints = CompanionSpecPoints;
            _lastAutonomousTrainedLevel = Level;
            int cost = CompanionTrainCost(spec);
            if (m_leftOverSpecPoints < cost)
                return $"{spec.Name} {spec.Level + 1} needs {cost} points; {Name} has {m_leftOverSpecPoints}.";
            m_leftOverSpecPoints -= cost;
            spec.Level++;
            AfterCompanionTraining();
            return null;
        }

        /// <summary>The free respec (owner rule): every trained line back to 1 - an autotrain line to its free level from
        /// level 8, as a player's respec leaves it (GamePlayer.RespecSingleLine; a line still at 1 is left alone, as
        /// RespecAllLines leaves it) - and all the points back to spend: a player's total for the level, half levels
        /// included.</summary>
        public void CompanionRespec()
        {
            foreach (Specialization spec in GetSpecList().Where(s => s.Trainable && s.Level >= 2))
                spec.Level = Level >= 8 && IsAutoTrainLine(spec) ? Level / 4 : 1;
            m_leftOverSpecPoints = CompanionSpecPoints;
            _lastAutonomousTrainedLevel = Level;
            AfterCompanionTraining();
        }

        private void AfterCompanionTraining()
        {
            RefreshSpecDependantSkills(false);
            SetBotSpells();
            SortStyles();
            SortSpells();
            BotRangedCombat.EnsureStarter(this);
            BotStarterInstruments.Ensure(this);
            MarkAutonomousStateDirty();
            AutonomousBotStatusPersistence.Queue(this, true);
            LogCompanionSkills("trained");
        }

        /// <summary>COMPANION_SKILLS_165: what a hired companion can use after training or a level (owner
        /// 2026-10-30: a level 5 Hunter trained Beastcraft 5 and seemed to use none of it).</summary>
        internal void LogCompanionSkills(string why)
        {
            if (!IsHiredCompanion || !log.IsInfoEnabled)
                return;
            static string Names<T>(IEnumerable<T> list, Func<T, string> name) =>
                list == null ? "-" : string.Join(", ", list.Where(x => x != null).Select(name).Distinct());
            log.Info($"COMPANION_SKILLS_165 {why} bot=\"{Name}\" id={DatabaseID} {ClassName} {Level} specs=[" +
                     string.Join(", ", GetSpecList().Select(sp => $"{sp.KeyName} {sp.Level}")) + "] spells=[" +
                     Names(Spells, sp => $"{sp.Name} ({sp.SpellType})") + "] styles=[" + Names(Styles, st => st.Name) + "]");
        }

        public bool TrainPendingSpecializations(GameTrainer trainer)
        {
            // A hired companion's player trains it (CompanionTrain); it never spends points at a trainer on its own.
            if (IsHiredCompanion)
                return false;
            if (!HasPendingAutonomousTraining && !AutonomousBotRealmAbilities.HasPurchase(this))
                return false;
            if (trainer == null ||
                trainer.ObjectState != eObjectState.Active || trainer.CurrentRegionID != CurrentRegionID ||
                !IsWithinRadius(trainer, 350) ||
                trainer.TrainedClass != eCharacterClass.Unknown && trainer.TrainedClass != (eCharacterClass)CharacterClass.ID)
                return false;

            // Realm abilities are bought at the same visit (the class trainer, as a player would).
            string realmAbilities = AutonomousBotRealmAbilities.Train(this);
            if (!HasPendingAutonomousTraining)
            {
                MarkAutonomousStateDirty();
                AutonomousBotStatusPersistence.Queue(this, true);
                return realmAbilities.Length > 0;
            }

            byte previous = _lastAutonomousTrainedLevel;
            Dictionary<string, int> beforeSpecs = GetSpecList()
                .Where(spec => spec.Trainable)
                .ToDictionary(spec => spec.KeyName, spec => spec.Level, StringComparer.OrdinalIgnoreCase);
            int abilitiesBefore = GetAllAbilities().Count;
            SpendSpecPoints(Level, previous);
            _lastAutonomousTrainedLevel = Level;
            if (EraLifetimeBuild.FirstTrainedAtFifty(BotSpec, previous, Level))
                log.Info(EraLifetimeBuild.TrainedLine(Name, DatabaseID, Level, BotSpec, GetBaseSpecLevel, m_leftOverSpecPoints));
            RefreshSpecDependantSkills(false);
            SetBotSpells();
            SortStyles();
            SortSpells();
            BotRangedCombat.EnsureStarter(this); // Only an empty slot, once the weapon ability is learned.
            BotStarterInstruments.Ensure(this);
            // Advanced Albion weapon plans still need their ordinary base
            // weapon before the class grants the planned line.  Reconcile at
            // the exact unlock level without changing the bot's lifetime plan.
            eCharacterClass trainedClass = (eCharacterClass)CharacterClass.ID;
            if (trainedClass == eCharacterClass.Savage ||
                !ShouldUsePlannedTwoHandedPrimary(trainedClass, previous,
                    BotSpec?.Is2H == true, BotSpec?.WeaponTwoType ?? 0) &&
                ShouldUsePlannedTwoHandedPrimary(trainedClass, Level,
                    BotSpec?.Is2H == true, BotSpec?.WeaponTwoType ?? 0))
                EnsureAutonomousStarterWeapon();
            MarkAutonomousStateDirty();
            // Training and its real equipment/inventory result are durable via
            // the prioritized persistence queue. Do not stall an NPC service
            // thread behind another SQLite writer for several seconds.
            AutonomousBotStatusPersistence.Queue(this, true);

            string changes = string.Join(",", GetSpecList()
                .Where(spec => spec.Trainable && (!beforeSpecs.TryGetValue(spec.KeyName, out int oldLevel) || oldLevel != spec.Level))
                .Select(spec =>
                {
                    int oldLevel = beforeSpecs.TryGetValue(spec.KeyName, out int value) ? value : 0;
                    return $"{spec.KeyName}:{oldLevel}->{spec.Level}";
                }));
            if (AutonomousDiagnosticsProperties.Training)
                log.Info($"AUTONOMOUS_TRAINING bot=\"{Name}\" id={DatabaseID} class=\"{ClassName}\" level={Level} " +
                     $"trainer=\"{trainer.Name}\" trainedFrom={previous} trainedThrough={_lastAutonomousTrainedLevel} " +
                     $"specializations=\"{changes}\" abilities={abilitiesBefore}->{GetAllAbilities().Count} unspent={m_leftOverSpecPoints}");
            return true;
        }

        public override void CheckWeaponMagicalEffect(AttackData ad)
        {
            int charges = ad.Weapon?.PoisonCharges ?? 0;
            base.CheckWeaponMagicalEffect(ad);
            if (ad.Weapon != null && charges != ad.Weapon.PoisonCharges)
                BotPoisonSupply.Changed(this);
        }

        public void MarkAutonomousStateDirty()
        {
            if (IsAutonomousWorldBot)
                AutonomousStateDirty = true;
        }

        public bool SaveAutonomousState(bool includeInventory)
        {
            if (IsInMemoryOnly)
            {
                // Nothing about a Special Forces member or a No Lifer's buffbot is ever saved. A buffbot's record is not a
                // roster row, so saving it would insert one: on 5378dbe every BUFFBOT_LOGOUT added a new gamebot with the
                // buffbot's name, class and level (BUFFBOT_LOGOUT "Eilriorrin" 22:26:05, then roster row 5876
                // "Eilriorrin" logging in with ERA_BUILD_ASSIGNED at 22:26:34).
                AutonomousStateDirty = false;
                return true;
            }
            OfflineWorldBotRecord record = PrepareAutonomousStateSnapshot();
            if (record == null)
                return false;

            bool saved;
            using (AiProfile.Lock(AutonomousBotStatusPersistence.DatabaseWriteLock, AiSystem.LockDatabaseWrite))
                saved = record.IsPersisted ? GameServer.Database.SaveObject(record) : GameServer.Database.AddObject(record);
            if (includeInventory && Inventory is BotInventory persistentInventory)
            {
                using (AiProfile.Lock(AutonomousBotStatusPersistence.DatabaseWriteLock, AiSystem.LockDatabaseWrite))
                    saved &= persistentInventory.SaveIntoDatabase(InternalID);
            }
            if (saved)
                AutonomousStateDirty = false;
            return saved;
        }

        public OfflineWorldBotRecord PrepareAutonomousStateSnapshot()
        {
            if (!IsAutonomousWorldBot || PersistentRecord == null)
                return null;

            OfflineWorldBotRecord record = PersistentRecord;
            record.Level = Level;
            record.Experience = Experience;
            record.RealmPoints = AutonomousRealmPoints;
            record.RegionId = CurrentRegionID;
            record.ZoneId = CurrentZone?.ID ?? 0;
            record.ZoneName = CurrentZone?.Description;
            record.X = X;
            record.Y = Y;
            record.Z = Z;
            record.Health = Math.Max(1, Health);
            record.Mana = Math.Max(0, Mana);
            record.Endurance = Math.Max(0, Endurance);
            record.IsAlive = SavedAlive(ObjectState == eObjectState.Active, IsAlive, Health);
            record.IsOnline = ObjectState == eObjectState.Active;
            record.SerializedSpecs = string.Join(';', GetSpecList().Where(spec => spec.Trainable).Select(spec => $"{spec.KeyName}|{spec.Level}"));
            record.SerializedAbilities = $"trained-level|{_lastAutonomousTrainedLevel}|realm-points|{AutonomousBotRealmAbilities.TrainedPoints(this)}" +
                                         AutonomousStarterGear.MarkerSuffix(_starterGearGiven);
            record.UnspentSpecPoints = m_leftOverSpecPoints;
            record.LastSavedUtc = DateTime.UtcNow.ToString("O");
            record.LastUpdateUtc = record.LastSavedUtc;
            record.PersistedStateVersion = 1;
            record.Dirty = true;
            return record;
        }

        /// <summary>
        /// Pure: the alive flag a status save writes. GameLiving.IsAlive is false for anything out of the world, and the
        /// status queue can save a bot after it left the world (a hired companion parked by /hire dismiss or its player's
        /// logout, owner's PC 2026-11-17 on 5ddd003: IsAlive=0 with Health=116, so summon and login said "dead"). Out of
        /// the world, health decides, whatever IsAlive says.
        /// </summary>
        public static bool SavedAlive(bool inWorld, bool aliveInWorld, int health) => inWorld ? aliveInWorld : health > 0;

        public void MarkAutonomousStateSaved() => AutonomousStateDirty = false;

        #endregion

        #region Specialization System

        protected virtual void AddSpecialization(Specialization skill, bool notify)
        {
            if (skill == null)
                return;

            lock (((ICollection)m_specialization).SyncRoot)
            {
                if (m_specialization.TryGetValue(skill.KeyName, out Specialization specialization))
                {
                    specialization.Level = skill.Level;
                    return;
                }

                m_specialization.Add(skill.KeyName, skill);
            }
        }

        public virtual bool RemoveSpecialization(string specKeyName)
        {
            lock (((ICollection)m_specialization).SyncRoot)
            {
                return m_specialization.Remove(specKeyName);
            }
        }

        /// <summary>
        /// Whether a trained specialization (level above 0) raises this skill property: GetSpecList's answer without its
        /// sorted copy. Issue #540: the gear checks asked this through GetSpecList for every skill bonus of every Exchange
        /// candidate, and the sort's arrays and iterators were the 1.2.2 PC log's next largest allocations.
        /// </summary>
        public bool HasTrainedSpecRaising(eProperty property)
        {
            lock (((ICollection)m_specialization).SyncRoot)
            {
                foreach (Specialization spec in m_specialization.Values)
                    if (spec != null && spec.Level > 0 && SkillBase.SpecToSkill(spec.KeyName) == property)
                        return true;
            }
            return false;
        }

        public virtual IList<Specialization> GetSpecList()
        {
            lock (((ICollection)m_specialization).SyncRoot)
            {
                return m_specialization.Select(item => item.Value)
                    .OrderBy(it => it.LevelRequired).ThenBy(it => it.ID).ToList();
            }
        }

        public virtual Specialization GetSpecializationByName(string name)
        {
            lock (((ICollection)m_specialization).SyncRoot)
            {
                foreach (var entry in m_specialization)
                {
                    if (entry.Key.Equals(name, StringComparison.OrdinalIgnoreCase))
                        return entry.Value;
                }
            }

            return null;
        }

        public override int GetBaseSpecLevel(string keyName)
        {
            lock (((ICollection)m_specialization).SyncRoot)
            {
                if (m_specialization.TryGetValue(keyName, out Specialization spec))
                    return spec.Level;
            }

            return 0;
        }

        public override int GetModifiedSpecLevel(string keyName)
        {
            if (keyName.StartsWith(GlobalSpellsLines.Champion_Lines_StartWith))
                return 50;

            if (keyName.StartsWith(GlobalSpellsLines.Realm_Spells))
                return Level;

            Specialization spec = null;
            int level = 0;

            lock (((ICollection)m_specialization).SyncRoot)
            {
                if (!m_specialization.TryGetValue(keyName, out spec))
                {
                    if (keyName == GlobalSpellsLines.Combat_Styles_Effect)
                    {
                        if (CharacterClass.ID == (int)eCharacterClass.Reaver || CharacterClass.ID == (int)eCharacterClass.Heretic)
                            level = GetModifiedSpecLevel(Specs.Flexible);
                        if (CharacterClass.ID == (int)eCharacterClass.Valewalker)
                            level = GetModifiedSpecLevel(Specs.Scythe);
                        if (CharacterClass.ID == (int)eCharacterClass.Savage)
                            level = GetModifiedSpecLevel(Specs.Savagery);
                    }

                    return level;
                }
            }

            if (spec != null)
            {
                level = spec.Level;
                eProperty skillProp = SkillBase.SpecToSkill(keyName);
                if (skillProp != eProperty.Undefined)
                    level += GetModified(skillProp);
            }

            return level;
        }

        public virtual void LoadClassSpecializations(bool sendMessage)
        {
            if (CharacterClass == null)
                return;

            IDictionary<Specialization, int> careers = SkillBase.GetSpecializationCareer(CharacterClass.ID);
            var speclist = GetSpecList();
            var careerslist = careers.Keys.Select(k => k.KeyName.ToLower());

            foreach (var spec in speclist.Where(sp => sp.Trainable || !sp.AllowSave))
            {
                if (!careerslist.Contains(spec.KeyName.ToLower()))
                    RemoveSpecialization(spec.KeyName);
            }

            foreach (var constraint in careers)
            {
                if (constraint.Key is IMasterLevelsSpecialization)
                    continue;

                if (Level >= constraint.Value)
                {
                    if (!HasSpecialization(constraint.Key.KeyName))
                        AddSpecialization(constraint.Key, sendMessage);
                }
                else
                {
                    if (HasSpecialization(constraint.Key.KeyName))
                        RemoveSpecialization(constraint.Key.KeyName);
                }
            }
        }

        /// <summary>
        /// A saved gamebot's points are counted again as a player's are, from the level it last trained at
        /// (GamePlayer.VerifySpecPoints; owner 2026-11-17: "companions and bots should be the same as a player"): every point
        /// a player of its class has earned there - half levels included - less what its lines cost a player there, with
        /// the autotrain levels free. When that is more than it saved (a bot trained before the autotrain refund, or before
        /// the half levels, #326), it gets the difference; a trained point is never taken away. A bot standing at the level
        /// it trained at spends the points now, as it would at the trainer (gamebots at 50 never train again); one with
        /// levels still to train spends them at its next trainer visit.
        /// </summary>
        private void ApplySavedSpecPointRefund()
        {
            int trainedAt = _lastAutonomousTrainedLevel;
            int owed = SavedSpecPointsOwed(CharacterClass.SpecPointsMultiplier, trainedAt,
                GetSpecList().Where(spec => spec.Trainable).Select(spec => (spec.Level, IsAutoTrainLine(spec))));
            if (owed <= m_leftOverSpecPoints)
                return;
            int saved = m_leftOverSpecPoints;
            m_leftOverSpecPoints = owed;
            if (trainedAt == Level && BotSpec != null)
                SpendSpecPoints(Level, Level, addLevelPoints: false);
            MarkAutonomousStateDirty();
            SpecRefundLog.Record(ClassName, owed - saved,
                $"{SpecRefundLog.Tag} bot=\"{Name}\" id={DatabaseID} class=\"{ClassName}\" level={Level} " +
                $"trainedAt={trainedAt} saved={saved} owed={owed} unspent={m_leftOverSpecPoints}");
        }

        /// <summary>
        /// AUTONOMOUS_SPEC_REFUND without a flood on a world's first load after the update (thousands of bots): one line
        /// each for the first <see cref="PerBotLines"/> bots refunded, then <see cref="Summary"/> once the start-up ramp
        /// has finished, and again for any refunded later: <c>AUTONOMOUS_SPEC_REFUND_SUMMARY bots=812 points=61234
        /// shown=30 classes="Armsman:90/9810,Warrior:85/..."</c> (bots/points by class).
        /// </summary>
        public static class SpecRefundLog
        {
            public const string Tag = "AUTONOMOUS_SPEC_REFUND";
            public const int PerBotLines = 30;
            private static readonly object Gate = new();
            private static readonly Dictionary<string, (int Bots, long Points)> ByClass = new(StringComparer.Ordinal);
            private static int _shown;

            public static void Record(string className, int points, string line)
            {
                bool show;
                lock (Gate)
                {
                    ByClass[className ?? "?"] = ByClass.TryGetValue(className ?? "?", out var seen)
                        ? (seen.Bots + 1, seen.Points + points) : (1, points);
                    show = _shown < PerBotLines;
                    if (show)
                        _shown++;
                }
                if (show)
                    log.Info(line);
            }

            /// <summary>The summary of the refunds since the last one, or null when there were none. Pure but for the
            /// counts it resets.</summary>
            public static string Summary()
            {
                lock (Gate)
                {
                    if (ByClass.Count == 0)
                        return null;
                    int bots = ByClass.Values.Sum(entry => entry.Bots);
                    long points = ByClass.Values.Sum(entry => entry.Points);
                    string classes = string.Join(",", ByClass.OrderByDescending(entry => entry.Value.Points)
                        .ThenBy(entry => entry.Key, StringComparer.Ordinal)
                        .Select(entry => $"{entry.Key}:{entry.Value.Bots}/{entry.Value.Points}"));
                    string line = $"{Tag}_SUMMARY bots={bots} points={points} shown={_shown} classes=\"{classes}\"";
                    ByClass.Clear();
                    return line;
                }
            }

            /// <summary>Logs the summary, if there is one.</summary>
            public static void Flush()
            {
                if (Summary() is { } line)
                    log.Info(line);
            }

            public static void ResetForTests()
            {
                lock (Gate)
                {
                    ByClass.Clear();
                    _shown = 0;
                }
            }
        }

        /// <summary>The points a player of <paramref name="trainedAt"/> has left with these lines: earned less their cost
        /// (autotrain lines free up to a quarter of the level). Pure.</summary>
        public static int SavedSpecPointsOwed(int multiplier, int trainedAt, IEnumerable<(int Level, bool AutoTrain)> lines) =>
            PlayerSpecPointTotal(multiplier, trainedAt, false) - lines.Sum(line => LineCost(line.Level, trainedAt, line.AutoTrain));

        /// <summary>Spends the points of levels <paramref name="previousLevel"/>+1 to <paramref name="level"/> (and what is
        /// left over) along the bot's plan. With <paramref name="addLevelPoints"/> false and the two levels equal, it spends
        /// only what is left over, at the bot's level (a saved bot's refund).</summary>
        public void SpendSpecPoints(byte level, byte previousLevel, bool addLevelPoints = true)
        {
            if (BotSpec == null)
                return;

            BotSpec.SpecLines = BotSpec.SpecLines.OrderByDescending(ratio => ratio.levelRatio).ToList();

            int leftOverSpecPoints = m_leftOverSpecPoints;
            bool spentPoints = true;
            bool spendLeftOverPoints = false;
            bool trainsEveryPoint = !string.IsNullOrEmpty(BotSpec.EraBuildId);

            byte index = (byte)(previousLevel + 1);

            if (level == previousLevel)
                index = level;

            for (byte i = index; i <= Level; i++)
            {
                spentPoints = true;
                spendLeftOverPoints = false;

                // The level's points, and what a player's autotrain lines get back at this level (every fourth).
                int totalSpecPointsThisLevel = (addLevelPoints ? GetSpecPointsForLevel(i) + AutoTrainRefundAtLevel(i) : 0) +
                    leftOverSpecPoints;
                // Every autotrain line at its free level, trained or not: a player pays nothing for it (GamePlayer
                // .GetAutoTrainPoints), and 1.65 trained it by itself (research finding 1, review of 1f3dcefe2 item 3).
                foreach (Specialization autoTrain in GetSpecList())
                    if (autoTrain.Trainable && IsAutoTrainLine(autoTrain) && autoTrain.Level < AutoTrainLevel(i) && i >= 8)
                        autoTrain.Level = AutoTrainLevel(i);

                while (spentPoints)
                {
                    spentPoints = false;

                    foreach (BotSpecLine specLine in BotSpec.SpecLines)
                    {
                        // A line with no levelling share waits for level 50 - except in an era build, whose lines are
                        // capped at the build's points: there the points nothing else can take go into it at once, so
                        // the bot is always as trained as its points allow, as a player following a template is (owner
                        // 2026-11-17 18:59). The build still ends at its points: no line passes its cap.
                        if (specLine.levelRatio <= 0 && Level < 50 && !(spendLeftOverPoints && trainsEveryPoint))
                            continue;

                        Specialization spec = GetSpecializationByName(specLine.Spec);

                        if (spec != null)
                        {
                            if (spec.Level < specLine.SpecCap && spec.Level < i)
                            {
                                int specRatio = (int)(i * specLine.levelRatio);

                                if (!spendLeftOverPoints && spec.Level >= specRatio)
                                    continue;

                                // A player's cost at this level: an autotrain line is free below a quarter of it.
                                int totalCost = NextLevelCost(spec.Level, i, IsAutoTrainLine(spec));

                                if (totalSpecPointsThisLevel >= totalCost)
                                {
                                    totalSpecPointsThisLevel -= totalCost;
                                    spec.Level++;
                                    spentPoints = true;
                                }
                            }
                        }
                    }

                    if (!spentPoints && !spendLeftOverPoints)
                    {
                        spendLeftOverPoints = true;
                        spentPoints = true;
                    }
                }

                m_leftOverSpecPoints = leftOverSpecPoints = totalSpecPointsThisLevel;
            }
        }

        /// <summary>
        /// Every bot earns the half-level points above level 40, whether it trains every level or catches up several at
        /// once (owner 2026-11-17: "companions and bots should be the same as a player"). Players get them at each "x.5"
        /// second stage (GamePlayer.OnLevelSecondStage, multiplier x level / 20); a bot that trains here gets the half level
        /// before each level with that level, so at every whole level it has a player's total. Before 1.2 only a bot catching
        /// up more than one level (or made above level 1) was given them: a gamebot that trained every level reached 50 with
        /// 220/330/445 points fewer (x1.0, x1.5, x2.0; review of era builds WP0-2, fixed in #326), and a /spawn helper
        /// promoted from 49 to 50 missed the 49.5 points. Hired companions count as a player does (CompanionSpecPoints).
        /// </summary>
        private int GetSpecPointsForLevel(int level) =>
            SpecPointsForLevel(CharacterClass.SpecPointsMultiplier, level, true);

        /// <summary>The spec points for reaching <paramref name="level"/>; with <paramref name="halfLevel"/>, above level 40,
        /// also the half level before it (level - 1).5. Pure; the sum over levels 2-50 with half levels is
        /// DAoCLivingServer.Shared.EraBuildCatalog.PointBudget.</summary>
        public static int SpecPointsForLevel(int multiplier, int level, bool halfLevel)
        {
            int specpoints = 0;

            if (halfLevel && level > 40)
                specpoints += multiplier * (level - 1) / 20;

            if (level > 5)
                specpoints += multiplier * level / 10;
            else if (level >= 2)
                specpoints = level;

            return specpoints;
        }

        public virtual void RefreshSpecDependantSkills(bool sendMessages)
        {
            LoadClassSpecializations(sendMessages);

            lock (((ICollection)m_specialization).SyncRoot)
            {
                foreach (Specialization spec in m_specialization.Values)
                {
                    foreach (Ability ab in spec.GetAbilitiesForLiving(this))
                    {
                        if (!HasAbility(ab.KeyName) || GetAbility(ab.KeyName).Level < ab.Level)
                            AddAbility(ab, false);
                    }

                    foreach (Style st in spec.GetStylesForLiving(this))
                    {
                        AddStyle(st, false);
                    }

                    foreach (SpellLine sl in spec.GetSpellLinesForLiving(this))
                    {
                        AddSpellLine(sl, false);
                    }
                }
            }
        }

        public virtual void AddStyle(Style st, bool notify)
        {
            lock (Styles)
            {
                if (!Styles.Contains(st))
                    Styles.Add(st);
            }

            Styles = Styles;
        }

        public virtual void AddSpellLine(SpellLine line, bool notify)
        {
            if (line == null)
                return;

            SpellLine oldline = GetSpellLine(line.KeyName);
            if (oldline == null)
            {
                lock (m_spellLines)
                {
                    m_spellLines.Add(line);
                }
            }
            else
            {
                oldline.Level = line.Level;
            }
        }

        public virtual List<SpellLine> GetSpellLines()
        {
            lock (m_spellLines)
            {
                return new List<SpellLine>(m_spellLines);
            }
        }

        #endregion

        #region Spell Resolution

        protected ReaderWriterList<Tuple<Skill, Skill>> m_usableSkills = new ReaderWriterList<Tuple<Skill, Skill>>();
        protected ReaderWriterList<Tuple<SpellLine, List<Skill>>> m_usableListSpells = new ReaderWriterList<Tuple<SpellLine, List<Skill>>>();

        public virtual new List<Tuple<SpellLine, List<Skill>>> GetAllUsableListSpells(bool update = false)
        {
            List<Tuple<SpellLine, List<Skill>>> results = new List<Tuple<SpellLine, List<Skill>>>();

            if (!update)
            {
                if (m_usableListSpells.Count > 0)
                    results = new List<Tuple<SpellLine, List<Skill>>>(m_usableListSpells);

                if (results.Count > 0)
                    return results;
            }

            m_usableListSpells.FreezeWhile(innerList =>
            {
                List<Tuple<SpellLine, List<Skill>>> finalbase = new List<Tuple<SpellLine, List<Skill>>>();
                List<Tuple<SpellLine, List<Skill>>> finalspec = new List<Tuple<SpellLine, List<Skill>>>();

                foreach (Specialization spec in GetSpecList().Where(item => !item.HybridSpellList))
                {
                    var spells = spec.GetLinesSpellsForLiving(this);

                    foreach (SpellLine sl in spec.GetSpellLinesForLiving(this))
                    {
                        List<Tuple<SpellLine, List<Skill>>> working;
                        if (sl.IsBaseLine)
                            working = finalbase;
                        else
                            working = finalspec;

                        List<Skill> sps = new List<Skill>();
                        SpellLine key = spells.Keys.FirstOrDefault(el => el.ID == sl.ID);

                        if (key != null && spells.TryGetValue(key, out List<Skill> spellsInLine))
                        {
                            foreach (Skill sp in spellsInLine)
                                sps.Add(sp);
                        }

                        working.Add(new Tuple<SpellLine, List<Skill>>(sl, sps));
                    }
                }

                innerList.Clear();
                foreach (var tp in finalbase)
                {
                    innerList.Add(tp);
                    results.Add(tp);
                }

                foreach (var tp in finalspec)
                {
                    innerList.Add(tp);
                    results.Add(tp);
                }
            });

            return results;
        }

        public virtual List<Tuple<Skill, Skill>> GetAllUsableSkills(bool update = false)
        {
            List<Tuple<Skill, Skill>> results = new List<Tuple<Skill, Skill>>();

            if (!update)
            {
                if (m_usableSkills.Count > 0)
                    results = new List<Tuple<Skill, Skill>>(m_usableSkills);

                if (results.Count > 0)
                    return results;
            }

            m_usableSkills.FreezeWhile(innerList =>
            {
                IList<Specialization> specs = GetSpecList();
                List<Tuple<Skill, Skill>> copylist = new List<Tuple<Skill, Skill>>(innerList);

                foreach (Specialization spec in specs.Where(item => item.Trainable))
                {
                    int index = innerList.FindIndex(e => (e.Item1 is Specialization specialization) && specialization.ID == spec.ID);

                    if (index < 0)
                        innerList.Insert(innerList.Count(e => e.Item1 is Specialization), new Tuple<Skill, Skill>(spec, spec));
                    else
                    {
                        copylist.Remove(innerList[index]);
                        innerList[index] = new Tuple<Skill, Skill>(spec, spec);
                    }
                }

                foreach (Specialization spec in specs)
                {
                    foreach (Ability abv in spec.GetAbilitiesForLiving(this))
                    {
                        Ability ab = GetAbility(abv.KeyName);
                        if (ab == null)
                            ab = abv;

                        int index = innerList.FindIndex(k => (k.Item1 is Ability ability) && ability.ID == ab.ID);

                        if (index < 0)
                            innerList.Add(new Tuple<Skill, Skill>(ab, spec));
                        else
                        {
                            copylist.Remove(innerList[index]);
                            innerList[index] = new Tuple<Skill, Skill>(ab, spec);
                        }
                    }
                }

                foreach (Specialization spec in specs.Where(item => item.HybridSpellList))
                {
                    foreach (var sl in spec.GetLinesSpellsForLiving(this))
                    {
                        int index = -1;

                        foreach (Spell sp in sl.Value.Where(it => (it is Spell) && !((Spell)it).NeedInstrument).Cast<Spell>())
                        {
                            if (index < innerList.Count)
                                index = innerList.FindIndex(index + 1, e => (e.Item2 is SpellLine spellLine) && spellLine.ID == sl.Key.ID && (e.Item1 is Spell spell) && !spell.NeedInstrument);

                            if (index < 0 || index >= innerList.Count)
                            {
                                innerList.Add(new Tuple<Skill, Skill>(sp, sl.Key));
                                index = innerList.Count;
                            }
                            else
                            {
                                copylist.Remove(innerList[index]);
                                innerList[index] = new Tuple<Skill, Skill>(sp, sl.Key);
                            }
                        }
                    }
                }

                int songIndex = -1;
                foreach (Specialization spec in specs.Where(item => item.HybridSpellList))
                {
                    foreach (var sl in spec.GetLinesSpellsForLiving(this))
                    {
                        foreach (Spell sp in sl.Value.Where(it => (it is Spell) && ((Spell)it).NeedInstrument).Cast<Spell>())
                        {
                            if (songIndex < innerList.Count)
                                songIndex = innerList.FindIndex(songIndex + 1, e => (e.Item1 is Spell) && ((Spell)e.Item1).NeedInstrument);

                            if (songIndex < 0 || songIndex >= innerList.Count)
                            {
                                innerList.Add(new Tuple<Skill, Skill>(sp, sl.Key));
                                songIndex = innerList.Count;
                            }
                            else
                            {
                                copylist.Remove(innerList[songIndex]);
                                innerList[songIndex] = new Tuple<Skill, Skill>(sp, sl.Key);
                            }
                        }
                    }
                }

                foreach (Specialization spec in specs)
                {
                    foreach (Style st in spec.GetStylesForLiving(this))
                    {
                        int index = innerList.FindIndex(e => (e.Item1 is Style) && e.Item1.ID == st.ID);
                        if (index < 0)
                            innerList.Add(new Tuple<Skill, Skill>(st, spec));
                        else
                        {
                            copylist.Remove(innerList[index]);
                            innerList[index] = new Tuple<Skill, Skill>(st, spec);
                        }
                    }
                }

                foreach (var item in copylist)
                    innerList.Remove(item);

                foreach (var el in innerList)
                    results.Add(el);
            });

            return results;
        }

        private void SetBotSpells()
        {
            _powerSpellLines = new Dictionary<int, SpellLine>();
            if (CharacterClass == null)
                return;

            // Sluaghbinder is a hybrid combat class, but its three baseline
            // lines and the selected advanced line are ordinary list-caster
            // spell lines.  The generic hybrid path only reads
            // Specialization.HybridSpellList lines, so it silently produced a
            // spell-less bot (no Raise spell, pet upkeep, self buffs, or
            // instant Rot/Bane abilities).  Build its learned catalog from
            // both sources while retaining the originating line for power and
            // line-specific rank selection.
            if (CharacterClass.ID == (int)eCharacterClass.Sluaghbinder)
            {
                SetSluaghbinderSpells();
                return;
            }

            // The usable-skill lists are cached: rebuild them from the current specs and level first. Read from the cache,
            // a bot trained or levelled in the field kept its old spells until it was created again (owner 2026-10-30:
            // a level 5 Hunter trained to Beastcraft 5 used none of the new buffs until dismissed and summoned at the inn).
            GetAllUsableSkills(true);
            GetAllUsableListSpells(true);

            // Casters use list spells (highest level per type), hybrids use all usable skills
            if (CharacterClass.ClassType == eClassType.ListCaster)
                SetCasterSpells();
            else
                SetHybridSpells();

            if (IsEndgameCompanion)
                Spells = TemporaryCompanionBalance.HighestRanks(Spells, Level, SkillBase.GetSpellByID);
        }

        private void SetSluaghbinderSpells()
        {
            List<(Spell Spell, SpellLine Line)> learned = new();

            // Baseline and selected advanced lines are represented by the
            // normal list-spell resolver.  Refreshing here is intentional:
            // companions and persistent bots can be promoted/trained after
            // construction and must see the newly learned ranks immediately.
            foreach (Tuple<SpellLine, List<Skill>> tuple in GetAllUsableListSpells(true))
            {
                if (tuple?.Item1 == null || tuple.Item2 == null)
                    continue;

                // The Sluaghbinder bot build commits to one advanced path.
                // GetSpellLinesForLiving intentionally exposes class-hint
                // lines to NPCs, so explicitly keep only the baseline lines
                // plus the path recorded by SluaghbinderBotSpec.  Otherwise
                // an untrained Bulwark/Bane/Covenant line could leak its
                // abilities into every bot at the NPC 75%-level estimate.
                if (!IsSluaghbinderLineAllowed(tuple.Item1))
                    continue;

                foreach (Spell spell in tuple.Item2.OfType<Spell>())
                {
                    if (spell.Level <= Level && !SluaghbinderBotPolicy.IsPlayerOnlyServiceSpell(spell))
                        learned.Add((spell, tuple.Item1));
                }
            }

            // Keep this second pass for future class extensions that expose a
            // spell line through HybridSpellList.  It also prevents this
            // dedicated branch from regressing if a weapon/style-backed spell
            // is added to the class later.
            foreach (Tuple<Skill, Skill> tuple in GetAllUsableSkills(true))
            {
                if (tuple?.Item1 is not Spell spell || spell.Level > Level ||
                    SluaghbinderBotPolicy.IsPlayerOnlyServiceSpell(spell))
                    continue;

                SpellLine line = tuple.Item2 as SpellLine ?? ResolvePowerSpellLine(spell, null);
                if (!IsSluaghbinderLineAllowed(line))
                    continue;
                learned.Add((spell, line));
            }

            // Select one legal rank per effect role *within its spell line*.
            // The line is part of the key so Abhartach's Rot and the advanced
            // Abhartach's Bane DoT remain separate abilities and can stack as
            // intended.  The generic caster selector does not include that
            // line identity and would collapse them into one spell.
            List<(Spell Spell, SpellLine Line)> selected = learned
                .Where(entry => entry.Spell != null)
                .GroupBy(entry => new
                {
                    LineId = entry.Line?.ID ?? 0,
                    entry.Spell.DamageType,
                    entry.Spell.SpellType,
                    entry.Spell.Frequency,
                    entry.Spell.CastTime,
                    entry.Spell.Target,
                    entry.Spell.Group,
                    entry.Spell.IsPBAoE,
                    entry.Spell.Radius,
                    entry.Spell.SubSpellID
                })
                .Select(group => group
                    .OrderByDescending(entry => entry.Spell.Level)
                    .ThenByDescending(entry => entry.Spell.ID)
                    .First())
                .OrderBy(entry => entry.Line?.IsBaseLine == true ? 0 : 1)
                .ThenBy(entry => entry.Line?.ID ?? int.MaxValue)
                .ThenBy(entry => entry.Spell.Level)
                .ThenBy(entry => entry.Spell.ID)
                .ToList();

            Spells = selected.Select(entry => entry.Spell).ToList();
            foreach ((Spell spell, SpellLine line) in selected)
            {
                if (spell != null)
                    _powerSpellLines[spell.ID] = line;
            }
        }

        private bool IsSluaghbinderLineAllowed(SpellLine line)
        {
            if (line == null || line.IsBaseLine)
                return line != null;

            return BotSpec?.SpecLines?.Any(specLine =>
                string.Equals(specLine.Spec, line.KeyName, StringComparison.OrdinalIgnoreCase)) == true;
        }

        private void SetCasterSpells()
        {
            List<Spell> spells = new List<Spell>();

            var dict = GetAllUsableListSpells();

            if (dict != null && dict.Count > 0)
            {
                foreach (var tuple in dict)
                {
                    if (tuple.Item2.Count > 0)
                    {
                        foreach (Skill skill in tuple.Item2)
                        {
                            if (skill is Spell spell && spell.Level <= Level && !spells.Contains(spell))
                            {
                                spells.Add(spell);
                                _powerSpellLines.TryAdd(spell.ID, tuple.Item1);
                            }
                        }
                    }
                }
            }

            List<Spell> highestSpellLevels = IsEndgameCompanion
                ? TemporaryCompanionBalance.HighestRanks(spells, Level, SkillBase.GetSpellByID)
                : GetHighestLevelSpells(spells);

            if (highestSpellLevels.Count > 0)
                Spells = highestSpellLevels;
        }

        private void SetHybridSpells()
        {
            List<Spell> spells = new List<Spell>();

            var usableSkills = GetAllUsableSkills();

            for (int i = 0; i < usableSkills.Count; i++)
            {
                Skill skill = usableSkills[i].Item1;

                if (skill is Spell spell && spell.Level <= Level)
                    spells.Add(spell);
            }

            if (spells.Count > 0)
                Spells = spells;
        }

        public List<Spell> GetHighestLevelSpells(List<Spell> spells)
        {
            spells = spells.OrderByDescending(spell => spell.Level).ToList();

            List<Spell> highestLevelSpells = new List<Spell>();

            foreach (Spell currentSpell in spells)
            {
                Spell existingSpell = highestLevelSpells.FirstOrDefault(x => AreSpellsEqual(x, currentSpell));

                if (existingSpell != null)
                {
                    if (existingSpell.Level < currentSpell.Level)
                        highestLevelSpells.Remove(existingSpell);
                    else
                        continue;
                }

                highestLevelSpells.Add(currentSpell);
            }

            return highestLevelSpells;
        }

        private bool AreSpellsEqual(Spell spellOne, Spell spellTwo)
        {
            // Ranks of one proc line may differ in proc chance (Valewalker
            // Witherstrike 20 vs 15); they still replace one another
            // (the newer rank takes the older one's place).
            bool sameProcLine = spellOne.SpellType is eSpellType.OffensiveProc or eSpellType.DefensiveProc &&
                                spellOne.Group != 0;
            return spellOne.DamageType == spellTwo.DamageType &&
                   spellOne.SpellType == spellTwo.SpellType &&
                   (sameProcLine || spellOne.Frequency == spellTwo.Frequency) &&
                   spellOne.CastTime == spellTwo.CastTime &&
                   spellOne.Target == spellTwo.Target &&
                   spellOne.Group == spellTwo.Group &&
                   spellOne.IsPBAoE == spellTwo.IsPBAoE;
        }

        #endregion

        #region Equipment

        public int BestArmorLevel
        {
            get
            {
                int bestLevel = -1;
                bestLevel = Math.Max(bestLevel, GetAbilityLevel("AlbArmor"));
                bestLevel = Math.Max(bestLevel, GetAbilityLevel("HibArmor"));
                bestLevel = Math.Max(bestLevel, GetAbilityLevel("MidArmor"));
                return bestLevel;
            }
        }

        public int BestShieldLevel => GetAbilityLevel("Shield");

        private void EquipBot(byte requestedEquipmentLevel = 0)
        {
            byte characterLevel = Level;
            byte equipmentLevel = (byte)TemporaryCompanionBalance.GearLevel(IsTemporaryGroupHelper, characterLevel, requestedEquipmentLevel);

            EquipmentLevelFloor = IsEndgameCompanion ? 50 : Math.Max(1, characterLevel - 10);
            EquipmentLevelCap = characterLevel;
            _generatedGearLevel = equipmentLevel;
            Inventory = new BotInventory();

            try
            {
                // Equipment generators read Level. Temporarily exposing the rolled
                // gear level keeps the character fully trained at its real level.
                Level = IsTemporaryGroupHelper ? characterLevel : equipmentLevel;
                // A Sluaghbinder Bane build, and a Covenant build that rolled
                // its scythe option, is a two-handed weapon plan.  The class
                // can use shields, but that does not make a shield part of
                // this build.  Do not let the generic Shield career grant it
                // an offhand on creation; doing so made every /spawn result
                // look like the default mace-and-shield loadout even when the
                // selected plan was supposed to use a scythe.
                bool sluaghbinderScythePlan = SluaghbinderUsesScythe;
                bool mayUseOffhand = !sluaghbinderScythePlan &&
                    (BotSpec?.SpecType is eSpecType.DualWield or eSpecType.DualWieldAndShield or eSpecType.LeftAxe ||
                     BestShieldLevel > 0);
                bool includeOffhand = TemporaryCompanionBalance.EquipOffhand(UsesCompanionKit, characterLevel, mayUseOffhand, Random.Shared.NextDouble());
                SetWeapons(includeOffhand);
                if (includeOffhand)
                    SetShield();
                SetRanged();
                BotRangedCombat.EnsureStarter(this);
                BotStarterInstruments.Ensure(this);
                if (UsesCompanionKit)
                    EnsureTemporaryHelperWeapon();
                Level = equipmentLevel;
                SetArmor();
                SetJewelry();
                _endgameCompanionEquipped = IsEndgameCompanion;
            }
            finally
            {
                Level = characterLevel;
                RefreshItemBonuses();
                if (!IsTemporaryGroupHelper)
                {
                    EquipmentLevelFloor = 0;
                    EquipmentLevelCap = 0;
                }
            }
        }

        // Level sync for /spawn helpers (#571 step 6): when the
        // owner out-levels a helper in his group, the helper catches up to his
        // level, with its stats, specs, spells, styles, generated kit and pet
        // brought up to date. Upward only. It also keeps the one-time endgame kit
        // for a helper already at 50 when its owner reaches 50. Run on the bot's
        // normal turn, never in a player event or during combat/casting/horse
        // travel. Preserve the existing build/group. Hired and /bot companions,
        // autonomous gamebots and the Epic Special Forces never enter it.
        internal bool TryMatchOwnerLevel()
        {
            if (!CompanionLevelSync.Applies(IsTemporaryGroupHelper, IsAutonomousWorldBot, IsEpicSpecialForce) ||
                Owner == null)
                return false;
            byte targetLevel = CompanionLevelSync.TargetLevel(Owner.Level, Level, MaxLevel);
            bool levelBehind = targetLevel > Level && CompanionLevelSync.InOwnersGroup(Group, Owner.Group);
            // As before #571: the one-time endgame promotion when the owner reaches 50, in his group or not.
            bool endgamePending = !_endgameCompanionEquipped && Owner.Level == 50;
            if ((!levelBehind && !endgamePending) ||
                !IsAlive || IsCasting || InCombat || IsAttacking || IsOnStableMasterRoute ||
                Brain is BotBrain { HasAggro: true } || ControlledBrain?.Body is { InCombat: true })
                return false;

            byte previousLevel = Level;
            int healthPercent = HealthPercent, manaPercent = ManaPercent, endurancePercent = EndurancePercent;
            Level = Math.Max(Level, targetLevel);
            for (int level = Math.Max(6, previousLevel + 1); level <= Level; level++)
                ApplyLevelStatGrowth(level);
            if (previousLevel < Level)
                SpendSpecPoints(Level, previousLevel);
            RefreshSpecDependantSkills(false);
            GetAllUsableSkills(true);
            GetAllUsableListSpells(true);
            SetBotSpells();
            SortStyles();
            SortSpells();
            EquipBot((byte)CompanionLevelSync.GearLevel(Level, _generatedGearLevel, Random.Shared.NextDouble()));
            // Replace a lower-level summon through its normal release lifecycle so
            // the brain resummons it at the new level. In particular this
            // preserves Necromancer shade cleanup.
            AutonomousPetSupport.CancelPendingCharm(this);
            AutonomousPetSupport.ReleaseFieldTurrets(this);
            CommandNpcRelease();
            Health = Math.Max(1, MaxHealth * healthPercent / 100);
            Mana = MaxMana * manaPercent / 100;
            Endurance = MaxEndurance * endurancePercent / 100;
            UpdateNPCEquipmentAppearance();
            Group?.UpdateMember(this, false, false);

            if (previousLevel < Level)
            {
                Owner.Out?.SendMessage($"{Name} is now level {Level}, matching you.",
                    eChatType.CT_System, eChatLoc.CL_SystemWindow);
                // A /spawn helper caught up to its owner's level (#571 step 6).
                log.Info($"COMPANION_LEVEL_SYNC bot=\"{Name}\" class=\"{ClassName}\" owner=\"{Owner.Name}\" " +
                         $"from={previousLevel} to={Level}");
            }
            return true;
        }

        private void SetWeapons(bool includeOffhand = true)
        {
            if (BotSpec == null)
                return;

            switch (BotSpec.SpecType)
            {
                case eSpecType.SluaghbinderBulwark:
                case eSpecType.SluaghbinderBane:
                case eSpecType.SluaghbinderCovenant:
                    // The selected Sluaghbinder plan is part of the bot's
                    // identity.  Bane always starts with a usable scythe and
                    // Covenant may start with the scythe it rolled; they must
                    // not wait for a future loot drop before their build is
                    // reflected in the active weapon.  Bulwark (and a
                    // mace-and-shield Covenant) keeps the class's legal mace.
                    if (SluaghbinderUsesScythe)
                        BotEquipment.SetMeleeWeapon(this, eObjectType.Scythe, eHand.twoHand);
                    else
                        BotEquipment.SetMeleeWeapon(this, eObjectType.Blunt, eHand.oneHand);
                    break;

                case eSpecType.DualWield:
                case eSpecType.DualWieldAndShield:
                    BotEquipment.SetMeleeWeapon(this, BotSpec.WeaponOneType, eHand.oneHand);
                    if (includeOffhand)
                        BotEquipment.SetMeleeWeapon(this, BotSpec.WeaponOneType, eHand.leftHand);
                    break;

                case eSpecType.LeftAxe:
                    BotEquipment.SetMeleeWeapon(this, BotSpec.WeaponOneType, eHand.oneHand);
                    BotEquipment.SetMeleeWeapon(this, BotSpec.WeaponOneType, eHand.twoHand);
                    if (includeOffhand)
                        BotEquipment.SetMeleeWeapon(this, BotSpec.WeaponTwoType, eHand.leftHand);
                    break;

                case eSpecType.OneHandAndShield:
                    BotEquipment.SetMeleeWeapon(this, BotSpec.WeaponOneType, eHand.oneHand);
                    break;

                case eSpecType.OneHandHybrid:
                case eSpecType.TwoHandHybrid:
                case eSpecType.TwoHanded when CharacterClass.ID != (int)eCharacterClass.Valewalker:
                    BotEquipment.SetMeleeWeapon(this, BotSpec.WeaponOneType, eHand.oneHand);
                    BotEquipment.SetMeleeWeapon(this, BotSpec.WeaponTwoType, eHand.twoHand, BotSpec.DamageType);
                    break;

                case eSpecType.Mid:
                case eSpecType.PacHealer:
                case eSpecType.AugHealer:
                case eSpecType.MendHealer:
                case eSpecType.MendShaman:
                case eSpecType.AugShaman:
                case eSpecType.SubtShaman:
                    BotEquipment.SetMeleeWeapon(this, BotSpec.WeaponOneType, eHand.oneHand);
                    BotEquipment.SetMeleeWeapon(this, BotSpec.WeaponOneType, eHand.twoHand);
                    break;

                case eSpecType.Instrument:
                    BotEquipment.SetMeleeWeapon(this, BotSpec.WeaponOneType, eHand.oneHand);
                    BotEquipment.SetInstrumentROG(this, Realm, (eCharacterClass)CharacterClass.ID, Level, eObjectType.Instrument, eInventorySlot.TwoHandWeapon, eInstrumentType.Flute);
                    BotEquipment.SetInstrumentROG(this, Realm, (eCharacterClass)CharacterClass.ID, Level, eObjectType.Instrument, eInventorySlot.DistanceWeapon, eInstrumentType.Drum);
                    BotEquipment.SetInstrumentROG(this, Realm, (eCharacterClass)CharacterClass.ID, Level, eObjectType.Instrument, eInventorySlot.FirstEmptyBackpack, eInstrumentType.Lute);
                    break;

                default:
                    if (CharacterClass.ClassType == eClassType.ListCaster ||
                        CharacterClass.ID == (int)eCharacterClass.Friar ||
                        CharacterClass.ID == (int)eCharacterClass.Valewalker)
                        BotEquipment.SetMeleeWeapon(this, BotSpec.WeaponOneType, eHand.twoHand);
                    else if (CharacterClass.ID != (int)eCharacterClass.Hunter)
                        BotEquipment.SetMeleeWeapon(this, BotSpec.WeaponOneType, eHand.oneHand);
                    else
                        BotEquipment.SetMeleeWeapon(this, BotSpec.WeaponOneType, eHand.twoHand);

                    if (BotSpec.WeaponOneType == eObjectType.Sword)
                        BotEquipment.SetMeleeWeapon(this, BotSpec.WeaponOneType, eHand.oneHand);
                    break;
            }

            if (CharacterClass.ID == (int)eCharacterClass.Savage &&
                SavageBotWeaponPolicy.TryBestGeneratedStarterSlot(this, out eActiveWeaponSlot savageSlot))
                SwitchWeapon(savageSlot);
            else if (PrimaryWeaponUsesTwoHands)
                SwitchWeapon(eActiveWeaponSlot.TwoHanded);
            else
                SwitchWeapon(eActiveWeaponSlot.Standard);
        }

        private static bool IsBackpackSlot(eInventorySlot slot) =>
            slot >= eInventorySlot.FirstBackpack && slot <= eInventorySlot.LastBackpack;

        public static int PlannedTwoHandedUnlockLevel(eCharacterClass classId,
            eObjectType plannedWeapon) => classId switch
        {
            // Classic Albion promotion gates. These bots begin directly in
            // their final class, so equipment must still respect the levels at
            // which a real character receives the advanced weapon ability.
            eCharacterClass.Paladin => 5,
            eCharacterClass.Armsman when plannedWeapon == eObjectType.PolearmWeapon => 5,
            eCharacterClass.Armsman when plannedWeapon == eObjectType.TwoHandedWeapon => 10,
            // Treat an unrecognized persisted Armsman 2H plan conservatively;
            // its base Slash/Crush/Thrust weapon is always legal in the interim.
            eCharacterClass.Armsman => 10,
            _ => 1,
        };

        public static bool ShouldUsePlannedTwoHandedPrimary(eCharacterClass classId,
            int level, bool plannedTwoHanded, eObjectType plannedWeapon = 0) =>
            plannedTwoHanded && level >= PlannedTwoHandedUnlockLevel(classId, plannedWeapon);

        private bool SluaghbinderUsesScythe =>
            CharacterClass?.ID == (int)eCharacterClass.Sluaghbinder &&
            BotSpec?.WeaponTwoType == eObjectType.Scythe &&
            BotSpec?.Is2H == true;

        private bool PrimaryWeaponUsesTwoHands =>
            (CharacterClass?.ID == (int)eCharacterClass.Sluaghbinder &&
             BotSpec?.WeaponTwoType == eObjectType.Scythe
                ? SluaghbinderUsesScythe
                : ShouldUsePlannedTwoHandedPrimary((eCharacterClass)(CharacterClass?.ID ?? 0),
                    Level, BotSpec?.Is2H == true, BotSpec?.WeaponTwoType ?? 0)) ||
            CharacterClass?.ClassType == eClassType.ListCaster ||
            CharacterClass?.ID is (int)eCharacterClass.Friar or (int)eCharacterClass.Valewalker;

        private eObjectType PreferredPrimaryWeaponType
        {
            get
            {
                if (BotSpec == null)
                    return 0;
                return BotWeaponStats.PrimaryType(BotSpec.WeaponOneType, BotSpec.WeaponTwoType, PrimaryWeaponUsesTwoHands);
            }
        }

        private static int OwnedWeaponScore(DbInventoryItem item, eObjectType preferredType)
        {
            if (item == null)
                return int.MinValue;
            int preferred = (eObjectType)item.Object_Type == preferredType ? 1_000_000 : 0;
            return preferred + Math.Max(0, item.Level) * 10_000 + Math.Max(0, item.DPS_AF) * 100 +
                   Math.Max(0, item.Quality) + Math.Max(0, item.Bonus) * 2;
        }

        private bool IsOwnedWeaponCandidate(DbInventoryItem item, eInventorySlot target, bool twoHandOnly)
        {
            if (!BotWeaponStats.FitsConfiguredSlot(this, item, target))
                return false;

            // Item_Type is retained when loot is placed in the backpack. It is
            // the authoritative hand shape; SlotPosition is only where that
            // particular item currently lives.
            bool twoHand = item.Item_Type == Slot.TWOHAND || item.SlotPosition == (int)eInventorySlot.TwoHandWeapon;
            if (twoHandOnly)
                return twoHand;
            return !twoHand || item.SlotPosition == (int)target;
        }

        private bool TryEquipOwnedWeaponUpgrade(eInventorySlot target, eObjectType preferredType, bool twoHandOnly)
        {
            if (Inventory == null)
                return false;

            DbInventoryItem current = Inventory.GetItem(target);
            int currentScore = BotWeaponStats.FitsConfiguredSlot(this, current, target)
                ? OwnedWeaponScore(current, preferredType) : int.MinValue;
            DbInventoryItem candidate = Inventory.AllItems
                .Where(item => IsOwnedWeaponCandidate(item, target, twoHandOnly) &&
                               ((eInventorySlot)item.SlotPosition == target ||
                                (!twoHandOnly && target == eInventorySlot.RightHandWeapon &&
                                 (eInventorySlot)item.SlotPosition == eInventorySlot.LeftHandWeapon) ||
                                IsBackpackSlot((eInventorySlot)item.SlotPosition)))
                .OrderByDescending(item => OwnedWeaponScore(item, preferredType))
                .FirstOrDefault();
            if (candidate == null || OwnedWeaponScore(candidate, preferredType) <= currentScore)
                return false;

            eInventorySlot source = (eInventorySlot)candidate.SlotPosition;
            if (source != target && !Inventory.MoveItem(source, target, Math.Max(1, candidate.Count)))
                return false;

            RefreshItemBonuses();
            SwitchWeapon(target == eInventorySlot.TwoHandWeapon
                ? eActiveWeaponSlot.TwoHanded : eActiveWeaponSlot.Standard);
            return true;
        }

        private bool TryMoveEquippedItemToBackpack(eInventorySlot slot)
        {
            DbInventoryItem item = Inventory?.GetItem(slot);
            if (item == null)
                return true;
            eInventorySlot storage = Inventory.FindFirstEmptySlot(
                eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack);
            if (storage == eInventorySlot.Invalid)
                storage = Inventory.FindFirstEmptySlot(eInventorySlot.FirstVault, eInventorySlot.LastVault);
            if (storage != eInventorySlot.Invalid &&
                Inventory.MoveItem(slot, storage, Math.Max(1, item.Count)))
                return true;

            // Explicit cleanup policy for legacy bad loadouts: a focus staff
            // must never remain equipped by a melee class merely because all
            // forty backpack slots are occupied. Friars are the sole staff
            // melee class; caster plans legitimately keep their focus staff.
            bool invalidMeleeStaff = (eObjectType)item.Object_Type == eObjectType.Staff &&
                                     CharacterClass?.ID != (int)eCharacterClass.Friar &&
                                     CharacterClass?.ClassType != eClassType.ListCaster;
            return invalidMeleeStaff && Inventory.RemoveItem(item);
        }

        private bool RequiresMeleeOffhand => BotSpec?.SpecType is
            eSpecType.DualWield or eSpecType.DualWieldAndShield or eSpecType.LeftAxe;

        private eObjectType PreferredOffhandWeaponType =>
            BotSpec?.SpecType == eSpecType.LeftAxe && BotSpec.WeaponTwoType != 0
                ? BotSpec.WeaponTwoType
                : BotSpec?.WeaponOneType ?? (eObjectType)0;

        private bool EnsureConfiguredMeleeOffhand()
        {
            if (!RequiresMeleeOffhand || PreferredOffhandWeaponType == 0)
                return false;

            DbInventoryItem current = Inventory.GetItem(eInventorySlot.LeftHandWeapon);
            if (BotWeaponStats.FitsConfiguredSlot(this, current, eInventorySlot.LeftHandWeapon))
                return false;

            // A bag/armor/focus item in the visible offhand is inventory, not
            // a weapon. Preserve it in the backpack before selecting or
            // creating the class weapon that the build actually trained.
            if (current != null && !TryMoveEquippedItemToBackpack(eInventorySlot.LeftHandWeapon))
                return false;

            if (TryEquipOwnedWeaponUpgrade(eInventorySlot.LeftHandWeapon,
                    PreferredOffhandWeaponType, false))
                return true;

            return TryCreateConfiguredStarterWeapon(
                eInventorySlot.LeftHandWeapon, PreferredOffhandWeaponType);
        }

        private bool StowInvalidInactiveWeaponSlots(eInventorySlot primarySlot)
        {
            bool changed = false;
            foreach (eInventorySlot slot in new[]
                     {
                         eInventorySlot.RightHandWeapon,
                         eInventorySlot.TwoHandWeapon,
                     })
            {
                if (slot == primarySlot)
                    continue;
                DbInventoryItem item = Inventory.GetItem(slot);
                if (item != null && !BotWeaponStats.CanUseMelee(this, item) &&
                    TryMoveEquippedItemToBackpack(slot))
                    changed = true;
            }
            return changed;
        }

        private bool TryCreateConfiguredStarterWeapon(eInventorySlot target, eObjectType type)
        {
            if (Inventory == null || type == 0 || !BotWeaponStats.IsMeleeWeapon(type))
                return false;

            DbInventoryItem occupied = Inventory.GetItem(target);
            if (BotWeaponStats.FitsConfiguredSlot(this, occupied, target))
            {
                SwitchWeapon(target == eInventorySlot.TwoHandWeapon
                    ? eActiveWeaponSlot.TwoHanded : eActiveWeaponSlot.Standard);
                return false;
            }

            eInventorySlot displacedSlot = eInventorySlot.Invalid;
            if (occupied != null)
            {
                // Builds prior to the persistent-fallback fix could save the
                // inventory relation but not its generated unique template.
                // Remove only that unusable GameBot-created row so a full bag
                // cannot permanently block the correct class weapon repair.
                if (BotWeaponStats.IsBrokenGeneratedFallback(occupied) && Inventory.RemoveItem(occupied))
                    occupied = null;
            }
            if (occupied != null)
            {
                displacedSlot = Inventory.FindFirstEmptySlot(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack);
                if (displacedSlot == eInventorySlot.Invalid)
                    displacedSlot = Inventory.FindFirstEmptySlot(eInventorySlot.FirstVault, eInventorySlot.LastVault);
                if (displacedSlot == eInventorySlot.Invalid)
                {
                    bool invalidMeleeStaff = (eObjectType)occupied.Object_Type == eObjectType.Staff &&
                                             CharacterClass.ID != (int)eCharacterClass.Friar &&
                                             CharacterClass.ClassType != eClassType.ListCaster;
                    if (!invalidMeleeStaff || !Inventory.RemoveItem(occupied))
                        return false;
                    occupied = null;
                }
                else if (!Inventory.MoveItem(target, displacedSlot, Math.Max(1, occupied.Count)))
                    return false;
            }

            var generated = new GeneratedUniqueItem(false, Realm, (eCharacterClass)CharacterClass.ID,
                (byte)Math.Clamp((int)Level, 1, (int)MaxLevel), type, target);
            generated.Item_Type = (int)target;
            generated.DPS_AF = Math.Max(generated.DPS_AF, BotWeaponStats.NormalDps(Level));
            generated.Quality = Math.Max(generated.Quality, 85);
            generated.SPD_ABS = Math.Max(generated.SPD_ABS, 20);
            generated.MaxCondition = Math.Max(generated.MaxCondition, 50_000);
            generated.Condition = Math.Max(generated.Condition, generated.MaxCondition);
            generated.MaxDurability = Math.Max(generated.MaxDurability, generated.MaxCondition);
            generated.Durability = Math.Max(generated.Durability, generated.MaxDurability);
            // GeneratedUniqueItem intentionally defaults to AllowAdd=false.
            // Persistent GameBot inventories need the unique side of the
            // relation saved with the inventory row or the weapon reloads as a
            // blank, unusable template after the next server start.
            if (Inventory is BotInventory { IsPersistent: true })
                generated.AllowAdd = true;

            GameInventoryItem replacement = GameInventoryItem.Create(generated);
            replacement.Creator = nameof(GameBot);
            replacement.Realm = (int)Realm;
            if (!Inventory.AddItem(target, replacement) || !BotWeaponStats.CanUseMelee(this, replacement))
            {
                if (Inventory.GetItem(target) == replacement)
                    Inventory.RemoveItemWithoutDbDeletion(replacement);
                if (occupied != null && displacedSlot != eInventorySlot.Invalid && Inventory.GetItem(target) == null)
                    Inventory.MoveItem(displacedSlot, target, Math.Max(1, occupied.Count));
                return false;
            }

            RefreshItemBonuses();
            SwitchWeapon(target == eInventorySlot.TwoHandWeapon
                ? eActiveWeaponSlot.TwoHanded : eActiveWeaponSlot.Standard);
            return true;
        }

        /// <summary>
        /// GameBot-only weapon reconciliation. Existing real inventory is
        /// preferred over creating anything: the highest-level class-valid
        /// weapon in the bot's equipment/backpack is moved into the primary
        /// hand as an upgrade. Invalid occupied slots are displaced into the
        /// backpack before a normalized fallback is created, so no item is
        /// silently deleted and no staff can become a hunter/shadowblade's
        /// melee weapon.
        /// </summary>
        private bool EnsureAutonomousStarterWeapon()
        {
            if (Inventory == null || CharacterClass == null || BotSpec == null)
                return false;

            if (CharacterClass.ID == (int)eCharacterClass.Savage)
                return EnsureSavageWeaponReady();

            eInventorySlot target = PrimaryWeaponUsesTwoHands
                ? eInventorySlot.TwoHandWeapon : eInventorySlot.RightHandWeapon;
            eObjectType preferredType = PreferredPrimaryWeaponType;
            if (preferredType == 0)
                return false;

            bool changed = TryEquipOwnedWeaponUpgrade(target, preferredType, PrimaryWeaponUsesTwoHands);

            DbInventoryItem current = Inventory.GetItem(target);
            if (!BotWeaponStats.FitsConfiguredSlot(this, current, target))
            {
                changed |= TryCreateConfiguredStarterWeapon(target, preferredType);
                current = Inventory.GetItem(target);
            }

            changed |= EnsureConfiguredMeleeOffhand();
            bool hasSluaghbinderShield = Inventory.AllItems.Any(item =>
                item != null && (eObjectType)item.Object_Type == eObjectType.Shield);
            if (CharacterClass.ID == (int)eCharacterClass.Sluaghbinder &&
                !SluaghbinderUsesScythe && BestShieldLevel > 0 && !hasSluaghbinderShield)
            {
                // Bulwark and mace-and-shield Covenant use the class-career
                // shield.  A scythe plan intentionally has no active offhand.
                BotEquipment.SetShield(this, BestShieldLevel);
                changed = true;
            }
            changed |= StowInvalidInactiveWeaponSlots(target);

            bool primaryReady = BotWeaponStats.FitsConfiguredSlot(this, Inventory.GetItem(target), target);
            if (primaryReady)
                SwitchWeapon(target == eInventorySlot.TwoHandWeapon
                    ? eActiveWeaponSlot.TwoHanded : eActiveWeaponSlot.Standard);
            else
            {
                // A full backpack can make it impossible to stow an old item
                // immediately. Never display/use it: fall back to any legal
                // standard or two-hand weapon already owned, and retry after
                // the next inventory-maintenance vendor pass creates space.
                bool standardReady = BotWeaponStats.CanUseMelee(this,
                    Inventory.GetItem(eInventorySlot.RightHandWeapon));
                bool twoHandReady = BotWeaponStats.CanUseMelee(this,
                    Inventory.GetItem(eInventorySlot.TwoHandWeapon));
                if (standardReady)
                    SwitchWeapon(eActiveWeaponSlot.Standard);
                else if (twoHandReady)
                    SwitchWeapon(eActiveWeaponSlot.TwoHanded);
                else if (log.IsWarnEnabled && GameLoop.GameLoopTime >= _nextWeaponRepairWarningTick)
                {
                    // Weapon reconciliation runs from the ordinary bot pulse.
                    // A full backpack or a temporarily unavailable service is
                    // not a new failure every pulse; rate-limit the diagnostic
                    // while retaining the retry and preserving all real items.
                    _nextWeaponRepairWarningTick = GameLoop.GameLoopTime + 60_000;
                    log.Warn($"No usable {preferredType} weapon or free replacement slot for bot {Name}; preserving existing inventory.");
                }
            }

            return changed;
        }

        private bool TryEquipBestSavageWeapon(eInventorySlot target)
        {
            DbInventoryItem current = Inventory.GetItem(target);
            double currentScore = SavageBotWeaponPolicy.CombatScore(this, current, target);
            DbInventoryItem best = Inventory.AllItems
                .Where(item => item != null &&
                    ((eInventorySlot)item.SlotPosition == target ||
                     IsBackpackSlot((eInventorySlot)item.SlotPosition) ||
                     target == eInventorySlot.RightHandWeapon &&
                     (eInventorySlot)item.SlotPosition == eInventorySlot.LeftHandWeapon))
                .OrderByDescending(item => SavageBotWeaponPolicy.CombatScore(this, item, target))
                .FirstOrDefault();
            if (best == null || SavageBotWeaponPolicy.CombatScore(this, best, target) <= currentScore)
                return false;

            eInventorySlot source = (eInventorySlot)best.SlotPosition;
            if (!Inventory.MoveItem(source, target, Math.Max(1, best.Count)))
                return false;
            RefreshItemBonuses();
            return true;
        }

        private bool EnsureSavageWeaponReady()
        {
            bool changed = TryEquipBestSavageWeapon(eInventorySlot.RightHandWeapon);
            bool handToHand = BotSpec.WeaponOneType == eObjectType.HandToHand;
            changed |= TryEquipBestSavageWeapon(handToHand
                ? eInventorySlot.LeftHandWeapon : eInventorySlot.TwoHandWeapon);

            if (!SavageBotWeaponPolicy.TryBestActiveSlot(this, out eActiveWeaponSlot activeSlot))
            {
                eInventorySlot starterSlot = handToHand
                    ? eInventorySlot.RightHandWeapon : eInventorySlot.TwoHandWeapon;
                changed |= TryCreateConfiguredStarterWeapon(starterSlot, BotSpec.WeaponOneType);
            }
            if (handToHand && SavageBotWeaponPolicy.CombatScore(this,
                    Inventory.GetItem(eInventorySlot.LeftHandWeapon), eInventorySlot.LeftHandWeapon) <= 0)
            {
                DbInventoryItem offhand = Inventory.GetItem(eInventorySlot.LeftHandWeapon);
                if (offhand == null || TryMoveEquippedItemToBackpack(eInventorySlot.LeftHandWeapon))
                    changed |= TryCreateConfiguredStarterWeapon(eInventorySlot.LeftHandWeapon, BotSpec.WeaponOneType);
            }
            else if (!handToHand && Inventory.GetItem(eInventorySlot.LeftHandWeapon) != null &&
                     TryMoveEquippedItemToBackpack(eInventorySlot.LeftHandWeapon))
                changed = true;

            // Legacy slots may still hold unusable weapons. Preserve the items
            // in storage when possible, while the active choice stays legal.
            changed |= StowInvalidInactiveWeaponSlots(eInventorySlot.Invalid);
            if (SavageBotWeaponPolicy.TryBestActiveSlot(this, out activeSlot))
                SwitchWeapon(activeSlot);
            return changed;
        }

        // Called by BotBrain only after a melee decision found no legal active
        // weapon.  Keeping the reconciliation behind this GameBot boundary
        // prevents ordinary GameNPC and real-player equipment from changing.
        internal bool EnsureBotWeaponReady()
        {
            bool changed = EnsureAutonomousStarterWeapon();
            if (changed && IsAutonomousWorldBot)
            {
                MarkAutonomousStateDirty();
                AutonomousBotEconomy.MarkInventoryChanged(this);
                AutonomousBotStatusPersistence.Queue(this, true);
            }
            return changed;
        }

        private void EnsureTemporaryHelperWeapon()
        {
            EnsureAutonomousStarterWeapon();
            bool hasUsableWeapon = BotWeaponStats.CanUseMelee(this, Inventory.GetItem(eInventorySlot.RightHandWeapon)) ||
                                   BotWeaponStats.CanUseMelee(this, Inventory.GetItem(eInventorySlot.TwoHandWeapon)) ||
                                   BotRangedCombat.CanUse(this, Inventory.GetItem(eInventorySlot.DistanceWeapon));
            if (hasUsableWeapon || BotSpec == null)
                return;

            bool useTwoHands = PrimaryWeaponUsesTwoHands;
            eInventorySlot slot = useTwoHands ? eInventorySlot.TwoHandWeapon : eInventorySlot.RightHandWeapon;
            BotEquipment.SetWeaponROG(
                this,
                Realm,
                (eCharacterClass)CharacterClass.ID,
                Level,
                BotSpec.WeaponOneType,
                slot,
                eDamageType.Slash);
            SwitchWeapon(useTwoHands ? eActiveWeaponSlot.TwoHanded : eActiveWeaponSlot.Standard);
        }

        private void SetShield()
        {
            BotEquipment.SetShield(this, BestShieldLevel);
        }

        private void SetRanged()
        {
            // The class's own kind first when it trains more than one (a mercenary's short bow, an armsman's crossbow):
            // SetRangedWeapon fills only an empty slot, so the ability order below no longer decides it.
            if (CharacterClass != null && BotRangedCombat.PreferredRangedType((eCharacterClass)CharacterClass.ID) is eObjectType own &&
                HasAbility(BotRangedCombat.AbilityFor(own)))
                BotEquipment.SetRangedWeapon(this, own);
            foreach (Ability ability in GetAllAbilities())
            {
                switch (ability.KeyName)
                {
                    case "Weaponry: Thrown": BotEquipment.SetRangedWeapon(this, eObjectType.Thrown); break;
                    case "Weaponry: Shortbows": BotEquipment.SetRangedWeapon(this, eObjectType.Fired); break;
                    case "Weaponry: Crossbow": BotEquipment.SetRangedWeapon(this, eObjectType.Crossbow); break;
                    case "Weaponry: Recurved Bows": BotEquipment.SetRangedWeapon(this, eObjectType.RecurvedBow); SwitchWeapon(eActiveWeaponSlot.Distance); break;
                    case "Weaponry: Longbows": BotEquipment.SetRangedWeapon(this, eObjectType.Longbow); SwitchWeapon(eActiveWeaponSlot.Distance); break;
                    case "Weaponry: Composite Bows": BotEquipment.SetRangedWeapon(this, eObjectType.CompositeBow); SwitchWeapon(eActiveWeaponSlot.Distance); break;
                }
            }
        }

        private void SetArmor()
        {
            int armorLevel = BestArmorLevel;
            eObjectType armorType = eObjectType.GenericArmor;

            switch (armorLevel)
            {
                case 1: armorType = eObjectType.Cloth; break;
                case 2: armorType = eObjectType.Leather; break;
                case 3:
                    armorType = (Realm == eRealm.Hibernia) ? eObjectType.Reinforced : eObjectType.Studded;
                    break;
                case 4:
                    armorType = (Realm == eRealm.Hibernia) ? eObjectType.Scale : eObjectType.Chain;
                    break;
                case 5: armorType = eObjectType.Plate; break;
            }

            BotEquipment.SetArmor(this, armorType);
        }

        private void SetJewelry()
        {
            BotEquipment.SetJewelryROG(this, Realm, (eCharacterClass)CharacterClass.ID, Level, eObjectType.Magical);
        }

        public virtual void RefreshItemBonuses()
        {
            ItemBonus.Clear();
            string slotToLoad = string.Empty;

            switch (VisibleActiveWeaponSlots)
            {
                case 16: slotToLoad = "rightandleftHandSlot"; break;
                case 18: slotToLoad = "leftandtwoHandSlot"; break;
                case 31: slotToLoad = "leftHandSlot"; break;
                case 34: slotToLoad = "twoHandSlot"; break;
                case 51: slotToLoad = "distanceSlot"; break;
                case 240: slotToLoad = "righttHandSlot"; break;
                case 242: slotToLoad = "twoHandSlot"; break;
            }

            foreach (DbInventoryItem item in Inventory.EquippedItems)
            {
                if (item == null)
                    continue;

                bool add = true;

                if (slotToLoad != string.Empty)
                {
                    switch (item.SlotPosition)
                    {
                        case Slot.TWOHAND:
                            if (!slotToLoad.Contains("twoHandSlot"))
                                add = false;
                            break;
                        case Slot.RIGHTHAND:
                            if (!slotToLoad.Contains("right"))
                                add = false;
                            break;
                        case Slot.SHIELD:
                        case Slot.LEFTHAND:
                            if (!slotToLoad.Contains("left"))
                                add = false;
                            break;
                        case Slot.RANGED:
                            if (slotToLoad != "distanceSlot")
                                add = false;
                            break;
                    }
                }

                if (!add)
                    continue;

                if (item.IsMagical)
                {
                    if (item.Bonus1 != 0) ItemBonus[(eProperty)item.Bonus1Type] += item.Bonus1;
                    if (item.Bonus2 != 0) ItemBonus[(eProperty)item.Bonus2Type] += item.Bonus2;
                    if (item.Bonus3 != 0) ItemBonus[(eProperty)item.Bonus3Type] += item.Bonus3;
                    if (item.Bonus4 != 0) ItemBonus[(eProperty)item.Bonus4Type] += item.Bonus4;
                    if (item.Bonus5 != 0) ItemBonus[(eProperty)item.Bonus5Type] += item.Bonus5;
                    if (item.Bonus6 != 0) ItemBonus[(eProperty)item.Bonus6Type] += item.Bonus6;
                    if (item.Bonus7 != 0) ItemBonus[(eProperty)item.Bonus7Type] += item.Bonus7;
                    if (item.Bonus8 != 0) ItemBonus[(eProperty)item.Bonus8Type] += item.Bonus8;
                    if (item.Bonus9 != 0) ItemBonus[(eProperty)item.Bonus9Type] += item.Bonus9;
                    if (item.Bonus10 != 0) ItemBonus[(eProperty)item.Bonus10Type] += item.Bonus10;
                    if (item.ExtraBonus != 0) ItemBonus[(eProperty)item.ExtraBonusType] += item.ExtraBonus;
                }
            }
        }

        public override DbInventoryItem ActiveLeftWeapon =>
            CharacterClass?.ID == (int)eCharacterClass.Savage &&
            (BotSpec?.WeaponOneType != eObjectType.HandToHand ||
             !BotWeaponStats.FitsConfiguredSlot(this, base.ActiveLeftWeapon, eInventorySlot.LeftHandWeapon))
                ? null : base.ActiveLeftWeapon;

        public override void SwitchWeapon(eActiveWeaponSlot slot)
        {
            // Play test diagnostic (Funus): who puts the melee weapon in hand of a bot that may fight its target only
            // with its distance weapon. Once per bot every ten minutes, the call path.
            if (slot != eActiveWeaponSlot.Distance && ActiveWeaponSlot == eActiveWeaponSlot.Distance &&
                TargetObject is GameLiving rangedOnly && EpicEncounterTactics.FightsAtRange(this, rangedOnly))
                EpicEncounterRestores.NoteMeleeSwitch(this, rangedOnly);
            // GameNPC.SwitchWeapon intentionally stops and restarts an active
            // attack, even when the requested slot is already selected. That
            // is useful for scripted NPCs, but inventory/equipment refreshes
            // can legitimately repeat the current slot for a GameBot. During
            // a bow draw that reset RangedAttackState to None and produced an
            // endless prepare animation instead of an arrow release.
            //
            // Only suppress a true no-op. If an item was replaced in the same
            // slot (or the standard offhand changed), run the native switch so
            // active item references and appearance are still refreshed.
            if (slot == ActiveWeaponSlot && ActiveSlotMatchesInventory(slot))
            {
                if (Inventory != null)
                    RefreshItemBonuses();
                return;
            }

            base.SwitchWeapon(slot);
            if (Inventory != null)
                RefreshItemBonuses();
        }

        private bool ActiveSlotMatchesInventory(eActiveWeaponSlot slot)
        {
            if (Inventory == null)
                return false;

            return slot switch
            {
                eActiveWeaponSlot.Standard =>
                    ReferenceEquals(ActiveWeapon, Inventory.GetItem(eInventorySlot.RightHandWeapon)) &&
                    ReferenceEquals(ActiveLeftWeapon,
                        CharacterClass?.ID == (int)eCharacterClass.Savage &&
                        (BotSpec?.WeaponOneType != eObjectType.HandToHand ||
                         !BotWeaponStats.FitsConfiguredSlot(this,
                             Inventory.GetItem(eInventorySlot.LeftHandWeapon), eInventorySlot.LeftHandWeapon))
                            ? null : Inventory.GetItem(eInventorySlot.LeftHandWeapon)),
                eActiveWeaponSlot.TwoHanded =>
                    ReferenceEquals(ActiveWeapon, Inventory.GetItem(eInventorySlot.TwoHandWeapon)),
                eActiveWeaponSlot.Distance =>
                    ReferenceEquals(ActiveWeapon, Inventory.GetItem(eInventorySlot.DistanceWeapon)),
                _ => false,
            };
        }

        #endregion

        #region Database

        public void SaveToDatabase()
        {
            if (IsTemporaryGroupHelper)
                return;
            BotDatabase.SaveBot(this);
        }

        public void LoadFromDatabase()
        {
            log.Debug($"LoadFromDatabase called for bot {Name}");
        }

        #endregion

        #region Movement

        /// <summary>
        /// Synchronizes the NPC movement component with coordinates assigned
        /// before AddToWorld. Login placement and validation run before the
        /// normal AddToWorld synchronization, so they must explicitly perform
        /// this step after loading or choosing a saved starting position.
        /// </summary>
        internal void SynchronizePositionForLoginValidation()
        {
            movementComponent.ForceUpdatePosition();
        }

        public override void WalkTo(Vector3 position, short speed)
        {
            if (IsOnStableMasterRoute)
                return;

            if (speed > 0 && IsRecoveryResting)
                return;

            // Persistent actors use collision-safe navigation for every ordinary
            // walk order, including combat approaches and service interactions.
            // A tiny cross-zone seam step remains direct because Detour meshes
            // are stored per zone and cannot calculate across that boundary.
            Zone destinationZone = CurrentRegion?.GetZone((int)position.X, (int)position.Y);
            if (IsAutonomousWorldBot && CurrentZone != null && destinationZone == CurrentZone)
                base.PathTo(position, speed);
            else
                base.WalkTo(position, speed);
        }

        public override void PathTo(Vector3 position, short speed)
        {
            if (speed > 0 && IsRecoveryResting)
                return;
            base.PathTo(position, speed);
        }

        public override void Follow(GameObject target, int minDistance, int maxDistance)
        {
            if (target != null && IsRecoveryResting)
                return;
            base.Follow(target, MeleeStandOff(target, minDistance), maxDistance);
        }

        /// <summary>
        /// Living Server 1.2, "arrive slightly short": an autonomous gamebot's melee approach to a monster stops 0-20 units
        /// short of the stick distance, the same for the same bot every fight (BotCombatMovement.StandOff), so bots don't all
        /// stand on one exact ring. Not for companions, player-led groups, dragons, epic encounters or raid dungeons.
        /// </summary>
        private int MeleeStandOff(GameObject target, int minDistance)
        {
            if (!IsAutonomousWorldBot || IsTemporaryGroupHelper || IsInMemoryOnly || IsPlayerLedGroup ||
                target == null || target != TargetObject || target is not GameNPC monster || monster is GameBot ||
                minDistance != StickMinimumRange || ActiveWeaponSlot == eActiveWeaponSlot.Distance ||
                DragonCombatGeometry.TargetReach(monster) > 0 || EpicEncounterTactics.For(monster) != null ||
                EpicEncounterTactics.IsTacticPart(monster) || AutonomousRealmRaid.IsRaidDungeon(CurrentRegionID))
                return minDistance;
            return BotCombatMovement.StandOff(minDistance, DatabaseID > 0 ? DatabaseID : ObjectID);
        }

        public new void StopFollowing()
        {
            base.StopFollowing();
        }

        public void MoveTo(Vector3 position)
        {
            WalkTo(position, MaxSpeed);
        }

        public override bool IsMoving => base.IsMoving || IsStrafing;

        #endregion

        #region Effectiveness

        // 1.0, less the RvR resurrection illness while an autonomous gamebot carries it (#521); nothing else sets it.
        public override double Effectiveness
        {
            get => Math.Max(0, 1.0 - _resurrectionIllnessPenalty);
            set { }
        }

        /// <summary>
        /// #521 (owner 2026-11-24: rez sickness by the player's rule in RvR): an autonomous gamebot raised after an RvR death
        /// at or above RESS_SICKNESS_LEVEL carries the RvR resurrection illness (spell 8181) as the player does
        /// (GamePlayer.OnRevive); a full raise halves it and Perfect Recovery removes it (ResurrectSpellHandler). PvE deaths
        /// and companions: none, as before.
        /// </summary>
        private void ApplyRvrResurrectionIllness()
        {
            bool rvrDeath = _lastDeathRvr;
            _lastDeathRvr = false;
            if (!rvrDeath)
            {
                TempProperties?.RemoveProperty(GamePlayer.RESURRECT_REZ_SICK_EFFECTIVENESS);
                return;
            }
            double rezSick = TempProperties.GetAllProperties().Contains(GamePlayer.RESURRECT_REZ_SICK_EFFECTIVENESS)
                ? TempProperties.GetProperty<double>(GamePlayer.RESURRECT_REZ_SICK_EFFECTIVENESS) : 1.0;
            if (!DOL.GS.ServerRules.PvpRules.RvrRezSicknessApplies(DOL.GS.ServerRules.PvpRules.IsAutonomousGamebot(this), rvrDeath,
                    Level, ServerProperties.Properties.RESS_SICKNESS_LEVEL, rezSick))
            {
                TempProperties.RemoveProperty(GamePlayer.RESURRECT_REZ_SICK_EFFECTIVENESS);
                return;
            }
            SpellLine line = SkillBase.GetSpellLine(GlobalSpellsLines.Realm_Spells);
            Spell illness = line == null ? null : SkillBase.FindSpell(8181, line);
            if (illness == null)
                return;
            ScriptMgr.CreateSpellHandler(this, illness, line)?.StartSpell(this);
        }

        double IGamePlayer.Effectiveness
        {
            get => Effectiveness;
            set { }
        }

        #endregion

        #region InteractDistance

        public override int InteractDistance => WorldMgr.VISIBILITY_DISTANCE;

        #endregion
    }
}
