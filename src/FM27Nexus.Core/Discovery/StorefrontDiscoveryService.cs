using System.Text.Json;
using System.Text.RegularExpressions;
using FM27Nexus.Core.Models;
using Microsoft.Win32;

namespace FM27Nexus.Core.Discovery;

public sealed class StorefrontDiscoveryService
{
    public const int SteamAppId = 4530370;
    public const string SteamExeRelative = @"Football Manager 27\fm.exe";
    public const string GamePassRelative = @"XboxGames\Football Manager 27\Content";

    private static readonly string[] EpicDirNames =
    [
        @"Epic Games\Football Manager 27",
        @"Epic Games\FootballManager27"
    ];

    private static readonly string[] FmExeNames = ["fm.exe", "fm_gamepass.exe"];

    public IReadOnlyList<GameInstallInfo> DiscoverAll()
    {
        var results = new List<GameInstallInfo>();
        results.AddRange(DiscoverSteam());
        results.AddRange(DiscoverEpic());
        results.AddRange(DiscoverGamePass());
        return Deduplicate(results);
    }

    public IReadOnlyList<GameInstallInfo> DiscoverSteam()
    {
        var found = new List<GameInstallInfo>();
        foreach (var library in EnumerateSteamLibraries())
        {
            var manifest = Path.Combine(library, "steamapps", $"appmanifest_{SteamAppId}.acf");
            string? installDirName = null;
            if (File.Exists(manifest))
                installDirName = TryReadAcfInstallDir(File.ReadAllText(manifest));

            var candidates = new List<string>();
            if (!string.IsNullOrWhiteSpace(installDirName))
                candidates.Add(Path.Combine(library, "steamapps", "common", installDirName));
            candidates.Add(Path.Combine(library, "steamapps", "common", "Football Manager 27"));

            foreach (var dir in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var exe = Path.Combine(dir, "fm.exe");
                if (!File.Exists(exe))
                    continue;

                found.Add(new GameInstallInfo
                {
                    Storefront = StorefrontKind.Steam,
                    DisplayName = "Football Manager 27 (Steam)",
                    InstallDirectory = dir,
                    ExecutablePath = exe,
                    ProcessName = "fm",
                    VersionString = TryReadFileVersion(exe)
                });
            }
        }

        return found;
    }

