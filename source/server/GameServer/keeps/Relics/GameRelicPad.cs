using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DOL.Events;
using DOL.GS.PacketHandler;
using DOL.Language;
using DOL.Logging;
using JNogueira.Discord.WebhookClient;

namespace DOL.GS
{
    public class GameRelicPad : GameStaticItem
    {
        private static readonly Logger log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);

        private const int PAD_AREA_RADIUS = 250;

        private PadArea _area;

        public override ushort Model
        {
            get => 2655;
            set => base.Model = value;
        }

        public override eRealm Realm
        {
            get => Emblem switch
            {
                1 or 11 => eRealm.Albion,
                2 or 12 => eRealm.Midgard,
                3 or 13 => eRealm.Hibernia,
                _ => eRealm.None,
            };
            set => base.Realm = value;
        }

        public virtual eRelicType PadType => Emblem switch
        {
            1 or 2 or 3 => eRelicType.Strength,
            11 or 12 or 13 => eRelicType.Magic,
            _ => eRelicType.Invalid,
        };

        private readonly object _mountLock = new();
        private readonly HashSet<GameRelic> _mountedRelics = new(3);
        public HashSet<GameRelic> MountedRelics { get { lock (_mountLock) return new(_mountedRelics); } }

        public GameRelicPad() : base() { }

        public override bool AddToWorld()
        {
            bool success = base.AddToWorld();

            if (success)
            {
                _area = new(this);
                CurrentRegion.AddArea(_area);
                RelicMgr.AddRelicPad(this);
            }

            return success;
        }

        public override bool RemoveFromWorld()
        {
            bool success = base.RemoveFromWorld();

            if (success)
            {
                if (_area != null)
                    CurrentRegion.RemoveArea(_area);

                RelicMgr.RemoveRelicPad(this);
            }

            return success;
        }

        public bool IsMountedHere(GameRelic relic)
        {
            lock (_mountLock) return _mountedRelics.Contains(relic);
        }

        public static void BroadcastDiscordRelic(string message, eRealm realm, string keepName)
        {
            int color;
            string avatarUrl;

            switch (realm)
            {
                case eRealm._FirstPlayerRealm:
                {
                    color = 16711680;
                    avatarUrl = string.Empty;
                    break;
                }
                case eRealm._LastPlayerRealm:
                {
                    color = 32768;
                    avatarUrl = string.Empty;
                    break;
                }
                default:
                {
                    color = 255;
                    avatarUrl = string.Empty;
                    break;
                }
            }

            if (DiscordClientManager.TryGetClient(WebhookType.RvR, out var client))
            {
                DiscordMessage discordMessage = new(
                    "",
                    username: "RvR",
                    avatarUrl: avatarUrl,
                    tts: false,
                    embeds:
                    [
                        new(
                            author: new(keepName),
                            color: color,
                            description: message
                        )
                    ]
                );

                client.SendToDiscordAsync(discordMessage);
            }
        }

        public bool MountRelic(GameRelic relic, bool returning)
        {
            lock (_mountLock) return MountRelicCore(relic, returning);
        }

