using Velopack;
using Velopack.Sources;

namespace FM27Nexus.Services;

public sealed class UpdateService
{
    private readonly string _repoUrl;

    public UpdateService(string repoUrl)
    {
        _repoUrl = repoUrl;
    }

    public async Task<(UpdateInfo? Update, string Status)> CheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var mgr = CreateManager();
            if (!mgr.IsInstalled)
                return (null, "Not installed via Velopack (dev build). Updates apply after Setup.exe install.");

            var update = await mgr.CheckForUpdatesAsync().ConfigureAwait(false);
            if (update is null)
                return (null, $"Up to date ({mgr.CurrentVersion}).");

            var notes = string.IsNullOrWhiteSpace(update.TargetFullRelease.NotesMarkdown)
                ? "(no release notes)"
                : update.TargetFullRelease.NotesMarkdown;

            return (update, $"Update available: {update.TargetFullRelease.Version}\n{notes}");
        }
        catch (Exception ex)
        {
            return (null, "Update check failed: " + ex.Message);
        }
    }

    public async Task<string> DownloadAndApplyAsync(UpdateInfo update, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var mgr = CreateManager();
        progress?.Report("Downloading update… On OneDrive-synced Desktops, restart can take several minutes while shortcuts are scanned. Do not launch a second copy.");
        await mgr.DownloadUpdatesAsync(update, progress: p =>
        {
            progress?.Report($"Downloading… {p}%");
        }).ConfigureAwait(false);

        progress?.Report("Applying update and restarting… Please wait; shortcut rescans on OneDrive Desktops may delay restart.");
        mgr.ApplyUpdatesAndRestart(update);
        return "Restarting…";
    }

    public string RepairDesktopShortcut()
    {
        try
        {
#pragma warning disable CS0618 // Intentional repair path required by product spec
            var shortcuts = new Velopack.Windows.Shortcuts();
            shortcuts.CreateShortcutForThisExe(Velopack.Windows.ShortcutLocation.Desktop | Velopack.Windows.ShortcutLocation.StartMenuRoot);
#pragma warning restore CS0618
            return "Desktop and Start Menu shortcuts created/repaired.";
        }
        catch (Exception ex)
        {
            return "Shortcut repair failed: " + ex.Message;
        }
    }

    private UpdateManager CreateManager()
    {
        var source = new GithubSource(_repoUrl, accessToken: null, prerelease: false);
        return new UpdateManager(source);
    }
}
