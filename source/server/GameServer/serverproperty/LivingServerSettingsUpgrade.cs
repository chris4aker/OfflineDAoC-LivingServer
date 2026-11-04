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
	/// </summary>
	public static class LivingServerSettingsUpgrade
	{
		private static readonly Logging.Logger log = Logging.LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);

		/// <summary>The newest build with a changed default (the highest <see cref="Change.Since"/>).</summary>
		public const string Version = "1.1.2";

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
		];

		/// <summary>The 1.1 keys (switched on), kept for callers of the first version.</summary>
		public static readonly string[] TurnedOn = Changes.Where(change => change.Since == "1.1").Select(change => change.Key).ToArray();

		/// <summary>
		/// The changes to apply, given the version row's value and each key's stored (Value, DefaultValue). Pure. A build is
		/// applied when it is newer than the recorded one; an empty or unreadable record means none has run yet.
		/// </summary>
		public static Change[] PlanChanges(string doneVersion, IReadOnlyDictionary<string, (string Value, string DefaultValue)> rows)
		{
			System.Version done = Parse(doneVersion);
			return Changes.Where(change => Parse(change.Since) > done &&
			                               rows.TryGetValue(change.Key, out var row) &&
			                               SameStoredValue(row.Value, change.OldDefault) &&
			                               SameStoredValue(row.DefaultValue, change.OldDefault)).ToArray();
		}

		/// <summary>The keys <see cref="PlanChanges"/> would change. Pure.</summary>
		public static string[] Plan(string doneVersion, IReadOnlyDictionary<string, (string Value, string DefaultValue)> rows) =>
			PlanChanges(doneVersion, rows).Select(change => change.Key).ToArray();

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
				foreach (Change change in PlanChanges(version.Item3.Value, rows))
				{
					var (attribute, field, row) = properties[change.Key];
					object value = Convert.ChangeType(change.NewDefault, attribute.DefaultValue.GetType(), provider);
					row.Value = change.NewDefault;
					row.DefaultValue = change.NewDefault;
					GameServer.Database.SaveObject(row);
					field.SetValue(null, value);
					log.Info($"Living Server {change.Since}: {change.Key} set to the new default {change.NewDefault} (was the untouched old default " +
					         $"{change.OldDefault}; change it in the launcher's Living Server tab)");
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
