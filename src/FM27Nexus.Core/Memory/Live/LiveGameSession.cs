using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;
using FM27Nexus.Core.Memory.OpenEditor;
using FM27Nexus.Core.Tactics;

namespace FM27Nexus.Core.Memory.Live;

/// <summary>
/// Live in-game entity index built from a user-imported OpenEditor definition
/// and MSVC RTTI instance scans of game_plugin.dll heap objects.
/// </summary>
public sealed class LiveGameSession : IDisposable
{
    private readonly NativeProcessMemory _memory = new();
    private OpenEditorDefinition? _definition;
    private ModuleInfo? _module;
    private readonly Dictionary<string, Dictionary<long, long>> _entitiesById =
        new(StringComparer.OrdinalIgnoreCase);
    private string? _definitionPath;
    private IReadOnlyList<RttiClass> _rttiClasses = Array.Empty<RttiClass>();

    public bool IsConnected => _memory.IsOpen && _definition is not null && PlayerCount > 0;
    public int PlayerCount => Count("Player");
    public int ClubCount => Count("Club");
    public int TeamCount => Count("Team");
    public string? DefinitionVersion => _definition?.Version;
    public string? ModuleVersion { get; private set; }
    public string? DefinitionPath => _definitionPath;
    public string? LastError { get; private set; }
    public OpenEditorDefinition? Definition => _definition;

