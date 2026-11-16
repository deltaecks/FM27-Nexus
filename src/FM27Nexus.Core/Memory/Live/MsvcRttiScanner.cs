using System.Buffers.Binary;
using System.Text;

namespace FM27Nexus.Core.Memory.Live;

/// <summary>
/// MSVC x64 RTTI class/vtable discovery and heap instance scan.
/// Algorithm matches the publicly documented OpenEditor research approach
/// (TypeDescriptor → CompleteObjectLocator → vtable → heap pointers).
/// </summary>
public static class MsvcRttiScanner
{
    private const int MaxNameLength = 512;

    public static IReadOnlyList<RttiClass> FindClasses(byte[] image, long moduleBase)
    {
        var typeDescriptors = new Dictionary<uint, (long Address, string Mangled)>();
        var marker = ".?A"u8.ToArray();
        var pos = 0;
        while (true)
        {
            var i = image.AsSpan(pos).IndexOf(marker);
            if (i < 0) break;
            pos += i;
            var nameAt = pos;
            pos++;
            if (nameAt < 16 || nameAt + 4 >= image.Length) continue;
            if (image[nameAt + 3] is not ((byte)'V' or (byte)'U')) continue;
            if (BinaryPrimitives.ReadUInt64LittleEndian(image.AsSpan(nameAt - 16, 8)) == 0) continue;
            var name = ReadAsciiZ(image, nameAt);
            if (name is null) continue;
            var td = moduleBase + nameAt - 16;
            typeDescriptors[(uint)(td - moduleBase)] = (td, name);
        }

        if (typeDescriptors.Count == 0)
            return Array.Empty<RttiClass>();

        var locators = new Dictionary<long, (uint TdRva, int Offset)>();
        for (var i = 0; i + 24 <= image.Length; i += 4)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(i, 4)) != 1) continue;
            var col = moduleBase + i;
            if (BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(i + 20, 4)) != (uint)(col - moduleBase))
                continue;
            var tdRva = BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(i + 12, 4));
            if (!typeDescriptors.ContainsKey(tdRva)) continue;
            locators[col] = (tdRva, BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(i + 4, 4)));
        }

        var vtables = new Dictionary<uint, List<RttiVtable>>();
        for (var i = 0; i + 8 <= image.Length; i += 8)
        {
            var v = BinaryPrimitives.ReadInt64LittleEndian(image.AsSpan(i, 8));
            if (!locators.TryGetValue(v, out var col)) continue;
            if (!vtables.TryGetValue(col.TdRva, out var list))
                vtables[col.TdRva] = list = [];
            list.Add(new RttiVtable(moduleBase + i + 8, v, col.Offset));
        }

        return typeDescriptors
            .Select(kv => new RttiClass(
                Demangle(kv.Value.Mangled),
                kv.Value.Mangled,
                kv.Value.Address,
                vtables.TryGetValue(kv.Key, out var list)
                    ? list.OrderBy(x => x.ObjectOffset).ToList()
                    : []))
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static List<long> FindInstances(
        NativeProcessMemory memory,
        IReadOnlyCollection<RttiVtable> vtables,
        long moduleBase,
        long moduleSize,
        int maxSamples = 2_000_000,
        CancellationToken cancellationToken = default)
    {
        var offsets = new Dictionary<long, int>();
        foreach (var v in vtables)
            offsets[v.Address] = v.ObjectOffset;
        if (offsets.Count == 0)
            return [];

        var min = offsets.Keys.Min();
        var max = offsets.Keys.Max();
        var results = new List<long>();
        var seen = new HashSet<long>();

        var regions = memory.EnumerateCommittedRegions(writableOnly: true)
            .Where(r => r.BaseAddress + r.Size <= moduleBase || r.BaseAddress >= moduleBase + moduleSize)
            .Where(IsLikelyHeapWritable)
            .ToList();

        var chunk = new byte[1024 * 1024];
        foreach (var region in regions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (long off = 0; off < region.Size; off += chunk.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var address = region.BaseAddress + off;
                var len = (int)Math.Min(chunk.Length, region.Size - off);
                var slice = chunk.AsSpan(0, len);
                if (!memory.TryReadBytes(address, slice))
                    continue;

                var first = (int)((8 - address % 8) % 8);
                for (var i = first; i + 8 <= len; i += 8)
                {
                    var v = BinaryPrimitives.ReadInt64LittleEndian(slice.Slice(i, 8));
                    if (v < min || v > max || !offsets.TryGetValue(v, out var objectOffset))
                        continue;
                    var obj = address + i - objectOffset;
                    if (seen.Add(obj))
                    {
                        results.Add(obj);
                        if (results.Count >= maxSamples)
                            return results;
                    }
                }
            }
        }

        return results;
    }

    public static string Demangle(string mangled)
    {
        if (!mangled.StartsWith(".?A", StringComparison.Ordinal) || mangled.Length < 5)
            return mangled;
        var body = mangled[4..];
        if (body.EndsWith("@@", StringComparison.Ordinal))
            body = body[..^2];
        if (body.Contains('?') || body.Contains('$'))
            return body;
        var parts = body.Split('@', StringSplitOptions.RemoveEmptyEntries);
        Array.Reverse(parts);
        return string.Join("::", parts);
    }

    private static bool IsLikelyHeapWritable(MemoryBasicInfo region)
    {
        // Skip tiny / absurd regions; committed process heap for FM is large.
        return region.Size >= 0x1000 && region.Size <= 0x40000000;
    }

    private static string? ReadAsciiZ(byte[] data, int start)
    {
        var end = start;
        while (end < data.Length && end - start < MaxNameLength && data[end] != 0)
        {
            if (data[end] < 0x20 || data[end] > 0x7E)
                return null;
            end++;
        }

        if (end >= data.Length || data[end] != 0 || end < start + 2)
            return null;
        if (!"@@"u8.SequenceEqual(data.AsSpan(end - 2, 2)))
            return null;
        return Encoding.ASCII.GetString(data, start, end - start);
    }
}

public sealed record RttiVtable(long Address, long CompleteObjectLocator, int ObjectOffset);

public sealed record RttiClass(string Name, string MangledName, long TypeDescriptor, IReadOnlyList<RttiVtable> Vtables)
{
    public RttiVtable? PrimaryVtable =>
        Vtables.FirstOrDefault(v => v.ObjectOffset == 0) ?? Vtables.FirstOrDefault();
}
