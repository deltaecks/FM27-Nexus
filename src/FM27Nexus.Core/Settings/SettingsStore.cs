using System.Text.Json;
using FM27Nexus.Core.Paths;

namespace FM27Nexus.Core.Settings;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly AppPaths _paths;

    public SettingsStore(AppPaths paths)
    {
        _paths = paths;
    }

    public AppSettings Load()
    {
        _paths.EnsureCreated();
        if (!File.Exists(_paths.SettingsFile))
            return new AppSettings();

        var json = File.ReadAllText(_paths.SettingsFile);
        if (string.IsNullOrWhiteSpace(json))
            return new AppSettings();

        return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        if (AppPaths.IsUnderVelopackCurrent(_paths.SettingsFile))
            throw new InvalidOperationException(
                "Refusing to write settings under Velopack current folder. Use %AppData%\\FM27Nexus\\.");

        _paths.EnsureCreated();
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(_paths.SettingsFile, json);
    }
}
