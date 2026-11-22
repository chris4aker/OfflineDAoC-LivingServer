using System.Reflection;
using DOL.Logging;

namespace DOL.GS.PacketHandler.Client.v168
{
    [PacketHandlerAttribute(PacketHandlerType.TCP, eClientPackets.PlayerTarget, "Handle Player Target Change.", eClientStatus.PlayerInGame)]
    public class PlayerTargetHandler : PacketHandler
    {
        private static readonly Logger log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);

        protected override void HandlePacketInternal(GameClient client, GSPacketIn packet)
        {
            ushort targetId = packet.ReadShort();
            ushort flags = packet.ReadShort();

            /*
             * 0x8000 = 'examine' bit
             * 0x4000 = LOS1 bit; is 0 if no LOS
             * 0x2000 = LOS2 bit; is 0 if no LOS
             * 0x0001 = players attack mode bit (not targets!)
             */

            ChangeTarget(client.Player, targetId, (flags & (0x4000 | 0x2000)) != 0, (flags & 0x8000) != 0);
        }

        private static void ChangeTarget(GamePlayer actionSource, ushort newTargetId, bool targetInView, bool examineTarget)
        {
            GameObject target = actionSource.CurrentRegion.GetObject(newTargetId);
            bool inRange = newTargetId == 0 || actionSource.IsWithinRadius(target, WorldMgr.VISIBILITY_DISTANCE);

            // 1.2.1 (#424): a click on a bot, as the server received it. No line after a click in the original client means
            // the client never sent the target (its own picking, or the tool that clicked); a line with result=set means
            // the server took the target.
            if (log.IsInfoEnabled && ClickLogLine(actionSource.Name, newTargetId, target, inRange, targetInView, examineTarget) is { } line)
                log.Info(line);

            if (newTargetId > 0 && !inRange)
            {
                actionSource.Out.SendObjectDelete(newTargetId);
                target = null;
            }

            actionSource.TargetObject = target;
            actionSource.TargetInView = targetInView;

            if (target != null)
            {
                // Send target message text only if 'examine' bit is set.
                if (examineTarget)
                {
                    foreach (string message in target.GetExamineMessages(actionSource))
                        actionSource.Out.SendMessage(message, eChatType.CT_Missed, eChatLoc.CL_SystemWindow);
                }

                // No LOS message. Not sure which bit to use so use both.
                if (!targetInView)
                    actionSource.Out.SendMessage("Target is not in view.", eChatType.CT_System, eChatLoc.CL_SystemWindow);

                if (target is not GamePlayer)
                    ClientService.UpdateObjectForPlayer(actionSource, target);

                // Unstealth if anything is targeted while we're in combat mode. Exclude archers as they should be allowed to change target without losing stealth.
                if (actionSource.ActiveWeaponSlot is not eActiveWeaponSlot.Distance && actionSource.IsAttacking && actionSource.IsStealthed)
                    actionSource.Stealth(false);
            }

            if (actionSource.IsPraying)
            {
                if (target is not GameGravestone gravestone || !gravestone.InternalID.Equals(actionSource.InternalID))
                {
                    actionSource.Out.SendMessage("You are no longer targeting your grave. Your prayers fail.", eChatType.CT_System, eChatLoc.CL_SystemWindow);
                    actionSource.PrayTimerStop();
                }
            }
        }

        /// <summary>
        /// 1.2.1 (#424): the log line for a click target (the client's PlayerTarget packet; /target is a command and not
        /// logged here): only for a bot (an autonomous gamebot or a companion) and for an object id the region doesn't
        /// know; null for anything else, so ordinary clicks on monsters and players stay out of the log. Pure.
        /// </summary>
        public static string ClickLogLine(string player, ushort targetId, GameObject target, bool inRange, bool inView, bool examine)
        {
            if (targetId == 0)
                return null;
            string kind = target switch
            {
                null => "unknown_id",
                GameBot { IsAutonomousWorldBot: true } => "gamebot",
                GameBot => "companion",
                _ => null,
            };
            if (kind == null)
                return null;
            string result = target == null ? "no_object" : !inRange ? "out_of_range" : "set";
            string alive = target is GameLiving living ? (living.IsAlive ? " alive=True" : " alive=False") : string.Empty;
            return $"PLAYER_CLICK_TARGET player=\"{player}\" id={targetId} kind={kind} name=\"{target?.Name}\" result={result}{alive} " +
                   $"inView={inView} examine={examine}";
        }
    }
}
