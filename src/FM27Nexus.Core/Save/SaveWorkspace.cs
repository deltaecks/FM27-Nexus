using FM27Nexus.Core.Analytics;
using FM27Nexus.Core.Paths;
using FM27Nexus.Core.Save.Fmf;
using FM27Nexus.Core.Save.Import;
using FM27Nexus.Core.Tactics;

namespace FM27Nexus.Core.Save;

public sealed class SaveWorkspace
{
    private readonly AppPaths _paths;
    private readonly FmfContainerReader _fmf = new();
    private readonly FmsaveCliBridge _bridge = new();
    private readonly FmHtmlCsvImporter _importer = new();
    private readonly ScoutTableJsonLoader _jsonLoader = new();
    private readonly MoneyballCalculator _moneyball = new();
    private readonly DualPhaseFitScorer _fit = new();
    private readonly LocalSaveDiscovery _discovery = new();

    public SaveWorkspace(AppPaths paths)
    {
        _paths = paths;
    }

    public IReadOnlyList<ScoutPlayer> Players { get; private set; } = Array.Empty<ScoutPlayer>();
    public SaveIntegrityReport? LastIntegrity { get; private set; }
    public string? LoadedPath { get; private set; }
    public string Status { get; private set; } = "No save loaded.";

    public LocalSaveInfo? FindCurrentSave(string? documentsOverride = null) =>
        _discovery.FindCurrentSave(documentsOverride);

    public async Task LoadFmAsync(
        string path,
        DatabaseNameMap? nameMap = null,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        progress?.Report("Validating FMF container…");
        LastIntegrity = _fmf.Validate(path);
        if (!LastIntegrity.Ok)
        {
            Players = Array.Empty<ScoutPlayer>();
            LoadedPath = path;
            Status = "Integrity failed: " + string.Join("; ", LastIntegrity.Issues);
            return;
        }

        _paths.EnsureCreated();
        var cache = Path.Combine(_paths.Root, "Cache");
        Directory.CreateDirectory(cache);
        var jsonPath = Path.Combine(cache, Path.GetFileNameWithoutExtension(path) + ".players.json");

        // Reuse cache if save unchanged and cache exists.
        var saveWrite = File.GetLastWriteTimeUtc(path);
        var cacheMeta = jsonPath + ".meta";
        if (File.Exists(jsonPath) && File.Exists(cacheMeta))
        {
            var meta = await File.ReadAllTextAsync(cacheMeta, cancellationToken).ConfigureAwait(false);
            if (meta.Trim() == saveWrite.Ticks.ToString())
            {
                progress?.Report("Loading cached player table…");
                Players = _jsonLoader.Load(jsonPath);
                ApplyDerived(Players, nameMap);
                LoadedPath = path;
                Status = $"Loaded {Players.Count} players from cache ({Path.GetFileName(path)}).";
                return;
            }
        }

        try
        {
            Players = await _bridge.ExportPlayersAsync(path, jsonPath, progress, cancellationToken).ConfigureAwait(false);
            await File.WriteAllTextAsync(cacheMeta, saveWrite.Ticks.ToString(), cancellationToken).ConfigureAwait(false);
            ApplyDerived(Players, nameMap);
            LoadedPath = path;
            Status = $"Loaded {Players.Count} players from .fm via fmsave ({Path.GetFileName(path)}). Sections={LastIntegrity.SectionCount}.";
        }
        catch (Exception ex)
        {
            Players = Array.Empty<ScoutPlayer>();
            LoadedPath = path;
            Status =
                $"FMF OK ({LastIntegrity.SectionCount} sections) but player tables require fmsave: {ex.Message}. " +
                "Install with `pip install fmsave`, restart Nexus, or import an FM HTML/CSV export.";
        }
    }

    public void LoadImport(string path, DatabaseNameMap? nameMap = null)
    {
        var ext = Path.GetExtension(path);
        Players = ext.Equals(".json", StringComparison.OrdinalIgnoreCase)
            ? _jsonLoader.Load(path)
            : _importer.Import(path);
        ApplyDerived(Players, nameMap);
        LoadedPath = path;
        Status = $"Loaded {Players.Count} players from {ext}.";
    }

    private void ApplyDerived(IReadOnlyList<ScoutPlayer> players, DatabaseNameMap? nameMap)
    {
        _ = nameMap;
        RcaCalculator.Apply(players);
        _moneyball.ApplyPer90(players);
        _fit.ApplyBestFits(players, DualPhaseFitScorer.DefaultRoles());
    }
}
