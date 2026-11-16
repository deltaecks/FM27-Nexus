using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using FM27Nexus.Core.Paths;

namespace FM27Nexus.Core.Mods;

public sealed class ModManagerService
{
    private readonly AppPaths _paths;

    public ModManagerService(AppPaths paths)
    {
        _paths = paths;
        _paths.EnsureCreated();
    }

    public ModInstallResult Install(string sourcePath, Fm27UserFolders folders)
    {
        if (!File.Exists(sourcePath) && !Directory.Exists(sourcePath))
            throw new FileNotFoundException("Mod source not found.", sourcePath);

        var kind = DetectKind(sourcePath);
        return kind switch
        {
            ModKind.Skin => InstallToFolder(sourcePath, folders.Skin, ModKind.Skin),
            ModKind.EditorData => InstallToFolder(sourcePath, folders.EditorData, ModKind.EditorData),
            ModKind.Graphics => InstallToFolder(sourcePath, folders.Graphics, ModKind.Graphics),
            ModKind.BepInExPlugin => InstallBepInEx(sourcePath, folders.GameInstallDirectory),
            ModKind.AssetBundle => InstallAssetBundles(sourcePath, folders.GameInstallDirectory),
            _ => throw new InvalidOperationException("Unrecognized mod type. Supported: skin, editor data (.fmf), graphics, BepInEx plugins, AssetBundles.")
        };
    }

