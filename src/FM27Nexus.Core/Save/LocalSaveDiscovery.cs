using FM27Nexus.Core.Paths;

namespace FM27Nexus.Core.Save;

public sealed class LocalSaveDiscovery
{
    private readonly DocumentsPathResolver _documents;

    public LocalSaveDiscovery(DocumentsPathResolver? documents = null)
    {
        _documents = documents ?? new DocumentsPathResolver();
    }

    public string GetGamesDirectory(string? documentsOverride = null)
    {
        var layout = _documents.Resolve(documentsOverride);
        return layout.Games;
    }

    public IReadOnlyList<LocalSaveInfo> ListSaves(string? documentsOverride = null)
    {
        var games = GetGamesDirectory(documentsOverride);
        if (!Directory.Exists(games))
            return Array.Empty<LocalSaveInfo>();

        return Directory.EnumerateFiles(games, "*.fm", SearchOption.TopDirectoryOnly)
            .Select(path =>
            {
                var info = new FileInfo(path);
                return new LocalSaveInfo
                {
                    Path = path,
                    Name = Path.GetFileNameWithoutExtension(path),
                    Length = info.Length,
                    LastWriteUtc = info.LastWriteTimeUtc,
                    IsAutoBackup = IsAutoBackupName(info.Name)
                };
            })
            .OrderByDescending(s => s.LastWriteUtc)
            .ToList();
    }

    /// <summary>
    /// Picks the most recently written career save, preferring non-auto-backup filenames.
    /// </summary>
    public LocalSaveInfo? FindCurrentSave(string? documentsOverride = null)
    {
        var saves = ListSaves(documentsOverride);
        if (saves.Count == 0)
            return null;

        var preferred = saves.FirstOrDefault(s => !s.IsAutoBackup);
        return preferred ?? saves[0];
    }

    private static bool IsAutoBackupName(string fileName)
    {
        return fileName.Contains("last save overwrite backup", StringComparison.OrdinalIgnoreCase)
               || fileName.Contains("autosave", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class LocalSaveInfo
{
    public required string Path { get; init; }
    public required string Name { get; init; }
    public required long Length { get; init; }
    public required DateTime LastWriteUtc { get; init; }
    public required bool IsAutoBackup { get; init; }
}
