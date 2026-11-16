using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FM27Nexus.Core.Memory.OpenEditor;

/// <summary>
/// Loader for OpenEditor-compatible FM27 definition JSON (user-imported).
/// Format documented publicly; this project does not redistribute GPL definition files.
/// </summary>
public sealed class OpenEditorDefinition
{
    public required string Game { get; init; }
    public required string Version { get; init; }
    public required string Module { get; init; }
    public required IReadOnlyList<string> ModuleVersions { get; init; }
    public required IReadOnlyDictionary<string, OpenEditorCollection> Collections { get; init; }
    public required IReadOnlyDictionary<string, IReadOnlyDictionary<string, OpenEditorField>> Structs { get; init; }

    public static OpenEditorDefinition Load(string path)
    {
        var json = File.ReadAllText(path);
        var doc = JsonSerializer.Deserialize<OpenEditorDefinitionDocument>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        }) ?? throw new InvalidDataException("Definition JSON was empty.");

        if (string.IsNullOrWhiteSpace(doc.Module))
            throw new InvalidDataException("Definition missing module.");
        if (doc.Collections.Count == 0 || doc.Structs.Count == 0)
            throw new InvalidDataException("Definition missing collections/structs.");

        return new OpenEditorDefinition
        {
            Game = doc.Game ?? "Football Manager",
            Version = doc.Version ?? "",
            Module = doc.Module,
            ModuleVersions = doc.ModuleVersions ?? [],
            Collections = doc.Collections.ToDictionary(
                kv => kv.Key,
                kv => kv.Value.ToModel(),
                StringComparer.OrdinalIgnoreCase),
            Structs = doc.Structs.ToDictionary(
                kv => kv.Key,
                kv => (IReadOnlyDictionary<string, OpenEditorField>)kv.Value.ToDictionary(
                    f => f.Key,
                    f => f.Value.ToModel(),
                    StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase)
        };
    }
}

public sealed class OpenEditorCollection
{
    public required string Layout { get; init; }
    public required IReadOnlyList<OpenEditorClassTarget> Classes { get; init; }
    public int MaxCount { get; init; } = 2_000_000;
}

public sealed class OpenEditorClassTarget
{
    public required string ClassName { get; init; }
    public required int VtableOffset { get; init; }
}

public sealed class OpenEditorField
{
    public required int? Offset { get; init; }
    public required string Type { get; init; }
    public int Scale { get; init; } = 1;
    public IReadOnlyList<int>? Path { get; init; }
}

public sealed class OpenEditorDefinitionDocument
{
    public string? Game { get; set; }
    public string? Version { get; set; }
    public string? Module { get; set; }
    public List<string>? ModuleVersions { get; set; }
    public Dictionary<string, OpenEditorCollectionDocument> Collections { get; set; } = new();
    public Dictionary<string, Dictionary<string, OpenEditorFieldDocument>> Structs { get; set; } = new();
}

public sealed class OpenEditorCollectionDocument
{
    public string Layout { get; set; } = "";
    public List<OpenEditorClassDocument>? Classes { get; set; }
    public int MaxCount { get; set; } = 2_000_000;

    public OpenEditorCollection ToModel() => new()
    {
        Layout = Layout,
        MaxCount = MaxCount,
        Classes = (Classes ?? []).Select(c => c.ToModel()).ToList()
    };
}

public sealed class OpenEditorClassDocument
{
    [JsonPropertyName("class")]
    public string Class { get; set; } = "";
    public string? VtableOffset { get; set; }

    public OpenEditorClassTarget ToModel() => new()
    {
        ClassName = Class,
        VtableOffset = ParseHex(VtableOffset)
    };

    private static int ParseHex(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        text = text.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return int.Parse(text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return int.Parse(text, CultureInfo.InvariantCulture);
    }
}

public sealed class OpenEditorFieldDocument
{
    public string? Offset { get; set; }
    public string Type { get; set; } = "i32";
    public int Scale { get; set; } = 1;
    public List<string>? Path { get; set; }

    public OpenEditorField ToModel() => new()
    {
        Offset = ParseOffset(Offset),
        Type = Type,
        Scale = Scale <= 0 ? 1 : Scale,
        Path = Path?.Select(ParseOffsetRequired).ToList()
    };

    private static int? ParseOffset(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Equals("null", StringComparison.OrdinalIgnoreCase))
            return null;
        return ParseOffsetRequired(text);
    }

    private static int ParseOffsetRequired(string text)
    {
        text = text.Trim();
        var neg = text.StartsWith('-');
        if (neg) text = text[1..];
        int value;
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            value = int.Parse(text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        else
            value = int.Parse(text, CultureInfo.InvariantCulture);
        return neg ? -value : value;
    }
}
