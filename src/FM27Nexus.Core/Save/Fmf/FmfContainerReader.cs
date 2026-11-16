using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using ZstdSharp;

namespace FM27Nexus.Core.Save.Fmf;

/// <summary>
/// FMF container reader modeled on the MIT-licensed fmsave _container.py architecture
/// (magic, trailer directory, Zstandard frames). Does not invent table layouts.
/// </summary>
public sealed class FmfContainerReader
{
    public static readonly byte[] FileMagic = [0x02, 0x01, (byte)'f', (byte)'m', (byte)'f', (byte)'.'];
    public const int HeaderSize = 26;
    public const int TrailerPointerAt = 9;
    public const int TrailerHeaderSize = 9;
    public const int FrameBase = 26;
    public const int EntryFixedTailBytes = 40;
    public const int MaxSaveNameBytes = 4096;
    public const int MaxEntryNameBytes = 256;
    public const int MaxDirectoryEntries = 100_000;
    public const long MaxTrailerCompressedBytes = 64L * 1024 * 1024;
    public const long MaxTrailerDecompressedBytes = 64L * 1024 * 1024;
    public const long ExpansionAllowance = 64L * 1024 * 1024;
    public const int MaxPartExpansion = 64;
    public const int MaxSaveExpansion = 32;

    private static readonly Regex EntryNamePattern = new("^[A-Za-z0-9_]+$", RegexOptions.Compiled);
    private static readonly Regex ExtensionPattern = new("^\\.[A-Za-z0-9]{3}$", RegexOptions.Compiled);