    public VanillaStateSnapshot CreateVanillaState(string gameInstallDirectory)
    {
        var aa = ResolveAssetBundleDir(gameInstallDirectory);
        if (!Directory.Exists(aa))
            throw new DirectoryNotFoundException("AssetBundle directory not found: " + aa);

        var stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var destRoot = Path.Combine(_paths.VanillaState, stamp);
        Directory.CreateDirectory(destRoot);
        var catalog = Path.Combine(aa, "catalog.bin");
        if (File.Exists(catalog))
            File.Copy(catalog, Path.Combine(destRoot, "catalog.bin"), true);

        var files = Directory.EnumerateFiles(aa, "*", SearchOption.TopDirectoryOnly).ToList();
        foreach (var file in files)
        {
            var name = Path.GetFileName(file);
            File.Copy(file, Path.Combine(destRoot, name), true);
        }

        var manifest = new VanillaStateSnapshot
        {
            Id = stamp,
            CreatedUtc = DateTime.UtcNow,
            SourceDirectory = aa,
            SnapshotDirectory = destRoot,
            FileCount = files.Count,
            CatalogSha256 = File.Exists(catalog) ? Sha256(catalog) : null
        };
        File.WriteAllText(Path.Combine(destRoot, "manifest.json"),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
        return manifest;
    }

    public void RestoreVanillaState(string snapshotId, string gameInstallDirectory)
    {
        var snapDir = Path.Combine(_paths.VanillaState, snapshotId);
        if (!Directory.Exists(snapDir))
            throw new DirectoryNotFoundException("VanillaState snapshot not found: " + snapshotId);

        var aa = ResolveAssetBundleDir(gameInstallDirectory);
        Directory.CreateDirectory(aa);
        foreach (var file in Directory.EnumerateFiles(snapDir))
        {
            var name = Path.GetFileName(file);
            if (name.Equals("manifest.json", StringComparison.OrdinalIgnoreCase))
                continue;
            File.Copy(file, Path.Combine(aa, name), overwrite: true);
        }
    }

    public IReadOnlyList<VanillaStateSnapshot> ListVanillaSnapshots()
    {
        if (!Directory.Exists(_paths.VanillaState))
            return [];

        var list = new List<VanillaStateSnapshot>();
        foreach (var dir in Directory.EnumerateDirectories(_paths.VanillaState))
        {
            var manifest = Path.Combine(dir, "manifest.json");
            if (!File.Exists(manifest)) continue;
            var snap = JsonSerializer.Deserialize<VanillaStateSnapshot>(File.ReadAllText(manifest));
            if (snap is not null) list.Add(snap);
        }

        return list.OrderByDescending(s => s.CreatedUtc).ToList();
    }

    private ModInstallResult InstallToFolder(string sourcePath, string destinationRoot, ModKind kind)
    {
        Directory.CreateDirectory(destinationRoot);
        if (Directory.Exists(sourcePath))
        {
            var dest = Path.Combine(destinationRoot, Path.GetFileName(sourcePath));
            CopyDirectory(sourcePath, dest);
            return new ModInstallResult(kind, dest, 1);
        }

        if (sourcePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            ZipFile.ExtractToDirectory(sourcePath, destinationRoot, overwriteFiles: true);
            return new ModInstallResult(kind, destinationRoot, 1);
        }

        var destFile = Path.Combine(destinationRoot, Path.GetFileName(sourcePath));
        File.Copy(sourcePath, destFile, overwrite: true);
        return new ModInstallResult(kind, destFile, 1);
    }

    private ModInstallResult InstallBepInEx(string sourcePath, string? gameInstallDirectory)
    {
        if (string.IsNullOrWhiteSpace(gameInstallDirectory) || !Directory.Exists(gameInstallDirectory))
            throw new InvalidOperationException("Game install directory is required for BepInEx plugins.");

        var plugins = Path.Combine(gameInstallDirectory, "BepInEx", "plugins");
        Directory.CreateDirectory(plugins);
        return InstallToFolder(sourcePath, plugins, ModKind.BepInExPlugin);
    }

    private ModInstallResult InstallAssetBundles(string sourcePath, string? gameInstallDirectory)
    {
        if (string.IsNullOrWhiteSpace(gameInstallDirectory) || !Directory.Exists(gameInstallDirectory))
            throw new InvalidOperationException("Game install directory is required for AssetBundles.");

        // Always snapshot before first AssetBundle install in this session folder if none exist.
        if (!ListVanillaSnapshots().Any())
            CreateVanillaState(gameInstallDirectory);

        var aa = ResolveAssetBundleDir(gameInstallDirectory);
        Directory.CreateDirectory(aa);
        return InstallToFolder(sourcePath, aa, ModKind.AssetBundle);
    }

    public static ModKind DetectKind(string sourcePath)
    {
        var name = Path.GetFileName(sourcePath);
        var full = Path.GetFullPath(sourcePath);
        if (name.EndsWith(".fmf", StringComparison.OrdinalIgnoreCase))
            return ModKind.EditorData;
        if (full.Contains($"{Path.DirectorySeparatorChar}skin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            || name.Contains("skin", StringComparison.OrdinalIgnoreCase))
            return ModKind.Skin;
        if (name.Contains("graphics", StringComparison.OrdinalIgnoreCase)
            || name.Contains("logo", StringComparison.OrdinalIgnoreCase)
            || name.Contains("faces", StringComparison.OrdinalIgnoreCase)
            || name.Contains("kits", StringComparison.OrdinalIgnoreCase))
            return ModKind.Graphics;
        if (name.Contains("BepInEx", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            return ModKind.BepInExPlugin;
        if (name.Equals("catalog.bin", StringComparison.OrdinalIgnoreCase)
            || full.Contains("StandaloneWindows64", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".bundle", StringComparison.OrdinalIgnoreCase))
            return ModKind.AssetBundle;

        // Folder heuristics
        if (Directory.Exists(sourcePath))
        {
            if (Directory.Exists(Path.Combine(sourcePath, "panels")) || Directory.Exists(Path.Combine(sourcePath, "fonts")))
                return ModKind.Skin;
            if (File.Exists(Path.Combine(sourcePath, "catalog.bin")))
                return ModKind.AssetBundle;
        }

        return ModKind.Unknown;
    }

    private static string ResolveAssetBundleDir(string gameInstallDirectory)
    {
        var candidates = new[]
        {
            Path.Combine(gameInstallDirectory, "fm_Data", "StreamingAssets", "aa", "StandaloneWindows64"),
            Path.Combine(gameInstallDirectory, "Content", "fm_Data", "StreamingAssets", "aa", "StandaloneWindows64")
        };
        return candidates.FirstOrDefault(Directory.Exists) ?? candidates[0];
    }

    private static void CopyDirectory(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(dir.Replace(source, dest));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = file.Replace(source, dest);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, true);
        }
    }

    private static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}

public enum ModKind
{
    Unknown,
    Skin,
    EditorData,
    Graphics,
    BepInExPlugin,
    AssetBundle
}

public sealed record ModInstallResult(ModKind Kind, string Destination, int Items);

public sealed class Fm27UserFolders
{
    public required string Skin { get; init; }
    public required string EditorData { get; init; }
    public required string Graphics { get; init; }
    public string? GameInstallDirectory { get; init; }
}

public sealed class VanillaStateSnapshot
{
    public required string Id { get; init; }
    public required DateTime CreatedUtc { get; init; }
    public required string SourceDirectory { get; init; }
    public required string SnapshotDirectory { get; init; }
    public required int FileCount { get; init; }
    public string? CatalogSha256 { get; init; }
}
