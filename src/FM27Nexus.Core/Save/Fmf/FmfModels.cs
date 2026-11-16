namespace FM27Nexus.Core.Save.Fmf;

public sealed record FmfDirectoryEntry(
    string Name,
    string Extension,
    long FrameOffset,
    long CompressedSize,
    long DecompressedSize,
    long TrailingA,
    long TrailingB)
{
    public bool IsSection => Extension is ".dat" or ".cmt";
    public long FrameEnd => FrameOffset + CompressedSize;
}

public sealed record FmfFingerprint(long FileSize, long MtimeUtcTicks, string HeaderDigest);

public sealed class FmfContainerIndex
{
    public required string Path { get; init; }
    public required string FileName { get; init; }
    public required string SaveName { get; init; }
    public required long TrailerOffset { get; init; }
    public required FmfFingerprint Fingerprint { get; init; }
    public required IReadOnlyList<FmfDirectoryEntry> Entries { get; init; }
    public required IReadOnlyDictionary<string, FmfDirectoryEntry> Sections { get; init; }
}