    public FmfContainerIndex ReadIndex(string path)
    {
        var savePath = Path.GetFullPath(path);
        var fileName = Path.GetFileName(savePath);
        var info = new FileInfo(savePath);
        if (!info.Exists)
            throw new FileNotFoundException("Save file not found.", savePath);

        using var fs = File.OpenRead(savePath);
        var fileSize = fs.Length;
        var header = new byte[HeaderSize];
        var read = fs.Read(header, 0, HeaderSize);
        if (read < FileMagic.Length || !header.AsSpan(0, FileMagic.Length).SequenceEqual(FileMagic))
            throw new NotAFmSaveException($"{fileName} is not a Football Manager save.");
        if (read < HeaderSize)
            throw new CorruptSaveException($"{fileName} is too short to be a save.");

        var trailerOffset = TrailerPointerAt + (long)BinaryPrimitives.ReadUInt64LittleEndian(header.AsSpan(TrailerPointerAt));
        if (trailerOffset < HeaderSize || trailerOffset >= fileSize - TrailerHeaderSize)
            throw new CorruptSaveException($"{fileName}: the save directory is missing.");
        if (fileSize - trailerOffset - TrailerHeaderSize > MaxTrailerCompressedBytes)
            throw new CorruptSaveException($"{fileName}: the save directory is implausibly large.");

        var trailerLen = (int)(fileSize - trailerOffset);
        var trailerBytes = new byte[trailerLen];
        fs.Seek(trailerOffset, SeekOrigin.Begin);
        if (fs.Read(trailerBytes, 0, trailerLen) != trailerLen)
            throw new CorruptSaveException($"{fileName}: failed reading trailer.");

        if (!trailerBytes.AsSpan(0, FileMagic.Length).SequenceEqual(FileMagic))
            throw new CorruptSaveException($"{fileName}: the save directory is damaged.");

        var trailerCompressed = trailerBytes.AsSpan(TrailerHeaderSize).ToArray();
        var trailerPayload = DecompressFrame(trailerCompressed, null, MaxTrailerDecompressedBytes, "the save directory", fileName);
        var (saveName, entries) = ParseDirectory(trailerPayload, fileName);
        ValidateEntries(entries, trailerOffset, fileName, fileSize);

        var sections = entries.Where(e => e.IsSection)
            .GroupBy(e => e.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        if (sections.Count != entries.Count(e => e.IsSection))
            throw new CorruptSaveException($"{fileName}: the save directory lists a section twice.");

        var digest = Convert.ToHexString(SHA256.HashData(header.Concat(trailerBytes).ToArray())).ToLowerInvariant();
        return new FmfContainerIndex
        {
            Path = savePath,
            FileName = fileName,
            SaveName = saveName,
            TrailerOffset = trailerOffset,
            Fingerprint = new FmfFingerprint(fileSize, info.LastWriteTimeUtc.Ticks, digest),
            Entries = entries,
            Sections = sections
        };
    }

    public byte[] ReadSection(FmfContainerIndex index, string sectionName)
    {
        if (!index.Sections.TryGetValue(sectionName, out var entry))
            throw new KeyNotFoundException($"Section '{sectionName}' was not found in {index.FileName}.");

        VerifyFingerprint(index);
        using var fs = File.OpenRead(index.Path);
        var compressed = new byte[entry.CompressedSize];
        fs.Seek(entry.FrameOffset, SeekOrigin.Begin);
        if (fs.Read(compressed, 0, compressed.Length) != compressed.Length)
            throw new CorruptSaveException($"{index.FileName}: truncated section {sectionName}.");

        var bound = PartBound(entry.CompressedSize);
        return DecompressFrame(compressed, entry.DecompressedSize, bound, $"section {sectionName}", index.FileName);
    }

    public SaveIntegrityReport Validate(string path)
    {
        var issues = new List<string>();
        try
        {
            var index = ReadIndex(path);
            var sectionNames = index.Sections.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
            return new SaveIntegrityReport
            {
                Ok = true,
                Path = index.Path,
                SaveName = index.SaveName,
                FileSize = index.Fingerprint.FileSize,
                SectionCount = index.Sections.Count,
                EntryCount = index.Entries.Count,
                SectionNames = sectionNames,
                HeaderDigest = index.Fingerprint.HeaderDigest,
                Issues = issues
            };
        }
        catch (Exception ex) when (ex is NotAFmSaveException or CorruptSaveException or FileNotFoundException)
        {
            issues.Add(ex.Message);
            return new SaveIntegrityReport
            {
                Ok = false,
                Path = path,
                SaveName = null,
                FileSize = File.Exists(path) ? new FileInfo(path).Length : 0,
                SectionCount = 0,
                EntryCount = 0,
                SectionNames = Array.Empty<string>(),
                HeaderDigest = null,
                Issues = issues
            };
        }
    }

    private void VerifyFingerprint(FmfContainerIndex index)
    {
        var info = new FileInfo(index.Path);
        if (!info.Exists || info.Length != index.Fingerprint.FileSize || info.LastWriteTimeUtc.Ticks != index.Fingerprint.MtimeUtcTicks)
            throw new SaveChangedException($"{index.FileName} changed on disk since it was opened.");
    }

    private static long PartBound(long compressedSize) =>
        Math.Max(ExpansionAllowance, MaxPartExpansion * compressedSize);

    private static long SaveBound(long fileSize) =>
        Math.Max(ExpansionAllowance, MaxSaveExpansion * fileSize);

    private static void ValidateEntries(IReadOnlyList<FmfDirectoryEntry> entries, long trailerOffset, string fileName, long fileSize)
    {
        long previousEnd = HeaderSize;
        long totalDecompressed = 0;
        foreach (var entry in entries)
        {
            var label = $"directory entry {entry.Name}{entry.Extension}";
            if (entry.CompressedSize == 0 || entry.FrameOffset < previousEnd || entry.FrameEnd > trailerOffset)
                throw new CorruptSaveException($"{fileName}: {label} points outside the frame area.");
            if (entry.DecompressedSize > PartBound(entry.CompressedSize))
                throw new CorruptSaveException($"{fileName}: {label} claims an implausible decompressed size.");
            totalDecompressed += entry.DecompressedSize;
            previousEnd = entry.FrameEnd;
        }

        if (totalDecompressed > SaveBound(fileSize))
            throw new CorruptSaveException($"{fileName}: claimed decompressed size exceeds safe bounds.");
    }

    private static (string SaveName, IReadOnlyList<FmfDirectoryEntry> Entries) ParseDirectory(byte[] payload, string fileName)
    {
        var (saveName, cursor) = ReadLengthPrefixedString(payload, 0, MaxSaveNameBytes);
        cursor += 4; // unidentified u32
        var entries = new List<FmfDirectoryEntry>();
        while (TryParseDirectoryEntry(payload, cursor, out var entry, out cursor))
        {
            entries.Add(entry);
            if (entries.Count > MaxDirectoryEntries)
                throw new CorruptSaveException($"{fileName}: too many directory entries.");
        }

        if (!entries.Any(e => e.IsSection))
            throw new CorruptSaveException($"{fileName}: the save directory lists no sections.");

        return (saveName, entries.OrderBy(e => e.FrameOffset).ToList());
    }

    private static bool TryParseDirectoryEntry(byte[] payload, int cursor, out FmfDirectoryEntry entry, out int next)
    {
        entry = null!;
        next = cursor;
        if (cursor + 4 > payload.Length)
            return false;

        var nameLength = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(cursor));
        if (nameLength is < 1 or > MaxEntryNameBytes)
            return false;

        var extensionLengthAt = cursor + 4 + (int)nameLength;
        var tailAt = extensionLengthAt + 8;
        if (tailAt + EntryFixedTailBytes > payload.Length)
            return false;

        var nameBytes = payload.AsSpan(cursor + 4, (int)nameLength);
        var extensionBytes = payload.AsSpan(extensionLengthAt + 4, 4);
        var name = Encoding.ASCII.GetString(nameBytes);
        var extension = Encoding.ASCII.GetString(extensionBytes);
        if (!EntryNamePattern.IsMatch(name)
            || BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(extensionLengthAt)) != 4
            || !ExtensionPattern.IsMatch(extension))
            return false;