        private bool MountRelicCore(GameRelic relic, bool returning)
        {
            if (_mountedRelics.Count >= 3)
            {
                if (log.IsErrorEnabled)
                    log.Error($"Pad {Name} is full (3 relics) {relic.Name}");

                return false;
            }

            if (!_mountedRelics.Add(relic))
            {
                if (log.IsErrorEnabled)
                    log.Error($"Relic {relic.Name} is already mounted on pad {Name} (Returning: {returning})");

                return false;
            }

            if (relic.CurrentCarrier != null && !returning)
            {
                string message = LanguageMgr.GetTranslation(ServerProperties.Properties.SERV_LANGUAGE, "GameRelicPad.MountRelic.Stored", relic.CurrentCarrier.Name, GlobalConstants.RealmToName(relic.CurrentCarrier.Realm), relic.Name, Name);

                foreach (GamePlayer otherPlayer in GameWideAnnouncements.Enabled(AnnouncementKind.RvrBattleground) ? ClientService.Instance.GetPlayers() : [])
                {
                    otherPlayer.Out.SendMessage(LanguageMgr.GetTranslation(otherPlayer.Client.Account.Language, "GameRelicPad.MountRelic.Captured", GlobalConstants.RealmToName(relic.CurrentCarrier.Realm), relic.Name), eChatType.CT_ScreenCenterSmaller, eChatLoc.CL_SystemWindow);
                    otherPlayer.Out.SendMessage($"{message}\n{message}\n{message}", eChatType.CT_Important, eChatLoc.CL_SystemWindow);
                }

                NewsMgr.CreateNews(message, relic.CurrentCarrier.Realm, eNewsType.RvRGlobal, false);

                if (ServerProperties.Properties.DISCORD_ACTIVE && !string.IsNullOrEmpty(ServerProperties.Properties.DISCORD_RVR_WEBHOOK_ID))
                    BroadcastDiscordRelic(message, relic.CurrentCarrier.Realm, relic.Name);

                foreach (GamePlayer player in relic.CurrentCarrier.GetPlayersInRadius(WorldMgr.VISIBILITY_DISTANCE * 5))
                {
                    if (player.Realm == relic.CurrentCarrier.Realm)
                        player.CapturedRelics++;
                }

                relic.LastCaptureDate = DateTime.Now;
                // RelicMounted fires from GameRelic once the relic is on this pad and of this realm (NotifyCustody).
            }
            else
            {
                string message = $"The {relic.Name} has been returned to {Name}.";

                foreach (GamePlayer otherPlayer in GameWideAnnouncements.Enabled(AnnouncementKind.RvrBattleground) ? ClientService.Instance.GetPlayers() : [])
                    otherPlayer.Out.SendMessage($"{message}\n{message}\n{message}", eChatType.CT_Important, eChatLoc.CL_SystemWindow);
            }

            relic.Realm = Realm;
            relic.CurrentRegionID = CurrentRegionID;
            UpdateRelicPositions();
            return true;
        }

        public void RemoveRelic(GameRelic relic)
        {
            lock (_mountLock) RemoveRelicCore(relic);
        }

        private void RemoveRelicCore(GameRelic relic)
        {
            if (!_mountedRelics.Remove(relic))
            {
                if (log.IsErrorEnabled)
                    log.Error($"Relic {relic.Name} is not mounted on pad {Name}");

                return;
            }

            if (relic.CurrentCarrier != null)
            {
                string message = LanguageMgr.GetTranslation(ServerProperties.Properties.SERV_LANGUAGE, "GameRelicPad.RemoveRelic.Removed", relic.CurrentCarrier.Name, GlobalConstants.RealmToName((eRealm)relic.CurrentCarrier.Realm), relic.Name, Name);

                foreach (GamePlayer otherPlayer in GameWideAnnouncements.Enabled(AnnouncementKind.RvrBattleground) ? ClientService.Instance.GetPlayers() : [])
                    otherPlayer.Out.SendMessage($"{message}\n{message}\n{message}", eChatType.CT_Important, eChatLoc.CL_SystemWindow);

                NewsMgr.CreateNews(message, relic.CurrentCarrier.Realm, eNewsType.RvRGlobal, false);

                if (ServerProperties.Properties.DISCORD_ACTIVE && !string.IsNullOrEmpty(ServerProperties.Properties.DISCORD_RVR_WEBHOOK_ID))
                    BroadcastDiscordRelic(message, relic.CurrentCarrier.Realm, relic.Name);

                // RelicStolen fires from GameRelic once the relic is off this pad and realmless (NotifyCustody).
            }

            UpdateRelicPositions();
        }

        /// <summary>
        /// Fires a relic custody event for this pad. GameRelic calls it after its own state has changed, so listeners see
        /// the relic where it now is (relic audit 2026-10-31, M3: the events fired before the relic's realm and pad were
        /// set, and RelicGuardManager scaled the sentinels one event late).
        /// </summary>
        public void NotifyCustody(RelicPadEvent e, GameLiving carrier, GameRelic relic)
        {
            if (e != null && relic != null)
                Notify(e, this, new RelicPadEventArgs(carrier, relic));
        }

