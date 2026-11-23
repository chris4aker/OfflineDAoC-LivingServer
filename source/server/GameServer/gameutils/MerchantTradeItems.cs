using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Reflection;
using System.Threading;
using DOL.Database;

namespace DOL.GS
{
	public enum eMerchantWindowSlot : int
	{
		FirstEmptyInPage = -2,
		Invalid = -1,

		FirstInPage = 0,
		LastInPage = MerchantTradeItems.MAX_ITEM_IN_TRADEWINDOWS - 1,
	}

	/// <summary>
	/// This class represents a full merchant item list
	/// and contains functions that can be used to
	/// add and remove items
	/// </summary>
	public class MerchantTradeItems
	{
		/// <summary>
		/// Defines a logger for this class.
		/// </summary>
		private static readonly Logging.Logger log = Logging.LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);

		/// <summary>
		/// The maximum number of items on one page
		/// </summary>
		public const byte MAX_ITEM_IN_TRADEWINDOWS = 30;

		/// <summary>
		/// The maximum number of pages supported by clients
		/// </summary>
		public const int MAX_PAGES_IN_TRADEWINDOWS = 5;

		#region Constructor/Declaration

		// for client one page is 30 items, just need to use scrollbar to see them all
		// item30 will be on page 0
		// item31 will be on page 1

		/// <summary>
		/// Constructor
		/// </summary>
		/// <param name="itemsListId"></param>
		public MerchantTradeItems(string itemsListId)
		{
			m_itemsListID = itemsListId;
		}

		/// <summary>
		/// Item list id
		/// </summary>
		protected string m_itemsListID;

		/// <summary>
		/// Item list id
		/// </summary>
		public string ItemsListID
		{
			get { return m_itemsListID; }
		}

		/// <summary>
		/// Holds item template instances defined with script
		/// </summary>
		protected HybridDictionary m_usedItemsTemplates = new HybridDictionary();
		private readonly Lock _lock = new();
		private Dictionary<int, DbItemTemplate> _databaseLayout;
		private long _databaseLayoutExpiresTick;
		private const int DatabaseItemsCacheLifetimeMilliseconds = 5 * 60_000;

		#endregion

		#region Add Trade Item

		/// <summary>
		/// Adds an item to the merchant item list
		/// </summary>
		/// <param name="page">Zero-based page number</param>
		/// <param name="slot">Zero-based slot number</param>
		/// <param name="item">The item template to add</param>
		public virtual bool AddTradeItem(int page, eMerchantWindowSlot slot, DbItemTemplate item)
		{
			lock (_lock)
			{
				if (item == null)
				{
					return false;
				}

				eMerchantWindowSlot pageSlot = GetValidSlot(page, slot);

				if (pageSlot == eMerchantWindowSlot.Invalid)
				{
					log.ErrorFormat("Invalid slot {0} specified for page {1} of TradeItemList {2}", slot, page, ItemsListID);
					return false;
				}

				m_usedItemsTemplates[(page*MAX_ITEM_IN_TRADEWINDOWS)+(int)pageSlot] = item;
			}

			return true;
		}

		/// <summary>
		/// Removes an item from trade window
		/// </summary>
		/// <param name="page">Zero-based page number</param>
		/// <param name="slot">Zero-based slot number</param>
		/// <returns>true if removed</returns>
		public virtual bool RemoveTradeItem(int page, eMerchantWindowSlot slot)
		{
			lock (_lock)
			{
				slot = GetValidSlot(page, slot);
				if (slot == eMerchantWindowSlot.Invalid) return false;
				if (!m_usedItemsTemplates.Contains((page*MAX_ITEM_IN_TRADEWINDOWS)+(int)slot)) return false;
				m_usedItemsTemplates.Remove((page*MAX_ITEM_IN_TRADEWINDOWS)+(int)slot);
				return true;
			}
		}

		#endregion

		#region Get Inventory Informations

		/// <summary>One merchant row: its page and slot, the item template it sells and its own row id.</summary>
		public readonly record struct MerchantRow(int Page, int Slot, string TemplateId, string RowId);