        var relativeOffset = BinaryPrimitives.ReadUInt64LittleEndian(payload.AsSpan(tailAt));
        var compressed = BinaryPrimitives.ReadUInt64LittleEndian(payload.AsSpan(tailAt + 8));
        var decompressed = BinaryPrimitives.ReadUInt64LittleEndian(payload.AsSpan(tailAt + 16));
        var trailingA = BinaryPrimitives.ReadUInt64LittleEndian(payload.AsSpan(tailAt + 24));
        var trailingB = BinaryPrimitives.ReadUInt64LittleEndian(payload.AsSpan(tailAt + 32));

        entry = new FmfDirectoryEntry(
            name,
            extension,
            FrameBase + (long)relativeOffset,
            (long)compressed,
            (long)decompressed,
            (long)trailingA,
            (long)trailingB);
        next = tailAt + EntryFixedTailBytes;
        return true;
    }

    private static (string Value, int Next) ReadLengthPrefixedString(byte[] data, int offset, int maxBytes)
    {
        if (offset + 4 > data.Length)
            throw new CorruptSaveException("Truncated length-prefixed string.");
        var length = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset));
        if (length > maxBytes || offset + 4 + length > data.Length)
            throw new CorruptSaveException("Invalid length-prefixed string.");
        var value = Encoding.UTF8.GetString(data, offset + 4, (int)length);
        return (value, offset + 4 + (int)length);
    }

    private static byte[] DecompressFrame(byte[] compressed, long? expectedSize, long cap, string what, string fileName)
    {
        var outputLimit = expectedSize is null ? cap : Math.Min(expectedSize.Value, cap);
        try
        {
            using var decompressor = new Decompressor();
            var output = decompressor.Unwrap(compressed).ToArray();
            if (output.Length > outputLimit)
                throw new CorruptSaveException($"{fileName}: {what} decompresses past safe bounds.");
            if (expectedSize is not null && output.Length != expectedSize.Value)
                throw new CorruptSaveException($"{fileName}: {what} decompressed to {output.Length}, expected {expectedSize}.");
            return output;
        }
        catch (Exception ex) when (ex is not CorruptSaveException)
        {
            throw new CorruptSaveException($"{fileName}: {what} could not be decompressed ({ex.Message}).");
        }
    }
}

public sealed class SaveIntegrityReport
{
    public required bool Ok { get; init; }
    public required string Path { get; init; }
    public string? SaveName { get; init; }
    public required long FileSize { get; init; }
    public required int SectionCount { get; init; }
    public required int EntryCount { get; init; }
    public required IReadOnlyList<string> SectionNames { get; init; }
    public string? HeaderDigest { get; init; }
    public required IReadOnlyList<string> Issues { get; init; }
}
