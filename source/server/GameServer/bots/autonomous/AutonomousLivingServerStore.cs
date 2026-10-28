using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using DOL.Database;
using DOL.Database.Attributes;
using DOL.Logging;

namespace DOL.GS;

/// <summary>A row that can be copied for a detached save (the copy is written; the live row keeps changing).</summary>
public interface ILivingServerRow
{
    DataObject CopyForSave();
}

/// <summary>What an autonomous gamebot remembers about one character it met (Living Server, docs/LIVING-SERVER.md).</summary>
[DataTable(TableName = "offline_bot_memory")]
public sealed class OfflineBotMemoryRecord : DataObject, ILivingServerRow
{
    public DataObject CopyForSave() => (OfflineBotMemoryRecord)MemberwiseClone();

    [DataElement(AllowDbNull = false, Index = true)] public long BotId { get; set; }
    [DataElement(AllowDbNull = false, Varchar = 64)] public string PlayerName { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false, Varchar = 32)] public string PlayerClass { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false)] public int PlayerRealm { get; set; }
    [DataElement(AllowDbNull = false)] public int Grouped { get; set; }
    [DataElement(AllowDbNull = false)] public int Rezzed { get; set; }
    [DataElement(AllowDbNull = false)] public int Abused { get; set; }
    [DataElement(AllowDbNull = false, Varchar = 16)] public string LastTopic { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false, Varchar = 64)] public string LastPlace { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false)] public string FirstMetUtc { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false)] public string LastSeenUtc { get; set; } = string.Empty;
}

/// <summary>An enemy who killed an autonomous gamebot, and how often (Living Server nemesis; the Herald's Most Wanted).</summary>
[DataTable(TableName = "offline_bot_nemesis")]
public sealed class OfflineBotNemesisRecord : DataObject, ILivingServerRow
{
    public DataObject CopyForSave() => (OfflineBotNemesisRecord)MemberwiseClone();

    [DataElement(AllowDbNull = false, Index = true)] public long BotId { get; set; }
    [DataElement(AllowDbNull = false, Varchar = 64)] public string KillerName { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false)] public int KillerRealm { get; set; }
    [DataElement(AllowDbNull = false, Varchar = 32)] public string KillerClass { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false, Varchar = 32)] public string KillerRace { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false)] public bool KillerIsPlayer { get; set; }
    [DataElement(AllowDbNull = false)] public int Kills { get; set; }
    [DataElement(AllowDbNull = false)] public int Revenges { get; set; }
    [DataElement(AllowDbNull = false, Varchar = 64)] public string LastPlace { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false)] public string LastUtc { get; set; } = string.Empty;
}

/// <summary>A server first (first to level 50, first of a class to 50, first to a realm rank).</summary>
[DataTable(TableName = "offline_server_firsts")]
public sealed class OfflineServerFirstRecord : DataObject, ILivingServerRow
{
    public DataObject CopyForSave() => (OfflineServerFirstRecord)MemberwiseClone();

    [DataElement(AllowDbNull = false, Unique = true, Varchar = 64)] public string Title { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false)] public int Realm { get; set; }
    [DataElement(AllowDbNull = false, Varchar = 64)] public string Name { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false, Varchar = 32)] public string ClassName { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false)] public bool IsPlayer { get; set; }
    /// <summary>Empty for a first that was already reached before records began (seeded at start-up, never announced).</summary>
    [DataElement(AllowDbNull = false)] public string AchievedUtc { get; set; } = string.Empty;
}