		/// <summary>
		/// The window layout of a list's rows: absolute slot (page * 30 + slot) to template id. The world data has rows on the
		/// same page and slot (1,754 copies of an item already in that slot and 182 different items fighting for one, in 50
		/// lists; docs/LOG-ERROR-BACKLOG.md). Rows are read in a stable order (page, slot, row id); the first row in a slot keeps
		/// it; a second copy of an item already in the list is dropped; a different item is moved to the first free slot from its
		/// own page on, so it can still be bought. Rows on a free slot keep it, even if the item is listed twice. Pure.
		/// </summary>
		public static Dictionary<int, string> Layout(IEnumerable<MerchantRow> rows, out int copies, out int moved, out int dropped)
		{
			const int total = MAX_PAGES_IN_TRADEWINDOWS * MAX_ITEM_IN_TRADEWINDOWS;
			var layout = new Dictionary<int, string>();
			var placed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var pending = new List<MerchantRow>();
			copies = moved = dropped = 0;
			foreach (MerchantRow row in rows.OrderBy(r => r.Page).ThenBy(r => r.Slot).ThenBy(r => r.RowId, StringComparer.Ordinal))
			{
				if (row.Page < 0 || row.Page >= MAX_PAGES_IN_TRADEWINDOWS || row.Slot < 0 || row.Slot >= MAX_ITEM_IN_TRADEWINDOWS)
				{
					pending.Add(row);
					continue;
				}
				int absolute = row.Page * MAX_ITEM_IN_TRADEWINDOWS + row.Slot;
				if (!layout.TryGetValue(absolute, out string there))
				{
					layout[absolute] = row.TemplateId;
					placed.Add(row.TemplateId);
				}
				else if (string.Equals(there, row.TemplateId, StringComparison.OrdinalIgnoreCase) || placed.Contains(row.TemplateId))
					copies++;
				else
					pending.Add(row);
			}
			foreach (MerchantRow row in pending)
			{
				if (placed.Contains(row.TemplateId))
				{
					copies++;
					continue;
				}
				int start = Math.Clamp(row.Page, 0, MAX_PAGES_IN_TRADEWINDOWS - 1) * MAX_ITEM_IN_TRADEWINDOWS;
				int free = -1;
				for (int i = 0; i < total && free < 0; i++)
					if (!layout.ContainsKey((start + i) % total))
						free = (start + i) % total;
				if (free < 0)
				{
					dropped++;
					continue;
				}
				layout[free] = row.TemplateId;
				placed.Add(row.TemplateId);
				moved++;
			}
			return layout;
		}

		/// <summary>Lists already reported as tidied, so each says so once per server run.</summary>
		private static readonly ConcurrentDictionary<string, byte> Reported = new(StringComparer.OrdinalIgnoreCase);

		/// <summary>
		/// Merchant rows whose item template is not in the world data. The stock world has about 760 of them, and each was an
		/// error line at every start ("Item template with ID = ... not found for merchant item list ..."; KNOWN-ISSUES 1.2).
		/// Nothing is wrong with the server: the row is skipped and the merchant sells the rest. 1.2.1: every row is still
		/// named once per run, at debug level, and the rows found together are counted in one warning with a few examples.
		/// No data is changed.
		/// </summary>
		public sealed class MissingTemplateLog
		{
			/// <summary>How many rows a summary names.</summary>
			public const int Examples = 5;
			/// <summary>Rows found while lists are read lazily are summed up once the oldest has waited this long.</summary>
			public const int QuietMilliseconds = 5 * 60_000;

			private readonly Lock _gate = new();
			private readonly HashSet<string> _seen = new(StringComparer.OrdinalIgnoreCase);
			private readonly HashSet<string> _pendingLists = new(StringComparer.OrdinalIgnoreCase);
			private readonly List<string> _examples = new();
			private int _pending;
			private long _firstPendingAt;

			/// <summary>Every missing row seen this run.</summary>
			public int Total { get { lock (_gate) return _seen.Count; } }

			/// <summary>Notes a missing row. True the first time it is seen this run (the caller names it at debug level).</summary>
			public bool Add(string listId, string templateId, long now)
			{
				lock (_gate)
				{
					if (!_seen.Add(listId + "|" + templateId))
						return false;
					if (_pending++ == 0)
						_firstPendingAt = now;
					_pendingLists.Add(listId ?? string.Empty);
					if (_examples.Count < Examples)
						_examples.Add(listId + ":" + templateId);
					return true;
				}
			}

