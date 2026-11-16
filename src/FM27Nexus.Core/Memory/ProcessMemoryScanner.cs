using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FM27Nexus.Core.Memory;

public sealed class ProcessMemoryScanner : IMemoryScanner, IDisposable
{
    private readonly IOffsetProvider _offsets;
    private IntPtr _handle = IntPtr.Zero;
    private Dictionary<string, long> _resolvedBases = new(StringComparer.OrdinalIgnoreCase);

    public bool IsAttached => _handle != IntPtr.Zero && ProcessId is not null;
    public int? ProcessId { get; private set; }
    public string? ExecutablePath { get; private set; }

    public ProcessMemoryScanner(IOffsetProvider offsets)
    {
        _offsets = offsets;
    }

    public AttachResult Attach(int processId, string expectedExecutablePath)
    {
        Detach();
        System.Diagnostics.Process proc;
        try
        {
            proc = System.Diagnostics.Process.GetProcessById(processId);
        }
        catch (Exception ex)
        {
            return new AttachResult { Ok = false, Error = "Process not found: " + ex.Message };
        }

        string? path = null;
        try { path = proc.MainModule?.FileName; } catch { /* access */ }
        if (string.IsNullOrWhiteSpace(path))
            return new AttachResult { Ok = false, Error = "Could not resolve process path." };

        if (!string.Equals(Path.GetFullPath(path), Path.GetFullPath(expectedExecutablePath), StringComparison.OrdinalIgnoreCase))
            return new AttachResult { Ok = false, Error = "Process path does not match the selected install executable." };

        var name = proc.ProcessName;
        if (name is not ("fm" or "fm_gamepass"))
            return new AttachResult { Ok = false, Error = $"Refusing attach to '{name}'. Exact names only: fm, fm_gamepass." };

        _handle = OpenProcess(ProcessAllAccess, false, processId);
        if (_handle == IntPtr.Zero)
            return new AttachResult { Ok = false, Error = "OpenProcess failed. Is MemoryAgent elevated?" };

        ProcessId = processId;
        ExecutablePath = path;

        if (_offsets.ActiveProfile is null)
            return new AttachResult { Ok = true, Error = "Attached, but no offset profile loaded — reads/writes remain blocked." };

        var resolve = ResolveSignatures(_offsets.ActiveProfile);
        if (!resolve.Ok)
        {
            Detach();
            return new AttachResult { Ok = false, Error = resolve.Error };
        }

        return new AttachResult { Ok = true };
    }

    public void Detach()
    {
        if (_handle != IntPtr.Zero)
        {
            CloseHandle(_handle);
            _handle = IntPtr.Zero;
        }

        ProcessId = null;
        ExecutablePath = null;
        _resolvedBases.Clear();
    }

    public ReadResult ReadField(string entity, int index, string field)
    {
        if (!TryGetFieldAddress(entity, index, field, out var address, out var layout, out var error))
            return new ReadResult { Ok = false, Error = error };

        if (!TryReadValue(address, layout.Type, out var value, out error))
            return new ReadResult { Ok = false, Error = error };

        var within = true;
        if (layout.Min is not null && value < layout.Min.Value) within = false;
        if (layout.Max is not null && value > layout.Max.Value) within = false;

        return new ReadResult { Ok = true, Value = value, WithinExpectedBounds = within };
    }

    public WriteResult WriteField(string entity, int index, string field, long value, bool force = false)
    {
        if (!TryGetFieldAddress(entity, index, field, out var address, out var layout, out var error))
            return new WriteResult { Ok = false, Error = error };
        if (!layout.Writable)
            return new WriteResult { Ok = false, Error = $"Field '{field}' is not marked writable in the profile." };
        if (!force)
        {
            if (layout.Min is not null && value < layout.Min.Value)
                return new WriteResult { Ok = false, Error = $"Value {value} below min {layout.Min}." };
            if (layout.Max is not null && value > layout.Max.Value)
                return new WriteResult { Ok = false, Error = $"Value {value} above max {layout.Max}." };
        }

        return TryWriteValue(address, layout.Type, value, out error)
            ? new WriteResult { Ok = true }
            : new WriteResult { Ok = false, Error = error };
    }

    public ScanValidationResult ValidateReadSanity()
    {
        var profile = _offsets.ActiveProfile;
        if (profile is null)
            return new ScanValidationResult { Ok = false, Error = "No profile loaded.", FieldsChecked = 0, FieldsPassed = 0 };
        if (!IsAttached)
            return new ScanValidationResult { Ok = false, Error = "Not attached.", FieldsChecked = 0, FieldsPassed = 0 };

        var checkedCount = 0;
        var passed = 0;
        foreach (var entity in profile.Entities)
        {
            foreach (var field in entity.Value.Fields.Take(3))
            {
                checkedCount++;
                var read = ReadField(entity.Key, 0, field.Key);
                if (read.Ok && read.WithinExpectedBounds)
                    passed++;
            }
        }

        var ok = checkedCount > 0 && passed == checkedCount;
        return new ScanValidationResult
        {
            Ok = ok,
            Error = ok ? null : "One or more sanity reads failed bounds/signature checks. Writes remain disabled until validation passes.",
            FieldsChecked = checkedCount,
            FieldsPassed = passed
        };
    }