    public LiveImportResult ImportDefinition(string path, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);
        var def = OpenEditorDefinition.Load(path);
        var dest = Path.Combine(destinationDirectory, Path.GetFileName(path));
        File.Copy(path, dest, overwrite: true);
        _definition = def;
        _definitionPath = dest;
        return new LiveImportResult(true, dest, def.Version, def.ModuleVersions.ToList(), null);
    }

    public LiveImportResult LoadDefinition(string path)
    {
        var def = OpenEditorDefinition.Load(path);
        _definition = def;
        _definitionPath = path;
        return new LiveImportResult(true, path, def.Version, def.ModuleVersions.ToList(), null);
    }

    public LiveConnectResult Connect(int processId, string expectedExecutablePath, CancellationToken cancellationToken = default)
    {
        LastError = null;
        _entitiesById.Clear();
        try
        {
            if (_definition is null)
                return FailConnect("Import an OpenEditor definition JSON first (user-imported; not shipped with Nexus).");

            VerifyProcess(processId, expectedExecutablePath);
            _memory.Open(processId);

            _module = _memory.FindModule(_definition.Module)
                      ?? throw new InvalidOperationException($"Module '{_definition.Module}' not found in the process.");

            ModuleVersion = TryReadFileVersion(_module.FilePath);
            if (_definition.ModuleVersions.Count > 0
                && !string.IsNullOrWhiteSpace(ModuleVersion)
                && !_definition.ModuleVersions.Any(v =>
                    string.Equals(v, ModuleVersion, StringComparison.OrdinalIgnoreCase)))
            {
                LastError = $"Definition moduleVersions [{string.Join(", ", _definition.ModuleVersions)}] do not include running {ModuleVersion}.";
            }

            var image = ReadModuleImage(_module);
            _rttiClasses = MsvcRttiScanner.FindClasses(image, _module.BaseAddress);
            if (_rttiClasses.Count == 0)
                return FailConnect("No MSVC RTTI classes found in game_plugin.dll.");

            IndexEntity("Player", required: true, cancellationToken);
            IndexEntity("Club", required: false, cancellationToken);
            IndexEntity("Team", required: false, cancellationToken);

            if (PlayerCount == 0)
                return FailConnect("RTTI scan found no player instances with a positive Id. Is a career save loaded in FM?");

            return new LiveConnectResult(true, PlayerCount, ClubCount, TeamCount, ModuleVersion, DefinitionVersion, LastError);
        }
        catch (Exception ex)
        {
            _memory.Close();
            _entitiesById.Clear();
            return FailConnect(ex.Message);
        }
    }

    public void Disconnect()
    {
        _memory.Close();
        _entitiesById.Clear();
        _module = null;
        ModuleVersion = null;
        _rttiClasses = Array.Empty<RttiClass>();
    }

    public LiveApplyResult ApplyPlayerFields(long playerId, IReadOnlyDictionary<string, long> displayValues) =>
        ApplyEntityFields("Player", playerId, displayValues, aliasPlayerFields: true);

    public LiveApplyResult ApplyEntityFields(
        string entity,
        long entityId,
        IReadOnlyDictionary<string, long> displayValues,
        bool aliasPlayerFields = false)
    {
        if (!IsConnected || _definition is null)
            return new LiveApplyResult(false, "Live session is not connected.", Array.Empty<string>());
        if (!_entitiesById.TryGetValue(entity, out var map) || !map.TryGetValue(entityId, out var baseAddress))
            return new LiveApplyResult(false, $"{entity} Id {entityId} was not found in the live index.", Array.Empty<string>());
        if (!_definition.Structs.TryGetValue(entity, out var entityStruct))
            return new LiveApplyResult(false, $"{entity} struct missing from definition.", Array.Empty<string>());

        var applied = new List<string>();
        foreach (var (rawName, displayValue) in displayValues)
        {
            var fieldName = aliasPlayerFields && entity.Equals("Player", StringComparison.OrdinalIgnoreCase)
                ? ResolveFieldAlias(rawName)
                : rawName.Trim();
            if (!entityStruct.TryGetValue(fieldName, out var field) || field.Offset is null)
                return new LiveApplyResult(false, $"Field '{rawName}' is not present in the imported definition (mapped as '{fieldName}').", applied);

            if (!TryResolveFieldAddress(baseAddress, field, out var address, out var error))
                return new LiveApplyResult(false, error ?? "Could not resolve field address.", applied);

            var stored = ToStoredValue(displayValue, field.Scale);
            WriteTyped(address, field.Type, stored);
            applied.Add(fieldName);
        }

        return new LiveApplyResult(true, $"Applied {applied.Count} field(s) to live {entity} {entityId}.", applied);
    }

    public LiveReadResult ReadEntityFields(string entity, long entityId, IReadOnlyList<string> fieldNames)
    {
        if (!IsConnected || _definition is null)
            return new LiveReadResult(false, "Live session is not connected.", new Dictionary<string, long?>());
        if (!_entitiesById.TryGetValue(entity, out var map) || !map.TryGetValue(entityId, out var baseAddress))
            return new LiveReadResult(false, $"{entity} Id {entityId} was not found.", new Dictionary<string, long?>());
        if (!_definition.Structs.TryGetValue(entity, out var entityStruct))
            return new LiveReadResult(false, $"{entity} struct missing.", new Dictionary<string, long?>());

        var values = new Dictionary<string, long?>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in fieldNames)
        {
            var fieldName = entity.Equals("Player", StringComparison.OrdinalIgnoreCase)
                ? ResolveFieldAlias(raw)
                : raw;
            if (!entityStruct.TryGetValue(fieldName, out var field) || field.Offset is null)
            {
                values[fieldName] = null;
                continue;
            }

            if (!TryResolveFieldAddress(baseAddress, field, out var address, out _))
            {
                values[fieldName] = null;
                continue;
            }

            if (!TryReadTyped(address, field.Type, out var stored))
            {
                values[fieldName] = null;
                continue;
            }

            values[fieldName] = field.Scale > 1 ? stored / field.Scale : stored;
        }

        return new LiveReadResult(true, $"Read {values.Count} field(s).", values);
    }

    public long? FindClubIdByName(string clubName)
    {
        if (_definition is null || !_entitiesById.TryGetValue("Club", out var clubs))
            return null;
        if (!_definition.Structs.TryGetValue("Club", out var clubStruct))
            return null;
        if (!clubStruct.TryGetValue("Name", out var nameField))
            return null;

        foreach (var (id, address) in clubs)
        {
            if (!TryReadFmString(address, nameField, out var name))
                continue;
            if (string.Equals(name, clubName, StringComparison.OrdinalIgnoreCase)
                || name.Contains(clubName, StringComparison.OrdinalIgnoreCase)
                || clubName.Contains(name, StringComparison.OrdinalIgnoreCase))
                return id;
        }

        return null;
    }

    public IReadOnlyList<TacticalFactorMapping> GetTacticalFactorMappings() =>
        _definition is null
            ? TacticalUnderstandingFactors.All.Select(f => new TacticalFactorMapping(f, null, null, null, false)).ToList()
            : TacticalUnderstandingFactors.Resolve(_definition);

    public LiveApplyResult MaximizeTacticalUnderstanding(IReadOnlyList<long>? playerIds = null)
    {
        if (!IsConnected || _definition is null)
            return new LiveApplyResult(false, "Live session is not connected.", Array.Empty<string>());

        var mappings = GetTacticalFactorMappings().Where(m => m.IsMapped).ToList();
        if (mappings.Count == 0)
        {
            return new LiveApplyResult(false,
                "No Tactical Understanding factors are mapped in the imported OpenEditor definition. Extend the definition with those keys (no invented offsets), then re-import.",
                Array.Empty<string>());
        }

        var applied = new List<string>();
        var targets = playerIds is { Count: > 0 }
            ? playerIds
            : (_entitiesById.TryGetValue("Player", out var all) ? all.Keys.ToList() : []);

        foreach (var mapping in mappings)
        {
            var display = TacticalUnderstandingFactors.MaximizeDisplayValue(mapping.Field!);
            var fields = new Dictionary<string, long> { [mapping.FieldKey!] = display };

            if (mapping.Entity!.Equals("Player", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var pid in targets)
                {
                    var result = ApplyEntityFields("Player", pid, fields);
                    if (result.Ok)
                        applied.Add($"Player:{pid}:{mapping.Factor.Id}");
                }
            }
            else if (_entitiesById.TryGetValue(mapping.Entity, out var map))
            {
                foreach (var id in map.Keys)
                {
                    var result = ApplyEntityFields(mapping.Entity, id, fields);
                    if (result.Ok)
                        applied.Add($"{mapping.Entity}:{id}:{mapping.Factor.Id}");
                }
            }
        }

        if (applied.Count == 0)
            return new LiveApplyResult(false, "Mapped factors exist but no entities could be written.", applied);

        return new LiveApplyResult(true,
            $"Maximized {mappings.Count} mapped factor(s); wrote {applied.Count} value(s). Save in FM to persist.",
            applied);
    }

    public IReadOnlyList<string> ClubFieldCatalog()
    {
        if (_definition is null || !_definition.Structs.TryGetValue("Club", out var fields))
            return Array.Empty<string>();
        return fields
            .Where(kv => kv.Value.Offset is not null)
            .Select(kv => kv.Key)
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public void Dispose()
    {
        Disconnect();
        _memory.Dispose();
    }

    private int Count(string entity) =>
        _entitiesById.TryGetValue(entity, out var map) ? map.Count : 0;

    private void IndexEntity(string entityName, bool required, CancellationToken cancellationToken)
    {
        if (_definition is null || _module is null)
            throw new InvalidOperationException("Not ready.");

        if (!_definition.Collections.TryGetValue(entityName, out var collection))
        {
            if (required) throw new InvalidOperationException($"Definition has no {entityName} collection.");
            return;
        }

        if (!string.Equals(collection.Layout, "rttiInstances", StringComparison.OrdinalIgnoreCase))
        {
            if (required)
                throw new InvalidOperationException($"{entityName} layout '{collection.Layout}' is not supported (need rttiInstances).");
            return;
        }

        if (!_definition.Structs.TryGetValue(entityName, out var entityStruct)
            || !entityStruct.TryGetValue("Id", out var idField)
            || idField.Offset is null)
        {
            if (required) throw new InvalidOperationException($"{entityName}.Id field is missing in the definition.");
            return;
        }

        var vtables = new List<RttiVtable>();
        foreach (var target in collection.Classes)
        {
            var cls = _rttiClasses.FirstOrDefault(c =>
                string.Equals(c.Name, target.ClassName, StringComparison.OrdinalIgnoreCase));
            if (cls is null)
            {
                if (required)
                    throw new InvalidOperationException($"RTTI class '{target.ClassName}' not found.");
                continue;
            }

            var vt = cls.Vtables.FirstOrDefault(v => v.ObjectOffset == target.VtableOffset)
                     ?? (target.VtableOffset == 0 ? cls.PrimaryVtable : null);
            if (vt is null)
            {
                if (required)
                    throw new InvalidOperationException($"Class '{target.ClassName}' has no vtable at 0x{target.VtableOffset:X}.");
                continue;
            }

            vtables.Add(vt);
        }

        if (vtables.Count == 0)
        {
            if (required) throw new InvalidOperationException($"No vtables resolved for {entityName}.");
            return;
        }

        var instances = MsvcRttiScanner.FindInstances(
            _memory,
            vtables,
            _module.BaseAddress,
            _module.Size,
            maxSamples: collection.MaxCount,
            cancellationToken: cancellationToken);

        var map = new Dictionary<long, long>();
        foreach (var address in instances)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryReadInt32(address + idField.Offset.Value, out var id) || id <= 0)
                continue;
            map[id] = address;
        }

        _entitiesById[entityName] = map;
    }

    private LiveConnectResult FailConnect(string error)
    {
        LastError = error;
        Disconnect();
        return new LiveConnectResult(false, 0, 0, 0, ModuleVersion, DefinitionVersion, error);
    }

    private static void VerifyProcess(int processId, string expectedExecutablePath)
    {
        Process proc;
        try { proc = Process.GetProcessById(processId); }
        catch (Exception ex) { throw new InvalidOperationException("Process not found: " + ex.Message); }

        string? path = null;
        try { path = proc.MainModule?.FileName; } catch { /* access */ }
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException("Could not resolve process path.");
        if (!string.Equals(Path.GetFullPath(path), Path.GetFullPath(expectedExecutablePath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Process path does not match the selected install executable.");
        if (proc.ProcessName is not ("fm" or "fm_gamepass"))
            throw new InvalidOperationException($"Refusing attach to '{proc.ProcessName}'. Exact names only: fm, fm_gamepass.");
    }

    private byte[] ReadModuleImage(ModuleInfo module)
    {
        var buffer = new byte[module.Size];
        var read = 0;
        while (read < buffer.Length)
        {
            var chunk = Math.Min(1024 * 1024, buffer.Length - read);
            Span<byte> slice = buffer.AsSpan(read, chunk);
            if (!_memory.TryReadBytes(module.BaseAddress + read, slice))
            {
                read += chunk;
                continue;
            }

            read += chunk;
        }

        return buffer;
    }

    private bool TryResolveFieldAddress(long recordBase, OpenEditorField field, out long address, out string? error)
    {
        address = recordBase;
        error = null;
        if (field.Path is { Count: > 0 })
        {
            foreach (var hop in field.Path)
            {
                if (!TryReadInt64(address + hop, out var next) || next == 0)
                {
                    error = "Field path pointer was null (value not present on this record).";
                    return false;
                }

                address = next;
            }
        }

        address += field.Offset!.Value;
        return true;
    }

    private bool TryReadFmString(long recordBase, OpenEditorField field, out string value)
    {
        value = "";
        if (!TryResolveFieldAddress(recordBase, field, out var address, out _))
            return false;

        // fmString: pointer to { i32 byteCount, utf8 bytes, 0 } OR the field offset is already the pointer location.
        if (field.Type.Equals("fmString", StringComparison.OrdinalIgnoreCase))
        {
            // When path already followed, address points at the string object or at a pointer to it.
            // OpenEditor: offset 0 on the pointed object. Try pointer then inline.
            if (TryReadInt64(address, out var ptr) && ptr != 0)
            {
                if (TryReadInt32(ptr, out var len) && len > 0 && len < 256)
                {
                    var bytes = new byte[len];
                    if (_memory.TryReadBytes(ptr + 4, bytes))
                    {
                        value = Encoding.UTF8.GetString(bytes);
                        return true;
                    }
                }
            }

            if (TryReadInt32(address, out var inlineLen) && inlineLen > 0 && inlineLen < 256)
            {
                var bytes = new byte[inlineLen];
                if (_memory.TryReadBytes(address + 4, bytes))
                {
                    value = Encoding.UTF8.GetString(bytes);
                    return true;
                }
            }
        }

        return false;
    }

    private void WriteTyped(long address, string type, long value)
    {
        byte[] buffer = type.ToLowerInvariant() switch
        {
            "i8" => [(byte)(sbyte)value],
            "u8" or "bool" => [(byte)value],
            "i16" => BitConverter.GetBytes((short)value),
            "u16" => BitConverter.GetBytes((ushort)value),
            "i32" => BitConverter.GetBytes((int)value),
            "u32" => BitConverter.GetBytes((uint)value),
            "i64" => BitConverter.GetBytes(value),
            "f32" => BitConverter.GetBytes((float)value),
            "f64" => BitConverter.GetBytes((double)value),
            _ => throw new InvalidOperationException($"Live writes do not support type '{type}'.")
        };
        _memory.WriteBytes(address, buffer);
    }

    private bool TryReadTyped(long address, string type, out long value)
    {
        value = 0;
        var size = type.ToLowerInvariant() switch
        {
            "i8" or "u8" or "bool" => 1,
            "i16" or "u16" => 2,
            "i32" or "u32" or "f32" => 4,
            "i64" or "f64" => 8,
            _ => 0
        };
        if (size == 0) return false;
        Span<byte> buf = stackalloc byte[size];
        if (!_memory.TryReadBytes(address, buf))
            return false;
        value = type.ToLowerInvariant() switch
        {
            "i8" => (sbyte)buf[0],
            "u8" or "bool" => buf[0],
            "i16" => BinaryPrimitives.ReadInt16LittleEndian(buf),
            "u16" => BinaryPrimitives.ReadUInt16LittleEndian(buf),
            "i32" => BinaryPrimitives.ReadInt32LittleEndian(buf),
            "u32" => BinaryPrimitives.ReadUInt32LittleEndian(buf),
            "i64" => BinaryPrimitives.ReadInt64LittleEndian(buf),
            "f32" => (long)BitConverter.ToSingle(buf),
            "f64" => (long)BitConverter.ToDouble(buf),
            _ => 0
        };
        return true;
    }

    private bool TryReadInt32(long address, out int value)
    {
        value = 0;
        Span<byte> buf = stackalloc byte[4];
        if (!_memory.TryReadBytes(address, buf))
            return false;
        value = BinaryPrimitives.ReadInt32LittleEndian(buf);
        return true;
    }

    private bool TryReadInt64(long address, out long value)
    {
        value = 0;
        Span<byte> buf = stackalloc byte[8];
        if (!_memory.TryReadBytes(address, buf))
            return false;
        value = BinaryPrimitives.ReadInt64LittleEndian(buf);
        return true;
    }

    private static long ToStoredValue(long displayValue, int scale) =>
        scale <= 1 ? displayValue : displayValue * scale;

    public static string ResolveFieldAlias(string field)
    {
        var f = field.Trim();
        return f.ToUpperInvariant() switch
        {
            "CA" => "CurrentAbility",
            "PA" => "PotentialAbility",
            "ID" or "UNIQUEID" or "UID" => "Id",
            _ when f.StartsWith("Attr.", StringComparison.OrdinalIgnoreCase) => f,
            _ when KnownAttributes.Contains(f) => "Attr." + f,
            _ => f
        };
    }

    private static readonly HashSet<string> KnownAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Pace", "Acceleration", "Finishing", "Passing", "Tackling", "Positioning", "Decisions",
        "Stamina", "Strength", "JumpingReach", "Heading", "Dribbling", "FirstTouch", "Technique",
        "Composure", "OffTheBall", "WorkRate", "Teamwork", "Vision", "Aggression", "Bravery",
        "Determination", "Flair", "Leadership", "Anticipation", "Concentration", "Agility",
        "Balance", "NaturalFitness", "Marking", "Crossing", "LongShots", "Corners", "FreeKicks",
        "LongThrows", "PenaltyTaking", "Eccentricity", "RushingOut", "TendencyToPunch",
        "Throwing", "Handling", "Reflexes", "OneOnOnes", "Kicking", "AerialReach", "CommandOfArea",
        "Communication", "Consistency", "ImportantMatches", "InjuryProneness", "Dirtiness"
    };

    private static string? TryReadFileVersion(string path)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            return info.ProductVersion ?? info.FileVersion;
        }
        catch
        {
            return null;
        }
    }
}

public sealed record LiveImportResult(bool Ok, string? Path, string? Version, IReadOnlyList<string> ModuleVersions, string? Error);
public sealed record LiveConnectResult(bool Ok, int PlayerCount, int ClubCount, int TeamCount, string? ModuleVersion, string? DefinitionVersion, string? Error);
public sealed record LiveApplyResult(bool Ok, string Message, IReadOnlyList<string> AppliedFields);
public sealed record LiveReadResult(bool Ok, string Message, IReadOnlyDictionary<string, long?> Values);
