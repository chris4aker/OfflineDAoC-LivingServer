using System;
using DOL.GS.PacketHandler;

namespace DOL.GS.Commands
{
    [CmdAttribute("&target", ePrivLevel.Player, "target a player by name", "/target <playerName>")]
    public class TargetCommandHandler : AbstractCommandHandler, ICommandHandler
    {
        /// <summary>How far /target looks for an NPC by name (the GM's reach since the command had one).</summary>
        public const ushort NpcRadius = 800;

        /// <summary>The name asked for: every word after the command ("/target Guardian Sergeant"). Pure.</summary>
        public static string AskedName(string[] args) =>
            args == null || args.Length < 2 ? string.Empty : string.Join(' ', args, 1, args.Length - 1).Trim();

        /// <summary>An NPC answers to its whole name, in any case. Pure.</summary>
        public static bool NpcNameMatches(string npcName, string asked) =>
            !string.IsNullOrWhiteSpace(asked) && string.Equals(npcName?.Trim(), asked.Trim(), StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Whether /target may pick an NPC that carries the asked name and stands in reach. A GM picks any, as before.
        /// A player picks one by the rule the command already has for players: not one it could attack (so a keep's
        /// guards for the realm that holds the keep, not for its enemies), not a hidden one, and not one the client
        /// can't select. Before 1.2.1 only a GM could pick an NPC at all, which is why Caer Caledon's guards could be
        /// picked as a GM and not as a player. Pure.
        /// </summary>
        public static bool MayTargetNpc(bool gm, bool alive, bool stealthed, bool cantTarget, bool attackable) =>
            gm || alive && !stealthed && !cantTarget && !attackable;

        private static GameNPC FindNpc(GamePlayer player, string name, bool gm)
        {
            GameNPC best = null;
            int bestDistance = int.MaxValue;

            foreach (GameNPC npc in player.GetNPCsInRadius(NpcRadius))
            {
                if (npc == null || !NpcNameMatches(npc.Name, name))
                    continue;

                if (!MayTargetNpc(gm, npc.IsAlive, npc.IsStealthed, (npc.Flags & GameNPC.eFlags.CANTTARGET) != 0,
                        !gm && GameServer.ServerRules.IsAllowedToAttack(player, npc, true)))
                    continue;

                int distance = player.GetDistanceTo(npc);

                if (distance < bestDistance)
                {
                    best = npc;
                    bestDistance = distance;
                }
            }

            return best;
        }

        public void OnCommand(GameClient client, string[] args)
        {
            if (IsSpammingCommand(client.Player, "target"))
                return;

            if (args.Length > 2)
            {
                // A name of several words is an NPC's: player and bot names are one word.
                string name = AskedName(args);
                bool gm = client.Account.PrivLevel > 1;
                GameNPC npc = name.Length == 0 ? null : FindNpc(client.Player, name, gm);

                if (npc != null)
                {
                    client.Out.SendChangeTarget(npc);
                    client.Out.SendMessage($"{(gm ? "[GM] " : string.Empty)}You target {npc.GetName(0, true)}.", eChatType.CT_System, eChatLoc.CL_SystemWindow);
                    return;
                }

                client.Out.SendMessage($"You don't see {name} around here!", eChatType.CT_System, eChatLoc.CL_SystemWindow);
                return;
            }

            if (args.Length == 2)
            {
                GamePlayer targetPlayer = ClientService.Instance.GetPlayerByPartialName(args[1], out ClientService.PlayerGuessResult result);

                if (result is ClientService.PlayerGuessResult.FOUND_PARTIAL or ClientService.PlayerGuessResult.FOUND_EXACT)
                {
                    if (!client.Player.IsWithinRadius(targetPlayer, WorldMgr.YELL_DISTANCE) || targetPlayer.IsStealthed || GameServer.ServerRules.IsAllowedToAttack(client.Player, targetPlayer, true))
                    {
                        client.Out.SendMessage($"You don't see {args[1]} around here!", eChatType.CT_System, eChatLoc.CL_SystemWindow);
                        return;
                    }

                    client.Out.SendChangeTarget(targetPlayer);
                    client.Out.SendMessage($"You target {targetPlayer.GetName(0, true)}.", eChatType.CT_System, eChatLoc.CL_SystemWindow);
                    return;
                }
                else if (AutonomousBotRegistry.TryGetOnline(args[1], client.Player.Realm, out GameBot bot) &&
                         client.Player.CurrentRegion == bot.CurrentRegion && client.Player.IsWithinRadius(bot, WorldMgr.YELL_DISTANCE) &&
                         !bot.IsStealthed)
                {
                    // 1.2.1: the original client may not select a dead bot; the server remembers it for your resurrection.
                    BotCorpseTargeting.Remember(client.Player, bot);
                    client.Out.SendChangeTarget(bot);
                    client.Out.SendMessage($"You target {bot.GetName(0, true)}.", eChatType.CT_System, eChatLoc.CL_SystemWindow);
                    return;
                }
                else if (BotCorpseTargeting.FindByName(client.Player, args[1], WorldMgr.YELL_DISTANCE) is GameBot deadBot)
                {
                    // A dead /bot companion (not in the gamebot registry), by its full name.
                    BotCorpseTargeting.Remember(client.Player, deadBot);
                    client.Out.SendChangeTarget(deadBot);
                    client.Out.SendMessage($"You target {deadBot.GetName(0, true)}.", eChatType.CT_System, eChatLoc.CL_SystemWindow);
                    return;
                }
                else
                {
                    bool gm = client.Account.PrivLevel > 1;
                    GameNPC npc = FindNpc(client.Player, args[1], gm);

                    if (npc != null)
                    {
                        client.Out.SendChangeTarget(npc);
                        client.Out.SendMessage($"{(gm ? "[GM] " : string.Empty)}You target {npc.GetName(0, true)}.", eChatType.CT_System, eChatLoc.CL_SystemWindow);
                        return;
                    }
                }

                client.Out.SendMessage($"You don't see {args[1]} around here!", eChatType.CT_System, eChatLoc.CL_SystemWindow);
                return;
            }

            if (client.Account.PrivLevel > 1)
                client.Out.SendMessage("/target <player/mobname>", eChatType.CT_System, eChatLoc.CL_SystemWindow);
            else
                client.Out.SendMessage("/target <player or friendly NPC name>", eChatType.CT_System, eChatLoc.CL_SystemWindow);
        }
    }
}