    public IReadOnlyList<GameInstallInfo> DiscoverEpic()
    {
        var found = new List<GameInstallInfo>();
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var programFiles in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
                 })
        {
            if (string.IsNullOrWhiteSpace(programFiles))
                continue;

            foreach (var relative in EpicDirNames)
            {
                var dir = Path.Combine(programFiles, relative);
                if (Directory.Exists(dir))
                    roots.Add(dir);
            }
        }

        foreach (var manifestRoot in EnumerateEpicManifestRoots())
        {
            if (!Directory.Exists(manifestRoot))
                continue;

            foreach (var file in Directory.EnumerateFiles(manifestRoot, "*.item"))
            {
                try
                {
                    var json = File.ReadAllText(file);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    var displayName = root.TryGetProperty("DisplayName", out var dn) ? dn.GetString() : null;
                    var installLocation = root.TryGetProperty("InstallLocation", out var il) ? il.GetString() : null;
                    if (string.IsNullOrWhiteSpace(installLocation))
                        continue;

                    var looksLikeFm27 =
                        (!string.IsNullOrWhiteSpace(displayName) &&
                         displayName.Contains("Football Manager 27", StringComparison.OrdinalIgnoreCase)) ||
                        installLocation.Contains("Football Manager 27", StringComparison.OrdinalIgnoreCase) ||
                        installLocation.Contains("FootballManager27", StringComparison.OrdinalIgnoreCase);

                    if (looksLikeFm27)
                        roots.Add(installLocation);
                }
                catch (JsonException)
                {
                    // Skip malformed Epic manifests rather than inventing paths.
                }
            }
        }

        foreach (var dir in roots)
        {
            var exe = Path.Combine(dir, "fm.exe");
            if (!File.Exists(exe))
                continue;

            found.Add(new GameInstallInfo
            {
                Storefront = StorefrontKind.Epic,
                DisplayName = "Football Manager 27 (Epic)",
                InstallDirectory = dir,
                ExecutablePath = exe,
                ProcessName = "fm",
                VersionString = TryReadFileVersion(exe)
            });
        }

        return found;
    }

    public IReadOnlyList<GameInstallInfo> DiscoverGamePass()
    {
        var found = new List<GameInstallInfo>();

        foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
        {
            var contentDir = Path.Combine(drive.RootDirectory.FullName, GamePassRelative);
            if (!Directory.Exists(contentDir))
                continue;

            foreach (var exeName in FmExeNames)
            {
                var exe = Path.Combine(contentDir, exeName);
                if (!File.Exists(exe))
                    continue;

                var isGamePassNamed = exeName.Equals("fm_gamepass.exe", StringComparison.OrdinalIgnoreCase);
                found.Add(new GameInstallInfo
                {
                    Storefront = StorefrontKind.GamePassPc,
                    DisplayName = isGamePassNamed
                        ? "Football Manager 27 (Game Pass PC)"
                        : "Football Manager 27 (XboxGames)",
                    InstallDirectory = contentDir,
                    ExecutablePath = exe,
                    ProcessName = Path.GetFileNameWithoutExtension(exeName),
                    VersionString = TryReadFileVersion(exe)
                });
            }
        }

        foreach (var pkg in EnumerateStorePackages())
        {
            found.Add(pkg);
        }

        return found;
    }

    public GameInstallInfo? TryResolveManualOverride(string directoryOrExe)
    {
        if (string.IsNullOrWhiteSpace(directoryOrExe))
            return null;

        var path = Path.GetFullPath(directoryOrExe.Trim());
        string exe;
        string dir;

        if (File.Exists(path) && path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            exe = path;
            dir = Path.GetDirectoryName(path) ?? path;
        }
        else if (Directory.Exists(path))
        {
            dir = path;
            exe = FmExeNames
                .Select(n => Path.Combine(dir, n))
                .FirstOrDefault(File.Exists) ?? Path.Combine(dir, "fm.exe");
            if (!File.Exists(exe))
                return null;
        }
        else
        {
            return null;
        }

        var storefront = InferStorefront(dir, exe);
        var processName = Path.GetFileNameWithoutExtension(exe);

        return new GameInstallInfo
        {
            Storefront = storefront,
            DisplayName = $"Football Manager 27 ({storefront})",
            InstallDirectory = dir,
            ExecutablePath = exe,
            ProcessName = processName,
            VersionString = TryReadFileVersion(exe)
        };
    }

    private static StorefrontKind InferStorefront(string dir, string exe)
    {
        if (dir.Contains(@"XboxGames\Football Manager 27", StringComparison.OrdinalIgnoreCase))
            return StorefrontKind.GamePassPc;
        if (Path.GetFileName(exe).Equals("fm_gamepass.exe", StringComparison.OrdinalIgnoreCase))
            return StorefrontKind.GamePassPc;
        if (dir.Contains("Epic Games", StringComparison.OrdinalIgnoreCase))
            return StorefrontKind.Epic;
        if (dir.Contains("steamapps", StringComparison.OrdinalIgnoreCase))
            return StorefrontKind.Steam;
        return StorefrontKind.Steam;
    }

    private IEnumerable<GameInstallInfo> EnumerateStorePackages()
    {
        // Query installed packages via PowerShell for reliability without WinRT package refs.
        // Distinguishes Console Edition by package family / display name heuristics.
        string output;
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments =
                    "-NoProfile -Command \"Get-AppxPackage | Where-Object { $_.Name -match 'FootballManager|Football.Manager' -or $_.PackageFullName -match 'FootballManager|Football.Manager' } | Select-Object Name,PackageFullName,InstallLocation | ConvertTo-Json -Compress\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc is null)
                yield break;
            output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(15000);
        }
        catch
        {
            yield break;
        }

        if (string.IsNullOrWhiteSpace(output))
            yield break;

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(output);
        }
        catch (JsonException)
        {
            yield break;
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    var info = MapPackageElement(el);
                    if (info is not null)
                        yield return info;
                }
            }
            else if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                var info = MapPackageElement(doc.RootElement);
                if (info is not null)
                    yield return info;
            }
        }
    }

    private static GameInstallInfo? MapPackageElement(JsonElement el)
    {
        var name = el.TryGetProperty("Name", out var n) ? n.GetString() : null;
        var fullName = el.TryGetProperty("PackageFullName", out var pfn) ? pfn.GetString() : null;
        var installLocation = el.TryGetProperty("InstallLocation", out var il) ? il.GetString() : null;
        if (string.IsNullOrWhiteSpace(installLocation) || !Directory.Exists(installLocation))
            return null;

        var isConsoleEdition =
            (!string.IsNullOrWhiteSpace(name) && name.Contains("Console", StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrWhiteSpace(fullName) && fullName.Contains("Console", StringComparison.OrdinalIgnoreCase));

        // Prefer Content\ layout when present.
        var content = Path.Combine(installLocation, "Content");
        var searchRoots = Directory.Exists(content)
            ? new[] { content, installLocation }
            : new[] { installLocation };

        string? exe = null;
        foreach (var root in searchRoots)
        {
            foreach (var exeName in FmExeNames)
            {
                var candidate = Path.Combine(root, exeName);
                if (File.Exists(candidate))
                {
                    exe = candidate;
                    break;
                }
            }

            if (exe is not null)
                break;
        }

        if (exe is null)
            return null;

        return new GameInstallInfo
        {
            Storefront = isConsoleEdition ? StorefrontKind.GamePassConsoleEdition : StorefrontKind.GamePassPc,
            DisplayName = isConsoleEdition
                ? "Football Manager 27 (Game Pass Console Edition)"
                : "Football Manager 27 (Microsoft Store / Game Pass)",
            InstallDirectory = Path.GetDirectoryName(exe) ?? installLocation,
            ExecutablePath = exe,
            ProcessName = Path.GetFileNameWithoutExtension(exe),
            PackageFamilyName = fullName,
            VersionString = TryReadFileVersion(exe)
        };
    }

    private static IEnumerable<string> EnumerateSteamLibraries()
    {
        var steamRoot = TryGetSteamRoot();
        if (steamRoot is null)
            yield break;

        yield return steamRoot;

        var vdf = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(vdf))
            yield break;

        var text = File.ReadAllText(vdf);
        foreach (Match match in Regex.Matches(text, "\"path\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase))
        {
            var path = match.Groups[1].Value.Replace(@"\\", @"\");
            if (Directory.Exists(path))
                yield return path;
        }
    }

    private static string? TryGetSteamRoot()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam")
                            ?? Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam")
                            ?? Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Valve\Steam");
            var path = key?.GetValue("SteamPath") as string ?? key?.GetValue("InstallPath") as string;
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
                return path.Replace('/', '\\');
        }
        catch
        {
            // Registry may be unavailable in restricted contexts.
        }

        var defaults = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam")
        };
        return defaults.FirstOrDefault(Directory.Exists);
    }

    private static IEnumerable<string> EnumerateEpicManifestRoots()
    {
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        yield return Path.Combine(programData, @"Epic\EpicGamesLauncher\Data\Manifests");
    }

    private static string? TryReadAcfInstallDir(string acf)
    {
        var match = Regex.Match(acf, "\"installdir\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string? TryReadFileVersion(string exePath)
    {
        try
        {
            var info = System.Diagnostics.FileVersionInfo.GetVersionInfo(exePath);
            return info.FileVersion ?? info.ProductVersion;
        }
        catch
        {
            return null;
        }
    }

    private static IReadOnlyList<GameInstallInfo> Deduplicate(List<GameInstallInfo> items)
    {
        return items
            .GroupBy(i => i.ExecutablePath, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(i => i.Storefront)
            .ThenBy(i => i.DisplayName)
            .ToList();
    }
}
