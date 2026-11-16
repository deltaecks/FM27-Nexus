using System.Security.Cryptography;
using System.Text.Json;
using FM27Nexus.Core.Paths;
using FM27Nexus.Core.Save.Fmf;

namespace FM27Nexus.Core.Vault;

public sealed class SaveVaultService
{
    private readonly AppPaths _paths;
    private readonly FmfContainerReader _fmf = new();
    private readonly string _manifestPath;

    public SaveVaultService(AppPaths paths)
    {
        _paths = paths;
        _paths.EnsureCreated();
        _manifestPath = Path.Combine(_paths.Backups, "vault-manifest.json");
    }

    public IReadOnlyList<VaultEntry> ListEntries()
    {
        return LoadManifest().OrderByDescending(e => e.CreatedUtc).ToList();
    }

    public VaultEntry CreateSnapshot(string sourcePath, string? note = null)
    {
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("Save not found.", sourcePath);

        var integrity = _fmf.Validate(sourcePath);
        if (!integrity.Ok)
            throw new InvalidOperationException("Refusing to snapshot a save that failed integrity: " + string.Join("; ", integrity.Issues));

        var stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var safeName = Path.GetFileNameWithoutExtension(sourcePath);
        foreach (var c in Path.GetInvalidFileNameChars())
            safeName = safeName.Replace(c, '_');
        var destName = $"{safeName}_{stamp}.fm";
        var destPath = Path.Combine(_paths.Backups, destName);
        File.Copy(sourcePath, destPath, overwrite: false);

        var entry = new VaultEntry
        {
            Id = Guid.NewGuid().ToString("N"),
            SourcePath = Path.GetFullPath(sourcePath),
            BackupPath = destPath,
            CreatedUtc = DateTime.UtcNow,
            FileSize = new FileInfo(destPath).Length,
            Sha256 = ComputeSha256(destPath),
            SaveName = integrity.SaveName,
            SectionCount = integrity.SectionCount,
            HeaderDigest = integrity.HeaderDigest,
            Note = note
        };

        var list = LoadManifest();
        list.Add(entry);
        SaveManifest(list);
        return entry;
    }

    public void Restore(string entryId, string destinationPath)
    {
        var entry = LoadManifest().FirstOrDefault(e => e.Id == entryId)
                    ?? throw new InvalidOperationException("Vault entry not found.");
        if (!File.Exists(entry.BackupPath))
            throw new FileNotFoundException("Backup file missing.", entry.BackupPath);

        var currentHash = ComputeSha256(entry.BackupPath);
        if (!string.Equals(currentHash, entry.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Backup checksum mismatch; refuse to restore.");

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        File.Copy(entry.BackupPath, destinationPath, overwrite: true);
    }

    public CrossStoreCopyResult CopyBetweenStores(string sourcePath, string destinationDirectory)
    {
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("Source save not found.", sourcePath);
        Directory.CreateDirectory(destinationDirectory);
        var integrity = _fmf.Validate(sourcePath);
        if (!integrity.Ok)
            throw new InvalidOperationException("Integrity failed: " + string.Join("; ", integrity.Issues));

        var dest = Path.Combine(destinationDirectory, Path.GetFileName(sourcePath));
        File.Copy(sourcePath, dest, overwrite: true);
        return new CrossStoreCopyResult(sourcePath, dest, integrity.SectionCount, integrity.HeaderDigest!);
    }

    public IReadOnlyList<string> DiscoverLocalSaves(params string[] gameDirectories)
    {
        var found = new List<string>();
        foreach (var dir in gameDirectories.Where(Directory.Exists))
        {
            found.AddRange(Directory.EnumerateFiles(dir, "*.fm", SearchOption.TopDirectoryOnly));
        }

        return found.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(f => f).ToList();
    }

    private List<VaultEntry> LoadManifest()
    {
        if (!File.Exists(_manifestPath))
            return [];
        var json = File.ReadAllText(_manifestPath);
        return JsonSerializer.Deserialize<List<VaultEntry>>(json) ?? [];
    }

    private void SaveManifest(List<VaultEntry> entries)
    {
        var json = JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_manifestPath, json);
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}

public sealed class VaultEntry
{
    public required string Id { get; init; }
    public required string SourcePath { get; init; }
    public required string BackupPath { get; init; }
    public required DateTime CreatedUtc { get; init; }
    public required long FileSize { get; init; }
    public required string Sha256 { get; init; }
    public string? SaveName { get; init; }
    public int SectionCount { get; init; }
    public string? HeaderDigest { get; init; }
    public string? Note { get; init; }
}

public sealed record CrossStoreCopyResult(string SourcePath, string DestinationPath, int SectionCount, string HeaderDigest);
