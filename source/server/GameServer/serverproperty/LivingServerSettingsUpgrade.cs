using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using DOL.Database;

namespace DOL.GS.ServerProperties
{
	/// <summary>
	/// New Living Server defaults for worlds that already ran an earlier build. The server keeps a stored row as it is, so a
	/// changed default would never reach such a world. Once per build, on its first start, rows the server itself wrote with
	/// the old default and never changed (Value and DefaultValue both the old default) get the new one. Rows the launcher
	/// wrote (DefaultValue empty) and rows with any other value are the player's choice and are left alone. The version row
	/// records the last build whose changes ran.
	/// - Beta 1.1 (the talking update): bot voices, bot memory and grats on.
	/// - Beta 1.1.2 (a r/daoc report, owner 2026-11-03): power regeneration 1.5 times the classic rate, and no halving below
	///   50% power, for players and bots alike.
	/// - Beta 1.1.2, raid sizing revision (PR #75, 2026-11-05): the win-rate sizing defaults changed between test builds
	///   (step 5 -> 10, largest 100 or 150 -> 200, smallest 60 -> 70), and a test copy kept the old step from its first
	///   build. "1.1.2.1" because worlds that ran any 1.1.2 build have already recorded "1.1.2".
	/// - 1.2 (owner 2026-11-12): a realm's automatic raids every 240 minutes instead of 150 (the tale, then the call).
	/// - 1.2, every feature on (owner 2026-11-16: "all our living server features should be in by default if someone
	///   downloads our installer"): bot guilds, personalities, the Server life parts that were off (firsts, death, alarms,
	///   nemesis, breaks, body language), the real-clock bot schedule, and Crafting 2.0's gamebot side (crafters, craft
	///   orders, upkeep, luxury spending, siege supply, commerce, crafting social life). "1.2.0.1" because worlds that ran
	///   an earlier 1.2 build have already recorded "1.2". A player who had switched one of these on and back off in the
	///   launcher, on a row the server wrote, can't be told from an untouched row (the launcher kept the server's
	///   DefaultValue until 1.2): that row moves once, and the player's next choice in the tab is kept from then on, because
	///   the launcher now clears DefaultValue on every save.
	/// - 1.2, Galladoria's Earthmenders (owner 2026-11-18, "Mutual heal off"): they no longer heal each other by default.
	///   "1.2.0.2" because worlds that ran an earlier 1.2 build have already recorded "1.2.0.1".
	/// - 1.2.1 (owner 2026-11-21: "set the server settings so the 'bot schedule' based on time or sessions is off by
	///   default"): the bot schedule is off again, so the whole roster is online. A world whose row the server wrote as clock
	///   (1.2.0.1's change, or a fresh 1.2 world) moves to off; a mode the player chose in the launcher is kept.
	/// - 1.2.3 (owner 2026-11-24, #598 option A): the Bot Goals default shares. Levels 1-19 go from 60/40/0/0 to 55/35/0/10
	///   (solo/group/RvR/battlegrounds) and levels 20-49 from 35/35/15/15 to 30/30/15/25. They live in a file
	///   (bot-goals.json), not a database row: a world whose saved file still holds exactly the 1.2.2 defaults moves once
	///   (<see cref="FileChanges"/>); a file with any other values is the player's choice and is left alone, and a world with
	///   no file already uses the new defaults.
	/// A key can change in more than one build. The changes are applied in order, each one seeing the rows as the earlier
	/// ones left them, so a world that skipped builds ends on the newest default (a 1.1.4 world's untouched off schedule
	/// goes off -> clock -> off and so stays off).
	/// </summary>
	public static class LivingServerSettingsUpgrade
	{
		private static readonly Logging.Logger log = Logging.LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);

		/// <summary>The newest build with a changed default (the highest <see cref="Change.Since"/>).</summary>
		public const string Version = "1.2.3";

		/// <summary>One changed default: since which build, the setting, and its old and new default as the server stores them.</summary>
		public sealed record Change(string Since, string Key, string OldDefault, string NewDefault);