			/// <summary>
			/// The warning for the rows noted since the last one, or null: nothing is waiting, or (unless forced) the oldest
			/// has not waited <see cref="QuietMilliseconds"/> yet. The rows it covers are not counted again.
			/// </summary>
			public string Summary(long now, bool force)
			{
				lock (_gate)
				{
					if (_pending == 0 || !force && now - _firstPendingAt < QuietMilliseconds)
						return null;
					string examples = string.Join(", ", _examples);
					string line = $"MERCHANT_ITEMS_MISSING rows={_pending} lists={_pendingLists.Count} this_run={_seen.Count} " +
						$"examples=[{examples}] note=\"world data: these merchant rows name item templates that do not exist; " +
						"the rows are skipped and nothing else is affected; every row is listed at debug level\"";
					_pending = 0;
					_pendingLists.Clear();
					_examples.Clear();
					return line;
				}
			}
		}

		/// <summary>This run's missing merchant item templates.</summary>
		public static MissingTemplateLog MissingTemplates { get; } = new();

		/// <summary>Writes the warning for the missing templates found so far, if any (after a walk over the world's merchants).</summary>
		public static void ReportMissingTemplates(bool force = true)
		{
			string line = MissingTemplates.Summary(GameLoop.GameLoopTime, force);
			if (line != null && log.IsWarnEnabled)
				log.Warn(line);
		}

		/// <summary>The database rows of this list laid out (<see cref="Layout"/>), cached for five minutes.</summary>
		private Dictionary<int, DbItemTemplate> DatabaseLayout()
		{
			lock (_lock)
			{
				long now = GameLoop.GameLoopTime;
				if (_databaseLayout != null && now < _databaseLayoutExpiresTick)
					return _databaseLayout;
				var layout = new Dictionary<int, DbItemTemplate>();
				if (!string.IsNullOrEmpty(m_itemsListID))
				{
					var templates = new Dictionary<string, DbItemTemplate>(StringComparer.OrdinalIgnoreCase);
					var rows = new List<MerchantRow>();
					foreach (DbMerchantItem merchantItem in DOLDB<DbMerchantItem>.SelectObjects(DB.Column("ItemListID").IsEqualTo(m_itemsListID)))
					{
						if (!templates.TryGetValue(merchantItem.ItemTemplateID, out DbItemTemplate template))
						{
							template = GameServer.Database.FindObjectByKey<DbItemTemplate>(merchantItem.ItemTemplateID);
							templates[merchantItem.ItemTemplateID] = template;
							if (template == null && MissingTemplates.Add(m_itemsListID, merchantItem.ItemTemplateID, now) && log.IsDebugEnabled)
								log.DebugFormat("Item template with ID = '{0}' not found for merchant item list '{1}'", merchantItem.ItemTemplateID, ItemsListID);
						}
						if (template != null)
							rows.Add(new MerchantRow(merchantItem.PageNumber, merchantItem.SlotPosition, merchantItem.ItemTemplateID, merchantItem.ObjectId));
					}
					Dictionary<int, string> laidOut = Layout(rows, out int copies, out int moved, out int dropped);
					foreach (KeyValuePair<int, string> placed in laidOut)
						layout[placed.Key] = templates[placed.Value];
					// Was "two merchant items on same page/slot" at error level for every slot on every page view (2,000+ a day).
					if (copies + moved + dropped > 0 && Reported.TryAdd(m_itemsListID, 0))
						log.InfoFormat("MERCHANT_LIST_TIDIED list={0} copies_dropped={1} moved_to_free_slots={2} no_room={3}", m_itemsListID, copies, moved, dropped);
					// Lists are read when first asked for: rows found outside a walk of the world are summed up once they have waited.
					ReportMissingTemplates(force: false);
				}
				_databaseLayout = layout;
				_databaseLayoutExpiresTick = now + DatabaseItemsCacheLifetimeMilliseconds;
				return layout;
			}
		}

		/// <summary>Forget the cached layout, after the list's rows were changed in the database (/merchant articles).</summary>
		public void InvalidateCache()
		{
			lock (_lock)
				_databaseLayout = null;
		}

