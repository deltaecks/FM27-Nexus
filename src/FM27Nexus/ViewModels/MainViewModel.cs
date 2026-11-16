using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FM27Nexus.Core.Discovery;
using FM27Nexus.Core.IPC;
using FM27Nexus.Core.Models;
using FM27Nexus.Core.Paths;
using FM27Nexus.Core.Settings;
using FM27Nexus.Services;
using Microsoft.Win32;
using Velopack;

namespace FM27Nexus.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly AppPaths _paths;
    private readonly SettingsStore _settingsStore;
    private readonly DocumentsPathResolver _documents;
    private readonly StorefrontDiscoveryService _discovery;
    private readonly MemoryAgentClient _agentClient;
    private readonly MemoryAgentLauncher _agentLauncher;
    private AppSettings _settings;
    private UpdateService _updateService;
    private UpdateInfo? _pendingUpdate;

    public ObservableCollection<GameInstallInfo> Installs { get; } = new();

    [ObservableProperty] private string documentsPath = string.Empty;
    [ObservableProperty] private string documentsStatus = string.Empty;
    [ObservableProperty] private string updateStatus = "Ready.";
    [ObservableProperty] private string agentStatus = "MemoryAgent not attached.";
    [ObservableProperty] private string statusMessage = "Milestone 1 — foundation & distribution.";
    [ObservableProperty] private bool isBusy;

    public MainViewModel()
    {
        _paths = new AppPaths();
        _paths.EnsureCreated();
        _settingsStore = new SettingsStore(_paths);
        _documents = new DocumentsPathResolver();
        _discovery = new StorefrontDiscoveryService();
        _agentClient = new MemoryAgentClient();
        _agentLauncher = new MemoryAgentLauncher();
        _settings = _settingsStore.Load();
        _updateService = new UpdateService(_settings.GitHubRepoUrl);

        DocumentsPath = _settings.DocumentsPathOverride ?? _documents.GetDefaultFm27Root();
        RefreshDocumentsStatus();
        RefreshInstalls();
    }

    [RelayCommand]
    private void RefreshInstalls()
    {
        Installs.Clear();
        foreach (var install in _discovery.DiscoverAll())
            Installs.Add(install);

        if (!string.IsNullOrWhiteSpace(_settings.PreferredInstallDirectory))
        {
            var manual = _discovery.TryResolveManualOverride(_settings.PreferredInstallDirectory);
            if (manual is not null && Installs.All(i => !string.Equals(i.ExecutablePath, manual.ExecutablePath, StringComparison.OrdinalIgnoreCase)))
                Installs.Add(manual);
        }

        StatusMessage = Installs.Count == 0
            ? "No FM27 installs detected. Use Browse Install to set a manual path."
            : $"Detected {Installs.Count} install(s).";
    }

    [RelayCommand]
    private void BrowseDocuments()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select Football Manager 27 documents folder (Sports Interactive\\Football Manager 27)",
            Multiselect = false
        };

        if (dialog.ShowDialog() != true)
            return;

        DocumentsPath = dialog.FolderName;
        _settings.DocumentsPathOverride = DocumentsPath;
        _settingsStore.Save(_settings);
        RefreshDocumentsStatus();
    }

    [RelayCommand]
    private void UseDefaultDocuments()
    {
        _settings.DocumentsPathOverride = null;
        _settingsStore.Save(_settings);
        DocumentsPath = _documents.GetDefaultFm27Root();
        RefreshDocumentsStatus();
    }

    [RelayCommand]
    private void BrowseInstall()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select FM27 install folder containing fm.exe or fm_gamepass.exe",
            Multiselect = false
        };

        if (dialog.ShowDialog() != true)
            return;

        var resolved = _discovery.TryResolveManualOverride(dialog.FolderName);
        if (resolved is null)
        {
            StatusMessage = "Selected folder does not contain fm.exe or fm_gamepass.exe.";
            return;
        }

        _settings.PreferredInstallDirectory = resolved.InstallDirectory;
        _settingsStore.Save(_settings);
        RefreshInstalls();
    }

    [RelayCommand]
    private void RepairShortcut()
    {
        UpdateStatus = _updateService.RepairDesktopShortcut();
    }

    [RelayCommand]
    private async Task CheckUpdatesAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            UpdateStatus = "Checking GitHub Releases…";
            var (update, status) = await _updateService.CheckAsync();
            _pendingUpdate = update;
            UpdateStatus = status;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ApplyUpdateAsync()
    {
        if (_pendingUpdate is null)
        {
            UpdateStatus = "No pending update. Check for updates first.";
            return;
        }

        if (IsBusy) return;
        IsBusy = true;
        try
        {
            var progress = new Progress<string>(msg => UpdateStatus = msg);
            await _updateService.DownloadAndApplyAsync(_pendingUpdate, progress);
        }
        catch (Exception ex)
        {
            UpdateStatus = "Update failed: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task AttachAgentAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            AgentStatus = "Launching elevated MemoryAgent (UAC)…";
            try
            {
                if (!await _agentClient.IsReachableAsync())
                    _agentLauncher.StartElevated();
            }
            catch (Exception ex)
            {
                AgentStatus = "Launch failed: " + ex.Message;
                return;
            }

            // Wait briefly for the pipe server to come up after UAC.
            for (var i = 0; i < 20; i++)
            {
                if (await _agentClient.IsReachableAsync())
                    break;
                await Task.Delay(250);
            }

            if (!await _agentClient.IsReachableAsync())
            {
                AgentStatus = "MemoryAgent did not become reachable. UAC may have been cancelled.";
                return;
            }

            var status = await _agentClient.GetStatusAsync();
            if (!status.Ok || status.Status is null)
            {
                AgentStatus = status.Error ?? "GetStatus failed.";
                return;
            }

            var s = status.Status;
            var procs = s.MatchingProcesses.Count == 0
                ? "no fm.exe / fm_gamepass processes"
                : string.Join("; ", s.MatchingProcesses.Select(p =>
                    $"PID {p.ProcessId} {p.ProcessName} pathVerified={p.PathVerifiedAgainstInstall}"));

            AgentStatus =
                $"Elevated={s.IsElevated}; v{s.AgentVersion}; protocol={s.ProtocolVersion}; {procs}. {s.Message}";
        }
        catch (Exception ex)
        {
            AgentStatus = "Attach error: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void RefreshDocumentsStatus()
    {
        var layout = _documents.Resolve(_settings.DocumentsPathOverride);
        DocumentsStatus = layout.RootExists
            ? $"Found. games={(Directory.Exists(layout.Games) ? "yes" : "no")}, skin={(Directory.Exists(layout.Skin) ? "yes" : "no")}, graphics={(Directory.Exists(layout.Graphics) ? "yes" : "no")}"
            : "Folder not found yet (will appear after FM27 creates Sports Interactive data).";
    }
}
