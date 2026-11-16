namespace FM27Nexus.Core.Paths;

/// <summary>
/// Resolves persistent user data under %AppData%\FM27Nexus\.
/// Never store user data under %LocalAppData%\FM27Nexus\current\ (Velopack replaces it).
/// </summary>
public sealed class AppPaths
{
    public const string AppFolderName = "FM27Nexus";

    public string Root { get; }
    public string Settings { get; }
    public string Backups { get; }
    public string Offsets { get; }
    public string Templates { get; }
    public string Logs { get; }
    public string VanillaState { get; }
    public string SettingsFile => Path.Combine(Settings, "settings.json");

    public AppPaths(string? rootOverride = null)
    {
        Root = rootOverride ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            AppFolderName);
        Settings = Path.Combine(Root, "Settings");
        Backups = Path.Combine(Root, "Backups");
        Offsets = Path.Combine(Root, "Offsets");
        Templates = Path.Combine(Root, "Templates");
        Logs = Path.Combine(Root, "Logs");
        VanillaState = Path.Combine(Root, "VanillaState");
    }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(Settings);
        Directory.CreateDirectory(Backups);
        Directory.CreateDirectory(Offsets);
        Directory.CreateDirectory(Templates);
        Directory.CreateDirectory(Logs);
        Directory.CreateDirectory(VanillaState);
    }

    public static bool IsUnderVelopackCurrent(string path)
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var current = Path.Combine(local, AppFolderName, "current");
        var full = Path.GetFullPath(path);
        var currentFull = Path.GetFullPath(current);
        return full.StartsWith(currentFull, StringComparison.OrdinalIgnoreCase);
    }
}
