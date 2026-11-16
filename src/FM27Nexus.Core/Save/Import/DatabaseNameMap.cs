using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;

namespace FM27Nexus.Core.Save.Import;

/// <summary>
/// External UTF-8 database_id,name maps for competitions/nations/cities/grounds
/// (.fm saves omit installed database name strings).
/// </summary>
public sealed class DatabaseNameMap
{
    private readonly Dictionary<long, string> _names;

    public DatabaseNameMap(IEnumerable<KeyValuePair<long, string>> pairs)
    {
        _names = pairs.ToDictionary(p => p.Key, p => p.Value);
    }

    public static DatabaseNameMap Empty { get; } = new(Array.Empty<KeyValuePair<long, string>>());

    public string? Resolve(long? id) =>
        id is null ? null : _names.TryGetValue(id.Value, out var name) ? name : null;

    public static DatabaseNameMap LoadCsv(string path)
    {
        using var reader = new StreamReader(path);
        using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            MissingFieldFound = null,
            HeaderValidated = null
        });

        var pairs = new List<KeyValuePair<long, string>>();
        csv.Read();
        csv.ReadHeader();
        while (csv.Read())
        {
            var idText = csv.GetField(0);
            var name = csv.GetField(1);
            if (long.TryParse(idText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
                && !string.IsNullOrWhiteSpace(name))
            {
                pairs.Add(new KeyValuePair<long, string>(id, name.Trim()));
            }
        }

        return new DatabaseNameMap(pairs);
    }
}
