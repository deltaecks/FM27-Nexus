namespace FM27Nexus.Core.Models;

public sealed class GameInstallInfo
{
    public required StorefrontKind Storefront { get; init; }
    public required string DisplayName { get; init; }
    public required string InstallDirectory { get; init; }
    public required string ExecutablePath { get; init; }
    public required string ProcessName { get; init; }
    public string? PackageFamilyName { get; init; }
    public string? VersionString { get; init; }
    public bool ExecutableExists => File.Exists(ExecutablePath);

    public override string ToString() =>
        $"{DisplayName} [{Storefront}] — {ExecutablePath}";
}
