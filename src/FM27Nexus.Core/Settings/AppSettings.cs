namespace FM27Nexus.Core.Settings;

public sealed class AppSettings
{
    public string? DocumentsPathOverride { get; set; }
    public string? PreferredInstallDirectory { get; set; }
    public string GitHubRepoUrl { get; set; } = "https://github.com/deltaecks/FM27-Nexus";
    public bool CheckForUpdatesOnStartup { get; set; } = true;
}
