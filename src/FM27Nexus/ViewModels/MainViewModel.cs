using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FM27Nexus.Core.Discovery;
using FM27Nexus.Core.IPC;
using FM27Nexus.Core.Models;
using FM27Nexus.Core.Mods;
using FM27Nexus.Core.Paths;
using FM27Nexus.Core.Save;
using FM27Nexus.Core.Settings;
using FM27Nexus.Core.Vault;
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
    private readonly SaveWorkspace _workspace;
    private readonly SaveVaultService _vault;
    private readonly ModManagerService _mods;
    private readonly ScoutExportService _export = new();
    private AppSettings _settings;
    private UpdateService _updateService;
    private UpdateInfo? _pendingUpdate;

    public ObservableCollection<GameInstallInfo> Installs { get; } = new();
    public ObservableCollection<ScoutPlayer> Players { get; } = new();
    public ObservableCollection<VaultEntry> VaultEntries { get; } = new();
    public ObservableCollection<VanillaStateSnapshot> VanillaSnapshots { get; } = new();
    public ICollectionView PlayersView { get; }

    [ObservableProperty] private string documentsPath = string.Empty;
    [ObservableProperty] private string documentsStatus = string.Empty;
    [ObservableProperty] private string updateStatus = "Ready.";
    [ObservableProperty] private string agentStatus = "MemoryAgent not attached.";
    [ObservableProperty] private string statusMessage = "FM27 Nexus";
    [ObservableProperty] private string filterText = string.Empty;
    [ObservableProperty] private string workspaceStatus = "No data loaded.";
    [ObservableProperty] private string editorEntity = "Player";
    [ObservableProperty] private int editorIndex;
    [ObservableProperty] private string editorField = "CA";
    [ObservableProperty] private long editorValue = 150;
    [ObservableProperty] private string editorLog = string.Empty;
    [ObservableProperty] private string buildId = "27.0.1+2494687";
    [ObservableProperty] private string storefront = "steam";
    [ObservableProperty] private string? backupSavePath;
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
        _workspace = new SaveWorkspace(_paths);
        _vault = new SaveVaultService(_paths);
        _mods = new ModManagerService(_paths);
        _settings = _settingsStore.Load();
        _updateService = new UpdateService(_settings.GitHubRepoUrl);

        PlayersView = CollectionViewSource.GetDefaultView(Players);
        PlayersView.Filter = FilterPlayer;

        DocumentsPath = _settings.DocumentsPathOverride ?? _documents.GetDefaultFm27Root();
        RefreshDocumentsStatus();
        RefreshInstalls();
        RefreshVault();
        RefreshVanilla();
    }

    partial void OnFilterTextChanged(string value) => PlayersView.Refresh();

    private bool FilterPlayer(object obj)
    {
        if (obj is not ScoutPlayer p) return false;
        if (string.IsNullOrWhiteSpace(FilterText)) return true;
        var q = FilterText.Trim();
        return (p.Name?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
               || (p.ClubName?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
               || (p.RcaBestRole?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    [RelayCommand]
    private void RefreshInstalls()
    {
        Installs.Clear();
        foreach (var install in _discovery.DiscoverAll())
            Installs.Add(install);
        StatusMessage = Installs.Count == 0
            ? "No FM27 installs detected."
            : $"Detected {Installs.Count} install(s).";
    }

    [RelayCommand]
    private void BrowseDocuments()
    {
        var dialog = new OpenFolderDialog { Title = "Select FM27 documents folder" };
        if (dialog.ShowDialog() != true) return;
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
        var dialog = new OpenFolderDialog { Title = "Select FM27 install folder" };
        if (dialog.ShowDialog() != true) return;
        var resolved = _discovery.TryResolveManualOverride(dialog.FolderName);
        if (resolved is null)
        {
            StatusMessage = "Folder missing fm.exe / fm_gamepass.exe.";
            return;
        }

        _settings.PreferredInstallDirectory = resolved.InstallDirectory;
        _settingsStore.Save(_settings);
        RefreshInstalls();
    }

    [RelayCommand]
    private async Task OpenFmSaveAsync()
    {
        var dialog = new OpenFileDialog { Filter = "FM Save (*.fm)|*.fm", Title = "Open Football Manager save" };
        if (dialog.ShowDialog() != true) return;
        IsBusy = true;
        try
        {
            BackupSavePath = dialog.FileName;
            await _workspace.LoadFmAsync(dialog.FileName);
            ReplacePlayers(_workspace.Players);
            WorkspaceStatus = _workspace.Status;
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void ImportTable()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Scout tables (*.csv;*.html;*.htm;*.json)|*.csv;*.html;*.htm;*.json",
            Title = "Import FM HTML/CSV/JSON"
        };
        if (dialog.ShowDialog() != true) return;
        _workspace.LoadImport(dialog.FileName);
        ReplacePlayers(_workspace.Players);
        WorkspaceStatus = _workspace.Status;
    }

    [RelayCommand]
    private void ExportCsv()
    {
        var dialog = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = "scout.csv" };
        if (dialog.ShowDialog() != true) return;
        _export.ExportCsv(Players, dialog.FileName);
        WorkspaceStatus = "Exported CSV: " + dialog.FileName;
    }

    [RelayCommand]
    private void ExportJson()
    {
        var dialog = new SaveFileDialog { Filter = "JSON (*.json)|*.json", FileName = "scout.json" };
        if (dialog.ShowDialog() != true) return;
        _export.ExportJson(Players, dialog.FileName);
        WorkspaceStatus = "Exported JSON: " + dialog.FileName;
    }

    [RelayCommand]
    private void SnapshotSave()
    {
        if (string.IsNullOrWhiteSpace(BackupSavePath) || !File.Exists(BackupSavePath))
        {
            var dialog = new OpenFileDialog { Filter = "FM Save (*.fm)|*.fm" };
            if (dialog.ShowDialog() != true) return;
            BackupSavePath = dialog.FileName;
        }

        var entry = _vault.CreateSnapshot(BackupSavePath!, "Manual vault snapshot");
        RefreshVault();
        WorkspaceStatus = $"Vault snapshot {entry.Id} ({entry.SectionCount} sections).";
    }

    [RelayCommand]
    private void RefreshVault()
    {
        VaultEntries.Clear();
        foreach (var e in _vault.ListEntries())
            VaultEntries.Add(e);
    }

    [RelayCommand]
    private void RestoreVaultEntry()
    {
        if (VaultEntries.Count == 0) return;
        var entry = VaultEntries[0];
        var dialog = new SaveFileDialog { Filter = "FM Save (*.fm)|*.fm", FileName = Path.GetFileName(entry.SourcePath) };
        if (dialog.ShowDialog() != true) return;
        _vault.Restore(entry.Id, dialog.FileName);
        WorkspaceStatus = "Restored vault entry to " + dialog.FileName;
    }

    [RelayCommand]
    private void InstallMod()
    {
        var dialog = new OpenFileDialog { Title = "Select mod/skin/graphics/plugin/bundle", Filter = "All|*.*" };
        if (dialog.ShowDialog() != true) return;
        var layout = _documents.Resolve(_settings.DocumentsPathOverride);
        var gameDir = Installs.FirstOrDefault()?.InstallDirectory ?? _settings.PreferredInstallDirectory;
        var result = _mods.Install(dialog.FileName, new Fm27UserFolders
        {
            Skin = layout.Skin,
            EditorData = layout.EditorData,
            Graphics = layout.Graphics,
            GameInstallDirectory = gameDir
        });
        RefreshVanilla();
        WorkspaceStatus = $"Installed {result.Kind} → {result.Destination}";
    }

    [RelayCommand]
    private void CreateVanillaState()
    {
        var gameDir = Installs.FirstOrDefault()?.InstallDirectory ?? _settings.PreferredInstallDirectory;
        if (string.IsNullOrWhiteSpace(gameDir))
        {
            WorkspaceStatus = "No game install directory available for VanillaState.";
            return;
        }

        var snap = _mods.CreateVanillaState(gameDir);
        RefreshVanilla();
        WorkspaceStatus = $"VanillaState snapshot {snap.Id} ({snap.FileCount} files).";
    }

    [RelayCommand]
    private void RestoreVanillaState()
    {
        if (VanillaSnapshots.Count == 0)
        {
            WorkspaceStatus = "No VanillaState snapshots.";
            return;
        }

        var gameDir = Installs.FirstOrDefault()?.InstallDirectory ?? _settings.PreferredInstallDirectory;
        if (string.IsNullOrWhiteSpace(gameDir)) return;
        _mods.RestoreVanillaState(VanillaSnapshots[0].Id, gameDir);
        WorkspaceStatus = "VanillaState restored: " + VanillaSnapshots[0].Id;
    }

    private void RefreshVanilla()
    {
        VanillaSnapshots.Clear();
        foreach (var s in _mods.ListVanillaSnapshots())
            VanillaSnapshots.Add(s);
    }

    [RelayCommand]
    private void RepairShortcut() => UpdateStatus = _updateService.RepairDesktopShortcut();

    [RelayCommand]
    private async Task CheckUpdatesAsync()
    {
        IsBusy = true;
        try
        {
            var (update, status) = await _updateService.CheckAsync();
            _pendingUpdate = update;
            UpdateStatus = status;
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task ApplyUpdateAsync()
    {
        if (_pendingUpdate is null)
        {
            UpdateStatus = "No pending update.";
            return;
        }

        IsBusy = true;
        try
        {
            await _updateService.DownloadAndApplyAsync(_pendingUpdate, new Progress<string>(m => UpdateStatus = m));
        }
        catch (Exception ex)
        {
            UpdateStatus = "Update failed: " + ex.Message;
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task AttachAgentAsync()
    {
        IsBusy = true;
        try
        {
            if (!await _agentClient.IsReachableAsync())
                _agentLauncher.StartElevated();

            for (var i = 0; i < 20 && !await _agentClient.IsReachableAsync(); i++)
                await Task.Delay(250);

            if (!await _agentClient.IsReachableAsync())
            {
                AgentStatus = "MemoryAgent not reachable (UAC cancelled?).";
                return;
            }

            var status = await _agentClient.GetStatusAsync();
            AgentStatus = FormatAgent(status);
        }
        catch (Exception ex)
        {
            AgentStatus = ex.Message;
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task LoadOffsetProfileAsync()
    {
        var response = await _agentClient.SendAsync(new AgentRequest
        {
            Command = "LoadProfile",
            BuildId = BuildId,
            Storefront = Storefront
        });
        EditorLog = response.Ok ? response.Message ?? "Loaded" : response.Error ?? "Failed";
        AgentStatus = FormatAgent(response);
    }

    [RelayCommand]
    private async Task ImportOffsetPackAsync()
    {
        var dialog = new OpenFileDialog { Filter = "Offset pack (*.json)|*.json" };
        if (dialog.ShowDialog() != true) return;
        var response = await _agentClient.SendAsync(new AgentRequest
        {
            Command = "ImportPack",
            PackPath = dialog.FileName
        });
        EditorLog = response.Ok ? response.Message ?? "Imported" : response.Error ?? "Failed";
    }

    [RelayCommand]
    private async Task AttachSelectedProcessAsync()
    {
        var status = await _agentClient.GetStatusAsync();
        var proc = status.Status?.MatchingProcesses.FirstOrDefault(p => p.PathVerifiedAgainstInstall)
                   ?? status.Status?.MatchingProcesses.FirstOrDefault();
        if (proc is null || string.IsNullOrWhiteSpace(proc.ExecutablePath))
        {
            EditorLog = "No matching fm/fm_gamepass process with a verified path.";
            return;
        }

        var response = await _agentClient.SendAsync(new AgentRequest
        {
            Command = "Attach",
            ProcessId = proc.ProcessId,
            ExecutablePath = proc.ExecutablePath
        });
        EditorLog = response.Ok ? response.Message ?? "Attached" : response.Error ?? "Attach failed";
        AgentStatus = FormatAgent(response);
    }

    [RelayCommand]
    private async Task ValidateSanityAsync()
    {
        var response = await _agentClient.SendAsync(new AgentRequest { Command = "ValidateSanity" });
        EditorLog = response.Ok ? response.Message ?? "OK" : response.Error ?? "Failed";
    }

    [RelayCommand]
    private async Task StageEditAsync()
    {
        var response = await _agentClient.SendAsync(new AgentRequest
        {
            Command = "StageEdit",
            Entity = EditorEntity,
            Index = EditorIndex,
            Field = EditorField,
            Value = EditorValue
        });
        EditorLog = response.Ok ? response.Message ?? "Staged" : response.Error ?? "Failed";
    }

    [RelayCommand]
    private async Task ApplyEditsAsync()
    {
        var response = await _agentClient.SendAsync(new AgentRequest
        {
            Command = "ApplyPending",
            BackupSavePath = BackupSavePath
        });
        EditorLog = response.Ok ? response.Message ?? "Applied" : response.Error ?? "Failed";
    }

    [RelayCommand]
    private async Task UndoEditAsync()
    {
        var response = await _agentClient.SendAsync(new AgentRequest { Command = "UndoLast" });
        EditorLog = response.Ok ? response.Message ?? "Undone" : response.Error ?? "Failed";
    }

    private void ReplacePlayers(IReadOnlyList<ScoutPlayer> players)
    {
        Players.Clear();
        foreach (var p in players)
            Players.Add(p);
        PlayersView.Refresh();
    }

    private void RefreshDocumentsStatus()
    {
        var layout = _documents.Resolve(_settings.DocumentsPathOverride);
        DocumentsStatus = layout.RootExists
            ? $"Found. games={(Directory.Exists(layout.Games) ? "yes" : "no")}"
            : "FM27 documents folder not found yet.";
    }

    private static string FormatAgent(AgentResponse response)
    {
        var s = response.Status;
        if (s is null) return response.Error ?? response.Message ?? "";
        return $"Elevated={s.IsElevated}; attached={s.Attached}; profile={s.ProfileLoaded}; sanity={s.SanityPassed}; {s.Message}";
    }
}
