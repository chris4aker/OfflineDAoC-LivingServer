using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DOL.Database;

namespace DOL.GS.ServerProperties
{
	/// <summary>
	/// Living Server Beta 1.1 (the talking update) turns bot voices, bot memory and grats on by default. A world that already
	/// ran an earlier build has rows for them with the old default (False), and the server keeps a stored row as it is, so
	/// the new default would never reach it. Once, on the first start of 1.1, rows the server itself wrote with the old
	/// default (DefaultValue "False", Value "False") are switched on. Rows the launcher wrote (DefaultValue empty) and any
	/// row set to True are left alone. The version row records that it ran.
	/// </summary>
	public static class LivingServerSettingsUpgrade
	{
		private static readonly Logging.Logger log = Logging.LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);

		public const string Version = "1.1";
		public static readonly string[] TurnedOn = ["bot_voices_enabled", "bot_living_memory", "bot_living_grats"];

		/// <summary>The keys to switch on, given the version row's value and each key's stored (Value, DefaultValue). Pure.</summary>
		public static string[] Plan(string doneVersion, IReadOnlyDictionary<string, (string Value, string DefaultValue)> rows)
		{
			if (string.Equals(doneVersion, Version, StringComparison.Ordinal))
				return [];
			return TurnedOn.Where(key => rows.TryGetValue(key, out var row) &&
			                             string.Equals(row.Value, "False", StringComparison.OrdinalIgnoreCase) &&
			                             string.Equals(row.DefaultValue, "False", StringComparison.OrdinalIgnoreCase)).ToArray();
		}

		/// <summary>From InitProperties, after every property is loaded.</summary>
		public static void Apply(IDictionary<string, Tuple<ServerPropertyAttribute, FieldInfo, DbServerProperty>> properties)
		{
			try
			{
				if (!properties.TryGetValue(Properties.LIVING_SERVER_SETTINGS_VERSION_KEY, out var version))
					return;
				var rows = TurnedOn.Where(properties.ContainsKey)
					.ToDictionary(key => key, key => (properties[key].Item3.Value, properties[key].Item3.DefaultValue));
				foreach (string key in Plan(version.Item3.Value, rows))
				{
					var (_, field, row) = properties[key];
					row.Value = "True";
					row.DefaultValue = "True";
					GameServer.Database.SaveObject(row);
					field.SetValue(null, true);
					log.Info($"Living Server {Version}: {key} switched on (the new default; switch it off in the launcher's Living Server tab)");
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