		// One line per changed default, oldest build first.
		public static readonly Change[] Changes =
		[
			new("1.1", "bot_voices_enabled", "False", "True"),
			new("1.1", "bot_living_memory", "False", "True"),
			new("1.1", "bot_living_grats", "False", "True"),
			new("1.1.2", "mana_regen_amount_modifier", "1", "1.5"),
			new("1.1.2", "mana_regen_amount_halved_below_50_percent", "True", "False"),
			// Owner 2026-11-04, after the playtest: at 900 bots a realm, 1 gave 6.4 lines a minute in Albion mid-morning.
			new("1.1.2", "bot_voices_chat_rate", "1", "2"),
			// Raid win-rate sizing (RealmRaidRecruitmentPolicy), every default an earlier test build wrote. Unchanged since
			// they first shipped: realm_raid_target_win_percent (67) and realm_raid_start_size_percent (125); the loss step
			// is derived from the win step, not a setting.
			new("1.1.2.1", "realm_raid_win_trim_percent", "5", "10"),
			new("1.1.2.1", "realm_raid_largest_size_percent", "100", "200"),
			new("1.1.2.1", "realm_raid_largest_size_percent", "150", "200"),
			new("1.1.2.1", "realm_raid_smallest_size_percent", "60", "70"),
			// 1.2 (owner 2026-11-12): every raid has its tale and its call, so a realm's raids come every four hours, not 2.5.
			new("1.2", "realm_raid_schedule_minutes", "150", "240"),
			// 1.2 (owner 2026-11-16): every Living Server feature on by default. Diagnostics, the classic crafting rules, the
			// epic encounter options and the Sluaghbinder edition switch keep their values (docs/CRAFTING.md, AGENTS.md).
			new("1.2.0.1", "bot_guilds_enabled", "False", "True"),
			new("1.2.0.1", "bot_personalities_enabled", "False", "True"),
			new("1.2.0.1", "bot_living_firsts", "False", "True"),
			new("1.2.0.1", "bot_living_death", "False", "True"),
			new("1.2.0.1", "bot_living_alarms", "False", "True"),
			new("1.2.0.1", "bot_living_nemesis", "False", "True"),
			new("1.2.0.1", "bot_living_breaks", "False", "True"),
			new("1.2.0.1", "bot_living_body_language", "False", "True"),
			new("1.2.0.1", "bot_schedule_mode", "off", "clock"),
			new("1.2.0.1", "bot_crafters", "False", "True"),
			new("1.2.0.1", "bot_craft_orders", "False", "True"),
			new("1.2.0.1", "bot_upkeep", "False", "True"),
			new("1.2.0.1", "bot_luxury_spending", "False", "True"),
			new("1.2.0.1", "bot_siege_supply", "False", "True"),
			new("1.2.0.1", "bot_crafter_commerce", "False", "True"),
			new("1.2.0.1", "bot_crafting_social", "False", "True"),
			// 1.2 (owner 2026-11-18, "Mutual heal off"): with the Earthmenders healing each other the bot raid can't kill them
			// (build b1b6846: 1.44M damage in 48 minutes, none below 99%). The class puzzle and the other epic options stay.
			new("1.2.0.2", "epic_earthmender_mutual_heal", "True", "False"),
			// 1.2.1 (owner 2026-11-21: "set the server settings so the 'bot schedule' based on time or sessions is off by
			// default"): the whole roster online, no time-of-day or session schedule. bot_living_breaks is a separate feature
			// and stays on.
			new("1.2.1", "bot_schedule_mode", "clock", "off"),
			// 1.2.3 (owner 2026-11-25, #641: the product is "DAoC - Living Server"): the welcome messages name it. A world
			// whose messages were edited keeps its own text.
			new("1.2.3", "motd", "Welcome back to Offline DAoC. Adventure alone or alongside autonomous player bots across the three realms.", "Welcome back to DAoC - Living Server. Adventure alone or alongside autonomous player bots across the three realms."),
			new("1.2.3", "starting_msg", "Welcome to Offline DAoC, a private Classic and Shrouded Isles world built to play alone or with player bot companions.", "Welcome to DAoC - Living Server, a private Classic and Shrouded Isles world built to play alone or with player bot companions."),
		];

		/// <summary>A changed default kept in a settings file rather than a database row: since which build, and the file.</summary>
		public sealed record FileChange(string Since, string File, string Description);

		/// <summary>One line per changed file default, oldest build first. Each runs once, on a world that recorded an older build.</summary>
		public static readonly FileChange[] FileChanges =
		[
			// 1.2.3 (#598 option A): 1-19 60/40/0/0 -> 55/35/0/10, 20-49 35/35/15/15 -> 30/30/15/25 (BotGoalSettings).
			new("1.2.3", DAoCLivingServer.Configuration.BotGoalSettings.FileName, "Bot Goals default shares (battlegrounds 10% at 15-19, 25% at 20-35)"),
		];

		/// <summary>The file changes still to run on a world that recorded <paramref name="doneVersion"/>. Pure.</summary>
		public static FileChange[] PlanFileChanges(string doneVersion)
		{
			System.Version done = Parse(doneVersion);
			return FileChanges.Where(change => Parse(change.Since) > done).ToArray();
		}

		/// <summary>The 1.1 keys (switched on), kept for callers of the first version.</summary>
		public static readonly string[] TurnedOn = Changes.Where(change => change.Since == "1.1").Select(change => change.Key).ToArray();