    private bool TryGetFieldAddress(string entity, int index, string field, out long address, out FieldLayout layout, out string? error)
    {
        address = 0;
        layout = null!;
        error = null;
        if (!IsAttached)
        {
            error = "Not attached.";
            return false;
        }

        var profile = _offsets.ActiveProfile;
        if (profile is null)
        {
            error = "No offset profile loaded.";
            return false;
        }

        if (!profile.Entities.TryGetValue(entity, out var entityLayout))
        {
            error = $"Unknown entity '{entity}'.";
            return false;
        }

        if (!entityLayout.Fields.TryGetValue(field, out layout!))
        {
            error = $"Unknown field '{field}'.";
            return false;
        }

        if (!_resolvedBases.TryGetValue(entityLayout.BaseSignature, out var bas))
        {
            error = $"Base signature '{entityLayout.BaseSignature}' was not resolved.";
            return false;
        }

        address = bas + (index * (long)entityLayout.Stride) + layout.Offset;
        return true;
    }

    private AttachResult ResolveSignatures(OffsetProfile profile)
    {
        _resolvedBases.Clear();
        byte[] image;
        try
        {
            image = File.ReadAllBytes(ExecutablePath!);
        }
        catch (Exception ex)
        {
            return new AttachResult { Ok = false, Error = "Failed reading game image for AOB scan: " + ex.Message };
        }

        if (!TryGetModuleBase(ProcessId!.Value, out var moduleBase))
            return new AttachResult { Ok = false, Error = "Failed to resolve module base (MainModule inaccessible)." };

        foreach (var sig in profile.Signatures)
        {
            var pattern = ParsePattern(sig.Pattern);
            if (pattern.Length == 0)
                return new AttachResult { Ok = false, Error = $"Signature '{sig.Name}' has an empty/invalid AOB pattern." };

            var offset = FindPattern(image, pattern);
            if (offset < 0)
                return new AttachResult { Ok = false, Error = $"AOB signature '{sig.Name}' not found in the running binary. Wrong build/storefront profile?" };

            _resolvedBases[sig.Name] = moduleBase + offset + sig.OffsetFromMatch;
        }

        return new AttachResult { Ok = true };
    }

    private static (byte?[] Pattern, int Length) ParsePatternTuple(string pattern)
    {
        var parts = pattern.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var bytes = new byte?[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            if (parts[i] is "??" or "?")
                bytes[i] = null;
            else
                bytes[i] = Convert.ToByte(parts[i], 16);
        }

        return (bytes, bytes.Length);
    }

    private static byte?[] ParsePattern(string pattern) => ParsePatternTuple(pattern).Pattern;

    private static int FindPattern(byte[] haystack, byte?[] pattern)
    {
        for (var i = 0; i <= haystack.Length - pattern.Length; i++)
        {
            var ok = true;
            for (var j = 0; j < pattern.Length; j++)
            {
                if (pattern[j] is null) continue;
                if (haystack[i + j] != pattern[j])
                {
                    ok = false;
                    break;
                }
            }

            if (ok) return i;
        }

        return -1;
    }

    private bool TryReadValue(long address, string type, out long value, out string? error)
    {
        value = 0;
        error = null;
        var size = SizeOf(type);
        var buffer = new byte[size];
        if (!ReadProcessMemory(_handle, (IntPtr)address, buffer, size, out var read) || read != size)
        {
            error = "ReadProcessMemory failed.";
            return false;
        }

        value = type.ToLowerInvariant() switch
        {
            "i8" => (sbyte)buffer[0],
            "u8" => buffer[0],
            "i16" => BitConverter.ToInt16(buffer, 0),
            "u16" => BitConverter.ToUInt16(buffer, 0),
            "i32" => BitConverter.ToInt32(buffer, 0),
            "u32" => BitConverter.ToUInt32(buffer, 0),
            "i64" => BitConverter.ToInt64(buffer, 0),
            "f32" => (long)BitConverter.ToSingle(buffer, 0),
            _ => throw new InvalidOperationException("Unsupported type " + type)
        };
        return true;
    }

    private bool TryWriteValue(long address, string type, long value, out string? error)
    {
        error = null;
        byte[] buffer = type.ToLowerInvariant() switch
        {
            "i8" => [(byte)(sbyte)value],
            "u8" => [(byte)value],
            "i16" => BitConverter.GetBytes((short)value),
            "u16" => BitConverter.GetBytes((ushort)value),
            "i32" => BitConverter.GetBytes((int)value),
            "u32" => BitConverter.GetBytes((uint)value),
            "i64" => BitConverter.GetBytes(value),
            "f32" => BitConverter.GetBytes((float)value),
            _ => Array.Empty<byte>()
        };
        if (buffer.Length == 0)
        {
            error = "Unsupported type " + type;
            return false;
        }

        if (!WriteProcessMemory(_handle, (IntPtr)address, buffer, buffer.Length, out var written) || written != buffer.Length)
        {
            error = "WriteProcessMemory failed.";
            return false;
        }

        return true;
    }

    private static int SizeOf(string type) => type.ToLowerInvariant() switch
    {
        "i8" or "u8" => 1,
        "i16" or "u16" => 2,
        "i32" or "u32" or "f32" => 4,
        "i64" => 8,
        _ => throw new InvalidOperationException("Unsupported type " + type)
    };

    private static bool TryGetModuleBase(int pid, out long moduleBase)
    {
        moduleBase = 0;
        try
        {
            var proc = System.Diagnostics.Process.GetProcessById(pid);
            var baseAddr = proc.MainModule?.BaseAddress ?? IntPtr.Zero;
            if (baseAddr == IntPtr.Zero)
                return false;
            moduleBase = baseAddr.ToInt64();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose() => Detach();

    private const uint ProcessAllAccess = 0x001F0FFF;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, [Out] byte[] lpBuffer, int dwSize, out int lpNumberOfBytesRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, int dwSize, out int lpNumberOfBytesWritten);
}