        public int GetEnemiesOnPad()
        {
            ICollection<GamePlayer> players = GetPlayersInRadius(500);
            int enemyNearby = 0;

            foreach (GamePlayer player in players)
            {
                if (CountsOnPad(Realm, player.Realm, player.IsAlive, player.IsStealthed, true))
                    enemyNearby++;
            }

            enemyNearby += GetNPCsInRadius(500).OfType<GameBot>().Count(bot => bot.ObjectState == eObjectState.Active &&
                CountsOnPad(Realm, bot.Realm, bot.IsAlive, bot.IsStealthed, bot.IsAutonomousWorldBot || bot.Owner != null));

            return enemyNearby;
        }

        /// <summary>
        /// Whether one player or bot near the pad counts toward relic_players_required_on_pad: alive, not stealthed, of
        /// another realm than the pad's, and a player, a gamebot or a player's own companion bot (2026-10-15: a player
        /// with a full group of companions counted as one and could never take a relic; the owner wants a player and
        /// seven companions, and nobody else, to be enough).
        /// </summary>
        public static bool CountsOnPad(eRealm padRealm, eRealm realm, bool alive, bool stealthed, bool eligible) =>
            eligible && alive && !stealthed && realm != padRealm && realm != eRealm.None;

        public void RemoveRelics()
        {
            lock (_mountLock) _mountedRelics.Clear();
        }

        private void UpdateRelicPositions()
        {
            int count = _mountedRelics.Count;

            if (count == 0)
                return;

            if (count == 1)
            {
                GameRelic singleRelic = _mountedRelics.First();
                singleRelic.X = X;
                singleRelic.Y = Y;
                singleRelic.Z = Z;
                singleRelic.Heading = Heading;
                return;
            }

            double baseRotation = (Heading - 1024) * Math.PI / 2048.0;
            double angleIncrement = 2 * Math.PI / count;
            int i = 0;

            foreach (GameRelic relic in _mountedRelics)
            {
                const int Radius = 50;

                double angle = baseRotation + angleIncrement * i++;
                relic.X = (int) (X + Radius * Math.Cos(angle));
                relic.Y = (int) (Y + Radius * Math.Sin(angle));
                relic.Z = Z;
                relic.Heading = (ushort) (2048 * angle / Math.PI + 3072);
            }
        }

        public class PadArea : Area.Circle
        {
            private static readonly Logger log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);

            private readonly GameRelicPad _parent;

            public PadArea(GameRelicPad parentPad) : base("", parentPad.X, parentPad.Y, parentPad.Z, PAD_AREA_RADIUS)
            {
                _parent = parentPad;
            }

            public override void OnPlayerEnter(GamePlayer player)
            {
                GameRelic relicOnPlayer = player.TempProperties.GetProperty<GameRelic>(GameRelic.PLAYER_CARRY_RELIC_WEAK);

                if (relicOnPlayer == null)
                    return;

                if (relicOnPlayer.RelicType != _parent.PadType)
                {
                    player.Client.Out.SendMessage(string.Format(LanguageMgr.GetTranslation(player.Client.Account.Language, "GameRelicPad.OnPlayerEnter.EmptyRelicPad"), relicOnPlayer.RelicType), eChatType.CT_Important, eChatLoc.CL_SystemWindow);

                    if (log.IsDebugEnabled)
                        log.Debug($"Player {player.Name} needs to find an empty {relicOnPlayer.RelicType} relic pad in order to place {relicOnPlayer.Name}");

                    return;
                }

                if (player.Realm == _parent.Realm)
                {
                    if (log.IsDebugEnabled)
                        log.Debug($"Player {player.Name} captured relic {relicOnPlayer.Name}");

                    relicOnPlayer.RelicPadTakesOver(_parent, false);
                }
                else
                {
                    if (log.IsDebugEnabled)
                        log.Debug($"Player realm {GlobalConstants.RealmToName(player.Realm)} wrong realm on attempt to capture relic {relicOnPlayer.Name} of realm {GlobalConstants.RealmToName(relicOnPlayer.Realm)} on pad of realm {GlobalConstants.RealmToName(_parent.Realm)}");
                }
            }
        }
    }
}