		/// <summary>
		/// The changes to apply, in order, given the version row's value and each key's stored (Value, DefaultValue). Pure. A
		/// build is applied when it is newer than the recorded one; an empty or unreadable record means none has run yet.
		/// Each change is checked against a working copy of the rows as the earlier planned changes left them (a planned
		/// change sets that key's Value and DefaultValue to its new default), so a key changed by two builds can be planned
		/// twice; its last planned change gives its final value.
		/// </summary>
		public static Change[] PlanChanges(string doneVersion, IReadOnlyDictionary<string, (string Value, string DefaultValue)> rows)
		{
			System.Version done = Parse(doneVersion);
			// The same key matching as the rows given (the server's own dictionary may ignore case).
			var working = new Dictionary<string, (string Value, string DefaultValue)>(
				rows is Dictionary<string, (string Value, string DefaultValue)> given ? given.Comparer : StringComparer.Ordinal);
			foreach (var stored in rows)
				working[stored.Key] = stored.Value;
			var planned = new List<Change>();
			foreach (Change change in Changes)
			{
				if (Parse(change.Since) <= done || !working.TryGetValue(change.Key, out var row) ||
				    !SameStoredValue(row.Value, change.OldDefault) || !SameStoredValue(row.DefaultValue, change.OldDefault))
					continue;
				planned.Add(change);
				working[change.Key] = (change.NewDefault, change.NewDefault);
			}
			return planned.ToArray();
		}

		/// <summary>The keys <see cref="PlanChanges"/> would change, each once, in the order first planned. Pure.</summary>
		public static string[] Plan(string doneVersion, IReadOnlyDictionary<string, (string Value, string DefaultValue)> rows) =>
			PlanChanges(doneVersion, rows).Select(change => change.Key).Distinct().ToArray();

		/// <summary>The same stored setting: equal text ignoring case, or the same number ("1" and "1.0").</summary>
		public static bool SameStoredValue(string stored, string expected)
		{
			if (stored == null || expected == null) return false;
			if (string.Equals(stored.Trim(), expected, StringComparison.OrdinalIgnoreCase)) return true;
			return double.TryParse(stored.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double a) &&
			       double.TryParse(expected, NumberStyles.Float, CultureInfo.InvariantCulture, out double b) && a == b;
		}

		private static System.Version Parse(string version) =>
			System.Version.TryParse(version?.Trim(), out System.Version parsed) ? parsed : new System.Version(0, 0);

		/// <summary>From InitProperties, after every property is loaded.</summary>
		public static void Apply(IDictionary<string, Tuple<ServerPropertyAttribute, FieldInfo, DbServerProperty>> properties)
		{
			try
			{
				if (!properties.TryGetValue(Properties.LIVING_SERVER_SETTINGS_VERSION_KEY, out var version))
					return;
				var rows = Changes.Select(change => change.Key).Distinct().Where(properties.ContainsKey)
					.ToDictionary(key => key, key => (properties[key].Item3.Value, properties[key].Item3.DefaultValue));
				var provider = new CultureInfo("en-US", false).NumberFormat;
				// One write and one log line per key: a key planned by more than one build takes its last change's new default.
				foreach (var planned in PlanChanges(version.Item3.Value, rows).GroupBy(change => change.Key))
				{
					Change first = planned.First(), last = planned.Last();
					var (attribute, field, row) = properties[planned.Key];
					string builds = string.Join(", ", planned.Select(change => change.Since));
					if (SameStoredValue(row.Value, last.NewDefault) && SameStoredValue(row.DefaultValue, last.NewDefault))
					{
						// e.g. a 1.1.4 world's untouched schedule: off -> clock (1.2.0.1) -> off (1.2.1), already the newest default.
						log.Info($"Living Server {builds}: {planned.Key} keeps {last.NewDefault}, already the newest default");
						continue;
					}
					object value = Convert.ChangeType(last.NewDefault, attribute.DefaultValue.GetType(), provider);
					row.Value = last.NewDefault;
					row.DefaultValue = last.NewDefault;
					GameServer.Database.SaveObject(row);
					field.SetValue(null, value);
					log.Info($"Living Server {builds}: {planned.Key} set to the new default {last.NewDefault} (was the untouched old default " +
					         $"{first.OldDefault}; change it in the launcher's Living Server tab)");
				}
				foreach (FileChange change in PlanFileChanges(version.Item3.Value))
				{
					if (change.File == DAoCLivingServer.Configuration.BotGoalSettings.FileName && AutonomousBotGoalPolicy.UpgradeUntouchedDefaults())
						log.Info($"Living Server {change.Since}: {change.File} set to the new defaults ({change.Description}; the file held " +
						         "the untouched old defaults; change it in the launcher's Bot Goals Setting tab)");
				}
				if (version.Item3.Value != Version)
				{
					version.Item3.Value = Version;
					GameServer.Database.SaveObject(version.Item3);
					version.Item2.SetValue(null, Version);
				}
			}
			catch (Exception exception)
			{
				log.Error("Living Server settings upgrade failed; settings keep their stored values", exception);
			}
		}
	}
}
