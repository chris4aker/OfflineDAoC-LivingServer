using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OfflineDaoc.Passport;

/// <summary>One database row, by column name.</summary>
public sealed class Row : Dictionary<string, object?>
{
    public Row() : base(StringComparer.OrdinalIgnoreCase) { }
    public Row(IDictionary<string, object?> values) : base(values, StringComparer.OrdinalIgnoreCase) { }

    public string Text(string column) => TryGetValue(column, out object? value) ? Convert.ToString(value) ?? "" : "";
}

public sealed record Manifest
{
    public const int CurrentFormat = 1;
    public const string Outbound = "outbound", Return = "return";

    public int Format { get; init; } = CurrentFormat;
    public required string TripId { get; init; }
    public required string CharacterId { get; init; }
    public required string Name { get; init; }
    public required int Realm { get; init; }
    public required int Class { get; init; }
    public required int Level { get; init; }
    public required string Direction { get; init; }
    public string FromWorld { get; init; } = "";
    public required long PurseCopper { get; init; }
    public string CraftingSkills { get; init; } = "";
    public int CraftingPrimarySkill { get; init; }
    public required string CreatedUtc { get; init; }
    public string PayloadSha256 { get; set; } = "";
}

/// <summary>A character and what it carries, in memory.</summary>
public sealed class PassportData
{
    public required Manifest Manifest { get; set; }

    /// <summary>Rows by table, one list per <see cref="PassportTables.Travelling"/> table.</summary>
    public Dictionary<string, List<Row>> Tables { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public List<Row> ItemUnique { get; init; } = [];
    public List<Row> ItemTemplate { get; init; } = [];
}

/// <summary>
/// Reads and writes the .odpassport zip. The checksum catches damage in transit; it is not a seal.
/// Friends' worlds are trusted, as with a GM.
/// </summary>
public static class PassportFile
{
    const string ManifestEntry = "manifest.json", UniqueEntry = "items/ItemUnique.json", TemplateEntry = "items/ItemTemplate.json";
    const string BlobKey = "$base64";

    static readonly JsonSerializerOptions ManifestJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        WriteIndented = true,
    };

    public static string FileName(string name, string tripId, string direction) => $"{name}-{tripId}-{direction}.odpassport";

    public static void Write(PassportData passport, string path)
    {
        var entries = new SortedDictionary<string, byte[]>(StringComparer.Ordinal)
        {
            [UniqueEntry] = EncodeRows(passport.ItemUnique),
            [TemplateEntry] = EncodeRows(passport.ItemTemplate),
        };
        foreach ((string table, List<Row> rows) in passport.Tables)
            entries[$"rows/{table}.json"] = EncodeRows(rows);

        passport.Manifest.PayloadSha256 = Checksum(entries);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        AddEntry(zip, ManifestEntry, JsonSerializer.SerializeToUtf8Bytes(passport.Manifest, ManifestJson));
        foreach ((string name, byte[] data) in entries)
            AddEntry(zip, name, data);
    }

    public static PassportData Read(string path)
    {
        if (!File.Exists(path))
            throw new PassportException("no_file", $"No passport at {path}.");

        try
        {
            using ZipArchive zip = ZipFile.OpenRead(path);
            Manifest manifest = JsonSerializer.Deserialize<Manifest>(ReadEntry(zip, ManifestEntry), ManifestJson)
                ?? throw new InvalidDataException("Empty manifest.");
            if (manifest.Format != Manifest.CurrentFormat)
                throw new PassportException("wrong_format", $"This passport is format {manifest.Format}; this game reads format {Manifest.CurrentFormat}.");

            var entries = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (ZipArchiveEntry entry in zip.Entries.Where(e => e.FullName != ManifestEntry))
                entries[entry.FullName] = ReadEntry(zip, entry.FullName);
            if (!string.Equals(Checksum(entries), manifest.PayloadSha256, StringComparison.OrdinalIgnoreCase))
                throw new PassportException("damaged", "This passport file is damaged. Nothing was changed.");

            var passport = new PassportData
            {
                Manifest = manifest,
                ItemUnique = DecodeRows(entries[UniqueEntry]),
                ItemTemplate = DecodeRows(entries[TemplateEntry]),
            };
            foreach ((string name, byte[] data) in entries.Where(e => e.Key.StartsWith("rows/", StringComparison.Ordinal)))
                passport.Tables[Path.GetFileNameWithoutExtension(name)] = DecodeRows(data);
            return passport;
        }
        catch (Exception e) when (e is InvalidDataException or JsonException or KeyNotFoundException or IOException)
        {
            throw new PassportException("damaged", $"This passport file cannot be read: {e.Message}");
        }
    }

    static void AddEntry(ZipArchive zip, string name, byte[] data)
    {
        using Stream stream = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
        stream.Write(data);
    }

    static byte[] ReadEntry(ZipArchive zip, string name)
    {
        ZipArchiveEntry entry = zip.GetEntry(name) ?? throw new InvalidDataException($"Missing {name}.");
        using Stream stream = entry.Open();
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    static string Checksum(SortedDictionary<string, byte[]> entries)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach ((string name, byte[] data) in entries)
        {
            hash.AppendData(Encoding.UTF8.GetBytes(name));
            hash.AppendData(data);
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    // Values keep their SQLite storage class: integer, real, text, null, or a blob as {"$base64": "..."}.
    static byte[] EncodeRows(List<Row> rows)
    {
        using var memory = new MemoryStream();
        using (var json = new Utf8JsonWriter(memory))
        {
            json.WriteStartArray();
            foreach (Row row in rows)
            {
                json.WriteStartObject();
                foreach ((string column, object? value) in row)
                    WriteValue(json, column, value);
                json.WriteEndObject();
            }
            json.WriteEndArray();
        }
        return memory.ToArray();
    }

    static void WriteValue(Utf8JsonWriter json, string column, object? value)
    {
        switch (value)
        {
            case null: json.WriteNull(column); break;
            case long number: json.WriteNumber(column, number); break;
            case double number: json.WriteNumber(column, number); break;
            case string text: json.WriteString(column, text); break;
            case byte[] blob:
                json.WriteStartObject(column);
                json.WriteBase64String(BlobKey, blob);
                json.WriteEndObject();
                break;
            default: throw new InvalidDataException($"Column {column} holds a {value.GetType().Name}.");
        }
    }

    static List<Row> DecodeRows(byte[] data)
    {
        using JsonDocument document = JsonDocument.Parse(data);
        var rows = new List<Row>(document.RootElement.GetArrayLength());
        foreach (JsonElement element in document.RootElement.EnumerateArray())
        {
            var row = new Row();
            foreach (JsonProperty property in element.EnumerateObject())
                row[property.Name] = ReadValue(property.Value);
            rows.Add(row);
        }
        return rows;
    }

    static object? ReadValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null => null,
        JsonValueKind.Number => value.TryGetInt64(out long number) ? number : value.GetDouble(),
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Object => value.GetProperty(BlobKey).GetBytesFromBase64(),
        _ => throw new InvalidDataException($"Unexpected {value.ValueKind} in a passport row."),
    };
}
