using System.Text.Json.Serialization;

namespace FM27Nexus.Core.Memory;

/// <summary>
/// Declarative build-verified offset/AOB profile. Never invent values — only load community/build-verified JSON.
/// </summary>
public sealed class OffsetProfile
{
    public required string BuildId { get; init; }
    public required string Storefront { get; init; }
    public required string UnityRuntimeHint { get; init; }
    public required string SchemaVersion { get; init; }
    public required IReadOnlyList<AobSignature> Signatures { get; init; }
    public required IReadOnlyDictionary<string, EntityLayout> Entities { get; init; }
}

public sealed class AobSignature
{
    public required string Name { get; init; }
    /// <summary>Space-separated hex bytes; ?? = wildcard.</summary>
    public required string Pattern { get; init; }
    public required long OffsetFromMatch { get; init; }
}

public sealed class EntityLayout
{
    public required string Name { get; init; }
    public required string BaseSignature { get; init; }
    public required int Stride { get; init; }
    public required IReadOnlyDictionary<string, FieldLayout> Fields { get; init; }
}

public sealed class FieldLayout
{
    public required int Offset { get; init; }
    public required string Type { get; init; } // i8,u8,i16,u16,i32,u32,i64,f32
    public int? Min { get; init; }
    public int? Max { get; init; }
    public bool Writable { get; init; }
}

public sealed class OffsetProfileDocument
{
    [JsonPropertyName("buildId")] public string BuildId { get; set; } = "";
    [JsonPropertyName("storefront")] public string Storefront { get; set; } = "";
    [JsonPropertyName("unityRuntimeHint")] public string UnityRuntimeHint { get; set; } = "";
    [JsonPropertyName("schemaVersion")] public string SchemaVersion { get; set; } = "1";
    [JsonPropertyName("signatures")] public List<AobSignatureDocument> Signatures { get; set; } = [];
    [JsonPropertyName("entities")] public Dictionary<string, EntityLayoutDocument> Entities { get; set; } = new();

    public OffsetProfile ToProfile() => new()
    {
        BuildId = BuildId,
        Storefront = Storefront,
        UnityRuntimeHint = UnityRuntimeHint,
        SchemaVersion = SchemaVersion,
        Signatures = Signatures.Select(s => new AobSignature
        {
            Name = s.Name,
            Pattern = s.Pattern,
            OffsetFromMatch = s.OffsetFromMatch
        }).ToList(),
        Entities = Entities.ToDictionary(
            kv => kv.Key,
            kv => new EntityLayout
            {
                Name = kv.Value.Name,
                BaseSignature = kv.Value.BaseSignature,
                Stride = kv.Value.Stride,
                Fields = kv.Value.Fields.ToDictionary(
                    f => f.Key,
                    f => new FieldLayout
                    {
                        Offset = f.Value.Offset,
                        Type = f.Value.Type,
                        Min = f.Value.Min,
                        Max = f.Value.Max,
                        Writable = f.Value.Writable
                    })
            },
            StringComparer.OrdinalIgnoreCase)
    };
}

public sealed class AobSignatureDocument
{
    public string Name { get; set; } = "";
    public string Pattern { get; set; } = "";
    public long OffsetFromMatch { get; set; }
}

public sealed class EntityLayoutDocument
{
    public string Name { get; set; } = "";
    public string BaseSignature { get; set; } = "";
    public int Stride { get; set; }
    public Dictionary<string, FieldLayoutDocument> Fields { get; set; } = new();
}

public sealed class FieldLayoutDocument
{
    public int Offset { get; set; }
    public string Type { get; set; } = "i32";
    public int? Min { get; set; }
    public int? Max { get; set; }
    public bool Writable { get; set; }
}