/// <summary>
/// Rows of one Living Server table, cached in memory and written in batches off the game loop
/// under the shared database write lock (the pattern of the bot guild memberships). A failed batch
/// stays queued and is tried again; nothing here writes on the game loop.
/// </summary>
public sealed class LivingServerTable<TKey, TRow> where TRow : DataObject, ILivingServerRow
{
    private static readonly Logger Log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);
    private const int WriteBatch = 16;
    private readonly object _sync = new();
    private readonly Dictionary<TKey, TRow> _rows;
    private readonly Dictionary<TKey, TRow> _pending;
    private readonly List<TRow> _deletes = new();
    private readonly Func<TRow, TKey> _keyOf;
    private readonly string _name;
    private int _flushing;

    private readonly Func<IObjectDatabase> _database;

    /// <param name="database">The database to write to (tests pass their own; the server's by default).</param>
    public LivingServerTable(string name, Func<TRow, TKey> keyOf, IEqualityComparer<TKey> comparer = null, Func<IObjectDatabase> database = null)
    {
        _database = database ?? (() => GameServer.Database);
        _name = name;
        _keyOf = keyOf;
        _rows = new Dictionary<TKey, TRow>(comparer);
        _pending = new Dictionary<TKey, TRow>(comparer);
    }

    public int Count
    {
        get { lock (_sync) return _rows.Count; }
    }

    public void Load(IEnumerable<TRow> rows)
    {
        lock (_sync)
        {
            _rows.Clear();
            _pending.Clear();
            foreach (TRow row in rows)
                _rows[_keyOf(row)] = row;
        }
    }

    public bool TryGet(TKey key, out TRow row)
    {
        lock (_sync)
            return _rows.TryGetValue(key, out row);
    }

    public TRow[] Where(Func<TRow, bool> predicate)
    {
        lock (_sync)
            return _rows.Values.Where(predicate).ToArray();
    }

    /// <summary>Changes (or creates) a row under the table lock and queues it for writing. Returns the row.</summary>
    public TRow Change(TKey key, Func<TRow> create, Action<TRow> change)
    {
        lock (_sync)
        {
            if (!_rows.TryGetValue(key, out TRow row))
                _rows[key] = row = create();
            change(row);
            row.Dirty = true;
            _pending[key] = row;
            return row;
        }
    }

    /// <summary>Removes every row matching the predicate (queued for deletion). Returns how many.</summary>
    public int RemoveWhere(Func<TRow, bool> predicate)
    {
        lock (_sync)
        {
            var gone = _rows.Where(pair => predicate(pair.Value)).ToList();
            foreach (var pair in gone)
            {
                _rows.Remove(pair.Key);
                _pending.Remove(pair.Key);
                _deletes.Add(pair.Value);
            }
            return gone.Count;
        }
    }

    /// <summary>Removes one row (queued for deletion). False when there was none.</summary>
    public bool Remove(TKey key)
    {
        lock (_sync)
        {
            if (!_rows.Remove(key, out TRow row))
                return false;
            _pending.Remove(key);
            _deletes.Add(row);
            return true;
        }
    }

    /// <summary>Adds a row only if the key is free (used to claim a server first exactly once).</summary>
    public bool TryAdd(TKey key, TRow row)
    {
        lock (_sync)
        {
            if (_rows.ContainsKey(key))
                return false;
            _rows[key] = row;
            _pending[key] = row;
            return true;
        }
    }

    /// <summary>Writes queued rows. With <paramref name="wait"/> (shutdown) it waits for a running flush instead of skipping.</summary>
    public void Flush(bool wait = false)
    {
        while (Interlocked.Exchange(ref _flushing, 1) != 0)
        {
            if (!wait)
                return;
            Thread.Sleep(20);
        }
        try
        {
            // Copies are written, not the live rows, so a change made while a save runs is never marked as
            // saved: the live row only takes the copy's state as its baseline (review 2026-10-16).
            (TKey Key, TRow Live, TRow Copy)[] writes;
            TRow[] deletes;
            lock (_sync)
            {
                if (_pending.Count == 0 && _deletes.Count == 0)
                    return;
                writes = _pending.Select(pair => (pair.Key, pair.Value, (TRow)pair.Value.CopyForSave())).ToArray();
                foreach (var write in writes)
                    write.Copy.Dirty = true;
                deletes = _deletes.ToArray();
                _pending.Clear();
                _deletes.Clear();
            }
            var failedDeletes = new List<TRow>();
            var failed = new List<(TKey Key, TRow Live, TRow Copy)>();
            lock (AutonomousBotStatusPersistence.DatabaseWriteLock)
            {
                foreach (var chunk in writes.Where(write => !write.Copy.IsPersisted).Chunk(WriteBatch))
                    if (!TryWrite(() => _database().AddObject(chunk.Select(write => (DataObject)write.Copy))))
                        failed.AddRange(chunk.Where(write => !write.Copy.IsPersisted));
                foreach (var chunk in writes.Where(write => write.Copy.IsPersisted).Chunk(WriteBatch))
                    if (!TryWrite(() => _database().SaveObject(chunk.Select(write => (DataObject)write.Copy))))
                        failed.AddRange(chunk.Where(write => write.Copy.Dirty));
                foreach (var chunk in deletes.Where(row => row.IsPersisted).Chunk(WriteBatch))
                {
                    foreach (TRow row in chunk)
                        row.AllowDelete = true;
                    if (!TryWrite(() => _database().DeleteObject(chunk.Cast<DataObject>())))
                        failedDeletes.AddRange(chunk.Where(row => row.IsPersisted));
                }
            }
            lock (_sync)
            {
                foreach (var write in writes)
                {
                    if (!write.Copy.IsPersisted || write.Copy.Dirty)
                        continue;
                    write.Live.IsPersisted = true;
                    try
                    {
                        write.Live.AcceptPersistenceBaseline(write.Copy);
                    }
                    catch (ArgumentException)
                    {
                        write.Live.Dirty = true;   // no baseline: the next save writes every column
                    }
                }
                // (A row removed while its first insert runs is queued in _deletes by RemoveWhere and is
                // persisted above, so the next flush deletes it.)
                _deletes.AddRange(failedDeletes);
                foreach (var write in failed)
                    if (_rows.TryGetValue(write.Key, out TRow current) && ReferenceEquals(current, write.Live))
                        _pending.TryAdd(write.Key, write.Live);
            }
            if (failed.Count > 0 || failedDeletes.Count > 0)
                Log.Warn($"Living Server: {failed.Count} {_name} write(s) and {failedDeletes.Count} delete(s) to retry");
        }
        finally
        {
            Volatile.Write(ref _flushing, 0);
        }
    }

    private bool TryWrite(Func<bool> write)
    {
        try
        {
            return write();
        }
        catch (Exception exception)
        {
            Log.Error($"Living Server: {_name} write failed", exception);
            return false;
        }
    }
}
