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

    public SaveWorkspace(AppPaths paths)
    {
        _paths = paths;
    }

    public IReadOnlyList<ScoutPlayer> Players { get; private set; } = Array.Empty<ScoutPlayer>();
    public SaveIntegrityReport? LastIntegrity { get; private set; }
    public string? LoadedPath { get; private set; }
    public string Status { get; private set; } = "No save loaded.";

    public async Task LoadFmAsync(string path, DatabaseNameMap? nameMap = null, CancellationToken cancellationToken = default)
    {
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

        try
        {
            Players = await _bridge.ExportPlayersAsync(path, jsonPath, cancellationToken).ConfigureAwait(false);
            ApplyDerived(Players, nameMap);
            LoadedPath = path;
            Status = $"Loaded {Players.Count} players from .fm via fmsave bridge. Sections={LastIntegrity.SectionCount}.";
        }
        catch (Exception ex)
        {
            Players = Array.Empty<ScoutPlayer>();
            LoadedPath = path;
            Status =
                $"FMF OK ({LastIntegrity.SectionCount} sections) but player tables require fmsave: {ex.Message}. " +
                "Import an FM HTML/CSV export, or `pip install fmsave`.";
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
        _ = nameMap; // reserved for competition/nation/city/ground string joins on unverified name fields
        RcaCalculator.Apply(players);
        _moneyball.ApplyPer90(players);
        _fit.ApplyBestFits(players, DualPhaseFitScorer.DefaultRoles());
    }
}
