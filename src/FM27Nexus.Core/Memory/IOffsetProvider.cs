namespace FM27Nexus.Core.Memory;

public interface IOffsetProvider
{
    OffsetProfile? ActiveProfile { get; }
    IReadOnlyList<string> AvailableProfilePaths { get; }
    OffsetLoadResult LoadForBuild(string buildId, string storefront);
    OffsetLoadResult ImportCommunityPack(string jsonPath);
}

public sealed class OffsetLoadResult
{
    public required bool Ok { get; init; }
    public string? Error { get; init; }
    public OffsetProfile? Profile { get; init; }
    public string? Path { get; init; }
}
