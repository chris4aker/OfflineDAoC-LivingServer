using System;
using System.Collections.Generic;

namespace DOL.GS
{
	/// <summary>
	/// Loot rows whose item template is not in the world data (1.2.1). The loot generators used to write one error line per
	/// row at every start ("ItemTemplate: X is not found, it is referenced from loottemplate: Y" and the same for
	/// DropTemplateXItemTemplate and the one-time drops). Nothing is wrong with the server: the row is skipped and the
	/// monster drops the rest. Now every row is named at debug level and the rows of one load are counted in one warning
	/// with a few examples, like MERCHANT_ITEMS_MISSING. No data is changed.
	/// </summary>
	public sealed class LootMissingItems
	{
		/// <summary>How many rows a summary names.</summary>
		public const int Examples = 5;

		private readonly string _source;
		private readonly HashSet<string> _seen = new(StringComparer.OrdinalIgnoreCase);
		private readonly HashSet<string> _templates = new(StringComparer.OrdinalIgnoreCase);
		private readonly HashSet<string> _items = new(StringComparer.OrdinalIgnoreCase);
		private readonly List<string> _examples = new();

		/// <param name="source">The table the rows come from (LootTemplate, DropTemplateXItemTemplate, LootOTD).</param>
		public LootMissingItems(string source)
		{
			_source = source ?? string.Empty;
		}

		/// <summary>Rows noted (each template and item pair once).</summary>
		public int Rows => _seen.Count;

		/// <summary>Notes a row. True the first time the pair is seen (the caller names it at debug level).</summary>
		public bool Add(string templateName, string itemTemplateId)
		{
			templateName ??= string.Empty;
			itemTemplateId ??= string.Empty;
			if (!_seen.Add(templateName + "|" + itemTemplateId))
				return false;
			_templates.Add(templateName);
			_items.Add(itemTemplateId);
			if (_examples.Count < Examples)
				_examples.Add(templateName + ":" + itemTemplateId);
			return true;
		}

		/// <summary>The warning for the rows noted, or null when none were.</summary>
		public string Summary()
		{
			if (_seen.Count == 0)
				return null;
			return $"LOOT_ITEMS_MISSING source={_source} rows={_seen.Count} templates={_templates.Count} items={_items.Count} " +
				$"examples=[{string.Join(", ", _examples)}] note=\"world data: these loot rows name item templates that do not " +
				"exist; the rows are skipped and the monster drops the rest; every row is listed at debug level\"";
		}
	}
}