		/// <summary>
		/// Get the list of all items in the specified page
		/// </summary>
		public virtual IDictionary GetItemsInPage(int page)
		{
			try
			{
				HybridDictionary itemsInPage = new HybridDictionary(MAX_ITEM_IN_TRADEWINDOWS);
				foreach (KeyValuePair<int, DbItemTemplate> item in DatabaseLayout())
				{
					if (item.Key / MAX_ITEM_IN_TRADEWINDOWS == page)
						itemsInPage[item.Key % MAX_ITEM_IN_TRADEWINDOWS] = item.Value;
				}
				lock (_lock)
				{
					foreach (DictionaryEntry de in m_usedItemsTemplates)
					{
						if ((int)de.Key >= (MAX_ITEM_IN_TRADEWINDOWS*page) && (int)de.Key < (MAX_ITEM_IN_TRADEWINDOWS*page+MAX_ITEM_IN_TRADEWINDOWS))
							itemsInPage[(int)de.Key%MAX_ITEM_IN_TRADEWINDOWS] = (DbItemTemplate)de.Value;
					}
				}
				return itemsInPage;
			}
			catch (Exception e)
			{
				if (log.IsErrorEnabled)
					log.Error("Loading merchant items list (" + m_itemsListID + ") page (" + page + "): ", e);
				return new HybridDictionary();
			}
		}

		/// <summary>
		/// Get the item in the specified page and slot
		/// </summary>
		/// <param name="page">The item page</param>
		/// <param name="slot">The item slot</param>
		/// <returns>Item template or null</returns>
		public virtual DbItemTemplate GetItem(int page, eMerchantWindowSlot slot)
		{
			try
			{
				slot = GetValidSlot(page, slot);
				if (slot == eMerchantWindowSlot.Invalid) return null;

				int absolute = (int)slot + (page * MAX_ITEM_IN_TRADEWINDOWS);
				lock (_lock)
				{
					if (m_usedItemsTemplates[absolute] is DbItemTemplate scripted) return scripted;
				}
				// The same layout the window showed, so a moved item is bought where it is shown.
				return DatabaseLayout().TryGetValue(absolute, out DbItemTemplate item) ? item : null;
			}
			catch (Exception e)
			{
				if (log.IsErrorEnabled)
					log.Error("Loading merchant items list (" + m_itemsListID + ") page (" + page + ") slot (" + slot + "): ", e);
				return null;
			}
		}

		/// <summary>
		/// Gets a copy of all intems in trade window
		/// </summary>
		/// <returns>A list where key is the slot position and value is the ItemTemplate</returns>
		public virtual IDictionary GetAllItems()
		{
			try
			{
				Hashtable allItems = new Hashtable();
				foreach (KeyValuePair<int, DbItemTemplate> item in DatabaseLayout())
					allItems[item.Key] = item.Value;
				lock (_lock)
				{
					foreach (DictionaryEntry de in m_usedItemsTemplates)
						allItems[(int)de.Key] = (DbItemTemplate)de.Value;
				}
				return allItems;
			}
			catch (Exception e)
			{
				if (log.IsErrorEnabled)
					log.Error("Loading merchant items list (" + m_itemsListID + "):", e);
				return new HybridDictionary();
			}
		}

		/// <summary>
		/// Check if the slot is valid
		/// </summary>
		/// <param name="page">Zero-based page number</param>
		/// <param name="slot">SlotPosition to check</param>
		/// <returns>the slot if it's valid or eMerchantWindowSlot.Invalid if not</returns>
		public virtual eMerchantWindowSlot GetValidSlot(int page, eMerchantWindowSlot slot)
		{
			if (page < 0 || page >= MAX_PAGES_IN_TRADEWINDOWS) return eMerchantWindowSlot.Invalid;

			if (slot == eMerchantWindowSlot.FirstEmptyInPage)
			{
				IDictionary itemsInPage = GetItemsInPage(page);
				for (int i = (int)eMerchantWindowSlot.FirstInPage; i < (int)eMerchantWindowSlot.LastInPage; i++)
				{
					if (!itemsInPage.Contains(i))
						return ((eMerchantWindowSlot)i);
				}
				return eMerchantWindowSlot.Invalid;
			}

			if (slot < eMerchantWindowSlot.FirstInPage || slot > eMerchantWindowSlot.LastInPage)
				return eMerchantWindowSlot.Invalid;

			return slot;
		}

		#endregion
	}
}
