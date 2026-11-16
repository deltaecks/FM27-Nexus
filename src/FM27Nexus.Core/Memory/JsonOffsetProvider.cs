using System.Text.Json;
using FM27Nexus.Core.Paths;

namespace FM27Nexus.Core.Memory;

public sealed class JsonOffsetProvider : IOffsetProvider
{
    private readonly string[] _searchRoots;
    private OffsetProfile? _active;
    private string? _activePath;

    public JsonOffsetProvider(AppPaths paths, string? bundledOffsetsDirectory = null)
    {
        var roots = new List<string> { paths.Offsets };
        if (!string.IsNullOrWhiteSpace(bundledOffsetsDirectory) && Directory.Exists(bundledOffsetsDirectory))
            roots.Add(bundledOffsetsDirectory);
        var repoOffsets = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "offsets"));
        if (Directory.Exists(repoOffsets))
            roots.Add(repoOffsets);
        _searchRoots = roots.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        paths.EnsureCreated();
    }

    public OffsetProfile? ActiveProfile => _active;

    public IReadOnlyList<string> AvailableProfilePaths =>
        _searchRoots.Where(Directory.Exists)
            .SelectMany(r => Directory.EnumerateFiles(r, "*.json", SearchOption.TopDirectoryOnly))
            .Where(f => !Path.GetFileName(f).Equals("schema.example.json", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(f => f)
            .ToList();

    public OffsetLoadResult LoadForBuild(string buildId, string storefront)
    {
        var expected = $"{Sanitize(buildId)}_{Sanitize(storefront)}.json";
        foreach (var root in _searchRoots.Where(Directory.Exists))
        {
            var path = Path.Combine(root, expected);
            if (File.Exists(path))
                return LoadPath(path, buildId, storefront);
        }

        // Try any profile that matches build+storefront fields inside JSON.
        foreach (var path in AvailableProfilePaths)
        {
            try
            {
                var doc = JsonSerializer.Deserialize<OffsetProfileDocument>(File.ReadAllText(path));
                if (doc is null) continue;
                if (string.Equals(doc.BuildId, buildId, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(doc.Storefront, storefront, StringComparison.OrdinalIgnoreCase))
                    return LoadPath(path, buildId, storefront);
            }
            catch (JsonException)
            {
                // skip malformed
            }
        }

        return new OffsetLoadResult
        {
            Ok = false,
            Error = $"No offset profile for build '{buildId}' / storefront '{storefront}'. Import a community pack into %AppData%\\FM27Nexus\\Offsets\\ as {expected}."
        };
    }

    public OffsetLoadResult ImportCommunityPack(string jsonPath)
    {
        if (!File.Exists(jsonPath))
            return new OffsetLoadResult { Ok = false, Error = "Pack file not found." };

        try
        {
            var doc = JsonSerializer.Deserialize<OffsetProfileDocument>(File.ReadAllText(jsonPath));
            if (doc is null || string.IsNullOrWhiteSpace(doc.BuildId) || string.IsNullOrWhiteSpace(doc.Storefront))
                return new OffsetLoadResult { Ok = false, Error = "Pack missing buildId/storefront." };

            if (doc.Signatures.Count == 0 || doc.Entities.Count == 0)
                return new OffsetLoadResult { Ok = false, Error = "Pack has no signatures/entities — refusing empty profile." };

            var destName = $"{Sanitize(doc.BuildId)}_{Sanitize(doc.Storefront)}.json";
            var dest = Path.Combine(_searchRoots[0], destName);
            Directory.CreateDirectory(_searchRoots[0]);
            File.Copy(jsonPath, dest, overwrite: true);
            return LoadPath(dest, doc.BuildId, doc.Storefront);
        }
        catch (Exception ex)
        {
            return new OffsetLoadResult { Ok = false, Error = ex.Message };
        }
    }

    private OffsetLoadResult LoadPath(string path, string buildId, string storefront)
    {
        try
        {
            var doc = JsonSerializer.Deserialize<OffsetProfileDocument>(File.ReadAllText(path));
            if (doc is null)
                return new OffsetLoadResult { Ok = false, Error = "Empty profile JSON." };
            if (doc.Signatures.Count == 0)
                return new OffsetLoadResult { Ok = false, Error = "Profile has zero AOB signatures." };

            var profile = doc.ToProfile();
            _active = profile;
            _activePath = path;
            return new OffsetLoadResult { Ok = true, Profile = profile, Path = path };
        }
        catch (Exception ex)
        {
            return new OffsetLoadResult { Ok = false, Error = $"Failed loading {path}: {ex.Message}" };
        }
    }

    private static string Sanitize(string value)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            value = value.Replace(c, '_');
        return value.Replace(' ', '_');
    }
}
