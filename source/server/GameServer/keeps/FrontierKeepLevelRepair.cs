using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DOL.Database;

namespace DOL.GS.Keeps
{
	/// <summary>
	/// Issue #526, owner decision 2026-11-24: Dun nGed, Dun Da Behnn and Dun Ailinne (level 1) and Dun Crauchon (level 4) go
	/// to level 5, the level of every other claimable frontier keep. Until 1.2.3 the shipped world had them lower
	/// (<c>source/tools/release-world-defaults.json</c>, siege.md 9): their doors were a fifth (level 1) or four fifths
	/// (level 4) of a level-5 keep's, and their guards and lord lower.
	/// <para>A world built from 1.2.3 ships them at 5. A world that ran an earlier build is repaired once, in the
	/// "untouched old default" way of <see cref="ServerProperties.LivingServerSettingsUpgrade"/>: a keep is raised only while
	/// it still holds the level the earlier build shipped and no guild holds it, so a level a player changed (a claim and its
	/// upgrades, a GM's /keep) is left alone. The doors of a keep at level 5 that still hold the old level's full health (the
	/// shipped door rows) are made whole at level 5; a door already hurt on a keep this repair raises keeps its share of
	/// health. The repair is recorded in <c>living_server_world_repairs</c> and never runs again, so a later capture that
	/// puts Crauchon back to starting_keep_level (4) is not undone. It only raises: no keep, door, guard or lord is lowered
	/// (AGENTS.md).</para>
	/// </summary>
	public static class FrontierKeepLevelRepair
	{
		private static readonly Logging.Logger log = Logging.LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);

		/// <summary>The name in <c>living_server_world_repairs</c>.</summary>
		public const string RepairName = "hib-keep-levels-1.2.3";

		public const string LogTag = "KEEP_LEVEL_DEFAULT_RAISED";

		/// <summary>The level every claimable frontier keep ships at.</summary>
		public const byte FrontierKeepLevel = 5;

		/// <summary>One keep the earlier builds shipped low: its id, name, the level shipped up to 1.2.2 and the level now.</summary>
		public sealed record Raise(int KeepId, string Name, byte OldLevel, byte NewLevel);

		public static readonly Raise[] Raises =
		[
			new(100, "Dun Crauchon", 4, FrontierKeepLevel),
			new(103, "Dun nGed", 1, FrontierKeepLevel),
			new(104, "Dun Da Behnn", 1, FrontierKeepLevel),
			new(106, "Dun Ailinne", 1, FrontierKeepLevel),
		];

		/// <summary>Whether this world's repair list already has the repair. Pure.</summary>
		public static bool AlreadyRan(string repairs) =>
			(repairs ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
			.Contains(RepairName, StringComparer.OrdinalIgnoreCase);

		/// <summary>The repair list with this repair added. Pure.</summary>
		public static string WithRepair(string repairs) =>
			AlreadyRan(repairs) ? repairs : string.IsNullOrWhiteSpace(repairs) ? RepairName : repairs.Trim() + "," + RepairName;

		/// <summary>
		/// The level the keep goes to, or null when it stays: only the old shipped level, unclaimed, is raised. Pure.
		/// </summary>
		public static byte? TargetLevel(Raise raise, byte level, bool claimed) =>
			raise != null && !claimed && level == raise.OldLevel && raise.NewLevel > level ? raise.NewLevel : null;

		/// <summary>A keep door's full health at this base level and keep level (<c>MaxHealthCalculator</c>). Pure.</summary>
		public static int DoorMaxHealth(int baseLevel, int keepLevel, int doorBaseHealth, double upgradeModifier)
		{
			int health = baseLevel * doorBaseHealth;
			return health + (int)(health * (keepLevel - 1) * upgradeModifier);
		}

		/// <summary>
		/// A door's health after the repair. A door at exactly the old level's full health (as shipped) is made whole at the
		/// new level; a hurt door of a keep raised now keeps its share; any other door is left as it is. Never lower. Pure.
		/// </summary>
		public static int DoorHealthAfter(int health, int oldFullHealth, int newFullHealth, bool keepRaisedNow)
		{
			if (oldFullHealth <= 0 || newFullHealth <= oldFullHealth || health <= 0)
				return health;
			if (health == oldFullHealth)
				return newFullHealth;
			if (keepRaisedNow && health < oldFullHealth)
				return Math.Max(health, Math.Min(newFullHealth, (int)Math.Ceiling(health * (double)newFullHealth / oldFullHealth)));
			return health;
		}

		public static string RaisedLine(Raise raise, AbstractGameKeep keep, byte was, int doorsFixed) =>
			$"{LogTag} keep=\"{raise.Name}\" id={raise.KeepId} realm={keep.Realm} level={was}->{keep.Level} doors={doorsFixed} " +
			"reason=old-shipped-default";

		/// <summary>From the server start, after the keeps and their doors are loaded. Never stops the start.</summary>
		public static bool Run()
		{
			try
			{
				if (AlreadyRan(ServerProperties.Properties.LIVING_SERVER_WORLD_REPAIRS))
					return true;
				IKeepManager keeps = GameServer.KeepManager;
				if (keeps == null)
					return true;
				foreach (Raise raise in Raises)
				{
					AbstractGameKeep keep = keeps.GetKeepByID(raise.KeepId);
					if (keep == null || keep is GameKeepTower || keep.IsRelic)
					{
						log.Info($"{LogTag} keep=\"{raise.Name}\" id={raise.KeepId}: not in this world; nothing to do");
						continue;
					}
					RepairKeep(raise, keep);
				}
				MarkDone();
			}
			catch (Exception exception)
			{
				log.Error($"{LogTag} the keep levels could not be repaired; tried again next start", exception);
			}
			return true;
		}

		private static void RepairKeep(Raise raise, AbstractGameKeep keep)
		{
			byte was = keep.Level;
			int oldFull = DoorMaxHealth(keep.BaseLevel, raise.OldLevel, ServerProperties.Properties.KEEP_DOORS_BASE_HEALTH,
				ServerProperties.Properties.KEEP_DOORS_HEALTH_UPGRADE_MODIFIER);
			List<(GameKeepDoor Door, int Health)> doors = keep.Doors.Values.Select(door => (door, door.Health)).ToList();
			byte? target = TargetLevel(raise, keep.Level, keep.Guild != null);
			if (target != null)
				keep.ChangeLevel(target.Value);
			int fixedDoors = 0;
			if (keep.Level == raise.NewLevel)
			{
				foreach ((GameKeepDoor door, int health) in doors)
				{
					int after = DoorHealthAfter(health, oldFull, door.MaxHealth, target != null);
					if (after == door.Health)
						continue;
					door.Health = after;
					door.SaveIntoDatabase();
					fixedDoors++;
				}
			}
			if (target != null || fixedDoors > 0)
				log.Info(RaisedLine(raise, keep, was, fixedDoors));
			else
				log.Info($"{LogTag} keep=\"{raise.Name}\" id={raise.KeepId} level={was} claimed={keep.Guild != null}: " +
					"not the old shipped level, left as it is");
		}

		private static void MarkDone()
		{
			string done = WithRepair(ServerProperties.Properties.LIVING_SERVER_WORLD_REPAIRS);
			DbServerProperty row = DOLDB<DbServerProperty>.SelectObject(DB.Column("Key").IsEqualTo(ServerProperties.Properties.LIVING_SERVER_WORLD_REPAIRS_KEY));
			if (row != null)
			{
				row.Value = done;
				GameServer.Database.SaveObject(row);
			}
			ServerProperties.Properties.LIVING_SERVER_WORLD_REPAIRS = done;
		}
	}
}
