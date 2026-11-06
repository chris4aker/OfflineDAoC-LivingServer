/*
 * DAWN OF LIGHT - The first free open source DAoC server emulator
 * 
 * This program is free software; you can redistribute it and/or
 * modify it under the terms of the GNU General Public License
 * as published by the Free Software Foundation; either version 2
 * of the License, or (at your option) any later version.
 * 
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 * 
 * You should have received a copy of the GNU General Public License
 * along with this program; if not, write to the Free Software
 * Foundation, Inc., 59 Temple Place - Suite 330, Boston, MA  02111-1307, USA.
 *
 */
/*
 * LightBringer Task System
 * Author:	LightBringer
 * Date:	20040817
*/
/*
 * Suncheck: Bring the /task-System more like real servers
 */

using System.Collections;
using System.Collections.Generic;
using DOL.GS.PacketHandler;
using DOL.GS.Quests;

namespace DOL.GS.Commands
{
	// /task is the period command again (owner 2026-10-31: only the new client is used now). The old client's QUEST
	// GUIDE button sent /task, so /task with nothing targeted opened the Quest Guide here; the guide is /questguide.
	// /task on a named guard asks for a kill task, on another townsperson of your realm for a delivery task (a crafter
	// of your trade for a craft task); /task with nothing (or yourself) targeted shows your task; /task abort gives it
	// up (it fails and still counts for the level).
	[CmdAttribute("&task", ePrivLevel.Player, "Ask the targeted guard or townsperson for a task, or show your current task",
		"/task (target a named guard for a kill task, another townsperson for a delivery task)",
		"/task (nothing targeted) or /task status - your current task", "/task abort")]
	public class TaskCommandHandler : AbstractCommandHandler, ICommandHandler
	{
		public void OnCommand(GameClient client, string[] args)
		{
			GamePlayer player = client.Player;
			if (player == null || IsSpammingCommand(player, "task"))
				return;

			string sub = args.Length > 1 ? args[1].ToLowerInvariant() : string.Empty;
			if (sub == "abort")
			{
				if (player.GameTask is { TaskActive: true })
					player.GameTask.ExpireTask();
				return;
			}

			if (sub != "status" && player.TargetObject is GameNPC giver && giver.Realm == player.Realm)
			{
				if (!player.IsWithinRadius(giver, WorldMgr.WHISPER_DISTANCE))
					player.Out.SendMessage(giver.GetName(0, true) + " is too far away to interact", eChatType.CT_System, eChatLoc.CL_SystemWindow);
				else
					AbstractTask.TryGiveTask(player, giver);
				return;
			}

			ShowStatus(player);
		}

		/// <summary>The period snapshot of your task.</summary>
		private static void ShowStatus(GamePlayer player)
		{
			player.GameTask?.CheckTaskExpired();
			AbstractTask task = player.GameTask;
			int done = task?.TasksDone ?? 0;
			if (task != null && task.TaskActive)
			{
				var messages = new List<string>
				{
					"You are on " + task.Name,
					"What to do: " + task.Description,
					" ",
					"Task will expire at " + task.TimeOut.ToShortTimeString(),
					"You have done " + done + " tasks out of " + AbstractTask.MaxTasksDone(player.Level) + " until now."
				};
				player.Out.SendCustomTextWindow("Tasks (Snapshot)", messages);
			}
			else if (player.Level > AbstractTask.MaxTaskLevel)
				player.Out.SendMessage($"Tasks are only available to players up to level {AbstractTask.MaxTaskLevel}!", eChatType.CT_System, eChatLoc.CL_SystemWindow);
			else if (done >= AbstractTask.MaxTasksDone(player.Level))
				player.Out.SendMessage("You can do no more tasks at your current level", eChatType.CT_System, eChatLoc.CL_SystemWindow);
			else
				player.Out.SendMessage("You have currently no pending task", eChatType.CT_System, eChatLoc.CL_SystemWindow);
		}

		/// <summary>
		/// Execute /Task Command
		/// Same if Player write /Task or press TaskButtom
		/// </summary>
		/// <param name="player">The GamePlayer Object</param>
		/// <returns>True if Command Execute Succesfully</returns>
		/*public static bool TaskCommand(GamePlayer player)
		{
			if (player.Task != null)
				player.Task.CheckTaskExpired();

			if (player.TargetObject == null || player.TargetObject == player)
			{
				//TaskMgr.UpdateDiaryWindow(Player);
				AbstractTask task = player.Task;

				if (task != null && task.TaskActive)
				{
					IList messages = new ArrayList(4);
					messages.Add("You are on " + task.Name);
					messages.Add("What to do: " + task.Description);
					messages.Add(" ");
					messages.Add("Task will expire at " + task.TimeOut.ToShortTimeString());
					messages.Add("You have done " + task.TasksDone + " tasks out of " + AbstractTask.MaxTasksDone(player.Level) + " until now.");
					player.Out.SendCustomTextWindow("Tasks (Snapshot)", messages);
				}
				else if (task != null && task.TasksDone >= AbstractTask.MaxTasksDone(player.Level))
				{
					player.Out.SendMessage("You can do no more tasks at your current level", eChatType.CT_System, eChatLoc.CL_SystemWindow);
				}
				else
				{
					player.Out.SendMessage("You have currently no pending task", eChatType.CT_System, eChatLoc.CL_SystemWindow);
				}
				return true;
			}
			else
			{
				if (WorldMgr.GetDistance(player.X, player.Y, 0, player.TargetObject.X, player.TargetObject.Y, 0) <= WorldMgr.WHISPER_DISTANCE)
					//ToDo: Use follow Line instead of Above when Dol will support Mob with a Z coordinate.
					//foreach(GameNPC mob in WorldMgr.GetNPCsCloseToObject(Player, WorldMgr.WHISPER_DISTANCE))
				{
					if (player.TargetObject is GameLiving)
					{
						if (KillTask.CheckAvailability(player, (GameLiving) player.TargetObject))
						{
							KillTask.BuildTask(player, (GameLiving) player.TargetObject);
							return true;
						}
						else if (MoneyTask.CheckAvailability(player, (GameLiving) player.TargetObject))
						{
							MoneyTask.BuildTask(player, (GameLiving) player.TargetObject);
							return true;
						}
						else if (CraftTask.CheckAvailability(player, (GameLiving) player.TargetObject))
						{
							CraftTask.BuildTask(player, (GameLiving) player.TargetObject);
							return true;
						}
					}
					return false;
				}
				else
				{
					player.Out.SendMessage(player.TargetObject.GetName(0, true) + " is too far away to interact", eChatType.CT_System, eChatLoc.CL_SystemWindow);
					return false;
				}
			}
		}*/
	}

}
