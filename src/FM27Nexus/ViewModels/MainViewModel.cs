using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FM27Nexus.Core.Club;
using FM27Nexus.Core.Discovery;
using FM27Nexus.Core.IPC;
using FM27Nexus.Core.Models;
using FM27Nexus.Core.Mods;
using FM27Nexus.Core.Paths;
using FM27Nexus.Core.Save;
using FM27Nexus.Core.Settings;
using FM27Nexus.Core.Tactics;
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
    public ObservableCollection<string> AvailableClubs { get; } = new();
    public ObservableCollection<ClubFieldRow> ClubFields { get; } = new();
    public ObservableCollection<TacticalFactorRow> TacticalAreaFactors { get; } = new();
    public ObservableCollection<TacticalFactorRow> TacticalComprehensionFactors { get; } = new();
    public ICollectionView PlayersView { get; }
    public ICollectionView SquadView { get; }

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
    [ObservableProperty] private ScoutPlayer? selectedPlayer;
    [ObservableProperty] private string liveStatus = "Live: not connected. Import an OpenEditor definition, then Connect Live.";
    [ObservableProperty] private string currentPage = "Home";
    [ObservableProperty] private string? selectedClubName;
    [ObservableProperty] private long? liveClubId;
    [ObservableProperty] private string clubSummary = "Load a save to see your club.";
    [ObservableProperty] private string clubLiveStatus = "Connect Live to read facilities, finances, and reputation.";
    [ObservableProperty] private string tacticsStatus = "Import an OpenEditor definition that maps Tactical Understanding factors, then Connect Live.";
    [ObservableProperty] private string footerStatus = "Ready";
    [ObservableProperty] private ClubFieldRow? selectedClubField;

    public bool IsHomePage => CurrentPage == "Home";
    public bool IsMyClubPage => CurrentPage == "MyClub";
    public bool IsScoutingPage => CurrentPage == "Scouting";
    public bool IsTacticsPage => CurrentPage == "Tactics";
    public bool IsVaultPage => CurrentPage == "Vault";
    public bool IsLivePage => CurrentPage == "Live";

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
        SquadView = new CollectionViewSource { Source = Players }.View;
        SquadView.Filter = FilterSquad;

        DocumentsPath = _settings.DocumentsPathOverride ?? _documents.GetDefaultFm27Root();
        SelectedClubName = _settings.PreferredClubName;
        SeedTacticalFactorsOffline();
        RefreshDocumentsStatus();
        RefreshInstalls();
        RefreshVault();
        RefreshVanilla();
        FooterStatus = "Ready";
    }

    partial void OnFilterTextChanged(string value) => PlayersView.Refresh();
    partial void OnSelectedClubNameChanged(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            _settings.PreferredClubName = value;
            _settingsStore.Save(_settings);
        }

        SquadView.Refresh();
        UpdateClubSummary();
    }

    partial void OnCurrentPageChanged(string value)
    {
        OnPropertyChanged(nameof(IsHomePage));
        OnPropertyChanged(nameof(IsMyClubPage));
        OnPropertyChanged(nameof(IsScoutingPage));
        OnPropertyChanged(nameof(IsTacticsPage));
        OnPropertyChanged(nameof(IsVaultPage));
        OnPropertyChanged(nameof(IsLivePage));
    }

    [RelayCommand]
    private void Navigate(string page) => CurrentPage = page;

    partial void OnSelectedPlayerChanged(ScoutPlayer? value)
    {
        if (value?.UniqueId is > 0)
            EditorIndex = (int)Math.Min(value.UniqueId.Value, int.MaxValue);
        else if (value?.Uid is > 0)
            EditorIndex = (int)Math.Min(value.Uid.Value, int.MaxValue);

        if (value?.Ca is int ca)
        {
            EditorField = "CA";
            EditorValue = ca;
        }
    }

    private bool FilterPlayer(object obj)
    {
        if (obj is not ScoutPlayer p) return false;
        if (string.IsNullOrWhiteSpace(FilterText)) return true;
        var q = FilterText.Trim();
        return (p.Name?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
               || (p.ClubName?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
               || (p.RcaBestRole?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private bool FilterSquad(object obj)
    {
        if (obj is not ScoutPlayer p) return false;
        if (string.IsNullOrWhiteSpace(SelectedClubName)) return false;
        return p.ClubName?.Contains(SelectedClubName, StringComparison.OrdinalIgnoreCase) == true
               || string.Equals(p.ClubName, SelectedClubName, StringComparison.OrdinalIgnoreCase);
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
    private async Task LoadLatestSaveAsync()
    {
        if (IsBusy) return;
        var current = _workspace.FindCurrentSave(_settings.DocumentsPathOverride);
        if (current is null)
        {
            WorkspaceStatus = "No .fm saves found in Documents\\Sports Interactive\\Football Manager 27\\games. Use Open .fm Save… or fix the Documents path on Home.";
            return;
        }

        await LoadFmPathAsync(current.Path, $"Loading latest save: {current.Name}…");
    }

    [RelayCommand]
    private async Task OpenFmSaveAsync()
    {
        if (IsBusy) return;
        var gamesDir = new LocalSaveDiscovery(_documents).GetGamesDirectory(_settings.DocumentsPathOverride);
        var dialog = new OpenFileDialog
        {
            Filter = "FM Save (*.fm)|*.fm",
            Title = "Open Football Manager save"
        };
        if (Directory.Exists(gamesDir))
            dialog.InitialDirectory = gamesDir;
        if (dialog.ShowDialog() != true) return;
        await LoadFmPathAsync(dialog.FileName, "Opening selected save…");
    }

    private async Task LoadFmPathAsync(string path, string startingStatus)
    {
        IsBusy = true;
        WorkspaceStatus = startingStatus;
        try
        {
            BackupSavePath = path;
            var progress = new Progress<string>(msg => WorkspaceStatus = msg);
            await _workspace.LoadFmAsync(path, progress: progress);
            ReplacePlayers(_workspace.Players);
            WorkspaceStatus = _workspace.Status;
            FooterStatus = WorkspaceStatus;
        }
        catch (Exception ex)
        {
            WorkspaceStatus = "Save load failed: " + ex.Message;
            FooterStatus = WorkspaceStatus;
        }
        finally
        {
            IsBusy = false;
        }
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
    private async Task ImportOpenEditorDefinitionAsync()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "OpenEditor definition (*.json)|*.json",
            Title = "Import OpenEditor FM27 definition (user-provided)"
        };
        if (dialog.ShowDialog() != true) return;

        await EnsureAgentAsync();
        var response = await _agentClient.SendAsync(new AgentRequest
        {
            Command = "ImportOpenEditorDef",
            PackPath = dialog.FileName
        });
        LiveStatus = response.Ok
            ? response.Message ?? "Definition imported."
            : response.Error ?? "Import failed.";
        EditorLog = LiveStatus;
    }

    [RelayCommand]
    private async Task ConnectLiveAsync()
    {
        IsBusy = true;
        try
        {
            await EnsureAgentAsync();
            var status = await _agentClient.GetStatusAsync();
            var proc = status.Status?.MatchingProcesses.FirstOrDefault(p => p.PathVerifiedAgainstInstall)
                       ?? status.Status?.MatchingProcesses.FirstOrDefault();
            if (proc is null || string.IsNullOrWhiteSpace(proc.ExecutablePath))
            {
                LiveStatus = "No matching fm/fm_gamepass process. Load a career in FM27 first.";
                return;
            }

            LiveStatus = "Indexing Player/Club/Team via RTTI (may take several minutes)…";
            var response = await _agentClient.SendAsync(new AgentRequest
            {
                Command = "LiveConnect",
                ProcessId = proc.ProcessId,
                ExecutablePath = proc.ExecutablePath,
                BackupSavePath = BackupSavePath
            }, TimeSpan.FromMinutes(8));

            LiveStatus = response.Ok
                ? $"{response.Message} (module {response.LiveModuleVersion ?? "?"})"
                : response.Error ?? "Live connect failed.";
            EditorLog = LiveStatus;
            AgentStatus = FormatAgent(response);
            FooterStatus = LiveStatus;
            ApplyTacticalFactors(response.TacticalFactors);
            if (response.Ok)
            {
                await RefreshMyClubLiveAsync();
                await RefreshTacticalFactorsAsync();
            }
        }
        catch (Exception ex)
        {
            LiveStatus = ex.Message;
            EditorLog = ex.Message;
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task StageEditAsync()
    {
        // When live-connected, edits apply immediately into the open career.
        if (await IsLiveConnectedAsync())
        {
            await ApplyLiveEditAsync();
            return;
        }

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
        if (await IsLiveConnectedAsync())
        {
            await ApplyLiveEditAsync();
            return;
        }

        var response = await _agentClient.SendAsync(new AgentRequest
        {
            Command = "ApplyPending",
            BackupSavePath = BackupSavePath
        });
        EditorLog = response.Ok ? response.Message ?? "Applied" : response.Error ?? "Failed";
    }

    [RelayCommand]
    private async Task ApplyLiveEditAsync()
    {
        var playerId = ResolveLivePlayerId();
        if (playerId is null)
        {
            EditorLog = "Select a scout-grid player (with UniqueId) or set Editor Index to the in-game player Id.";
            return;
        }

        if (string.IsNullOrWhiteSpace(BackupSavePath) || !File.Exists(BackupSavePath))
        {
            EditorLog = "Load/open the current .fm first so a vault backup can be taken before live writes.";
            return;
        }

        await EnsureAgentAsync();
        var response = await _agentClient.SendAsync(new AgentRequest
        {
            Command = "LiveApplyPlayer",
            LivePlayerId = playerId,
            Field = EditorField,
            Value = EditorValue,
            BackupSavePath = BackupSavePath
        });

        EditorLog = response.Ok
            ? $"{response.Message} — open career updated in-memory. Save in FM to persist to disk."
            : response.Error ?? "Live apply failed.";
        if (response.Ok)
            LiveStatus = $"Live write OK → player {playerId}, field {EditorField}={EditorValue}";
    }

    [RelayCommand]
    private async Task UndoEditAsync()
    {
        var response = await _agentClient.SendAsync(new AgentRequest { Command = "UndoLast" });
        EditorLog = response.Ok ? response.Message ?? "Undone" : response.Error ?? "Failed";
    }

    [RelayCommand]
    private async Task RefreshMyClubLiveAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedClubName))
        {
            ClubLiveStatus = "Select a club first.";
            return;
        }

        if (!await IsLiveConnectedAsync())
        {
            ClubLiveStatus = "Connect Live to read club facilities, finances, and reputation.";
            return;
        }

        try
        {
            await EnsureAgentAsync();
            var resolve = await _agentClient.SendAsync(new AgentRequest
            {
                Command = "LiveResolveClub",
                ClubName = SelectedClubName
            });
            if (!resolve.Ok || resolve.ResolvedClubId is null)
            {
                ClubLiveStatus = resolve.Error ?? "Club not found in live index.";
                LiveClubId = null;
                return;
            }

            LiveClubId = resolve.ResolvedClubId;
            var fieldNames = resolve.ClubFields?.ToList()
                             ??
                             [
                                 "Reputation", "Morale", "Facilities.Training", "Facilities.Youth",
                                 "Facilities.YouthRecruitment", "Facilities.JuniorCoaching", "Facilities.Corporate",
                                 "Finance.Balance", "Finance.TransferBudget", "Finance.WageBudget",
                                 "Stadium.Capacity", "YouthImportance"
                             ];

            var read = await _agentClient.SendAsync(new AgentRequest
            {
                Command = "LiveReadEntity",
                Entity = "Club",
                LiveEntityId = LiveClubId,
                FieldNames = fieldNames
            });

            ClubFields.Clear();
            if (read.FieldValues is not null)
            {
                foreach (var kv in read.FieldValues.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
                    ClubFields.Add(new ClubFieldRow(kv.Key, kv.Value));
            }

            ClubLiveStatus = read.Ok
                ? $"Live club Id {LiveClubId} — {ClubFields.Count} fields."
                : read.Error ?? "Failed reading club fields.";
        }
        catch (Exception ex)
        {
            ClubLiveStatus = ex.Message;
        }
    }

    [RelayCommand]
    private async Task ApplyClubFieldAsync()
    {
        if (SelectedClubField is null || LiveClubId is null)
        {
            ClubLiveStatus = "Select a club field and ensure Live club is resolved.";
            return;
        }

        if (!long.TryParse(SelectedClubField.EditValue, out var value))
        {
            ClubLiveStatus = "Enter a numeric value.";
            return;
        }

        if (string.IsNullOrWhiteSpace(BackupSavePath) || !File.Exists(BackupSavePath))
        {
            ClubLiveStatus = "Load the current .fm first for a vault backup.";
            return;
        }

        await EnsureAgentAsync();
        var response = await _agentClient.SendAsync(new AgentRequest
        {
            Command = "LiveApplyEntity",
            Entity = "Club",
            LiveEntityId = LiveClubId,
            Fields = new Dictionary<string, long> { [SelectedClubField.Key] = value },
            BackupSavePath = BackupSavePath
        });
        ClubLiveStatus = response.Ok
            ? $"{response.Message} Save in FM to persist."
            : response.Error ?? "Club apply failed.";
        if (response.Ok)
            await RefreshMyClubLiveAsync();
    }

    [RelayCommand]
    private async Task RefreshTacticalFactorsAsync()
    {
        try
        {
            if (!await _agentClient.IsReachableAsync())
            {
                SeedTacticalFactorsOffline();
                TacticsStatus = "MemoryAgent offline — showing factor catalog (all Unmapped until definition + Live).";
                return;
            }

            var response = await _agentClient.SendAsync(new AgentRequest { Command = "LiveGetTacticalFactors" });
            ApplyTacticalFactors(response.TacticalFactors);
            TacticsStatus = response.Message ?? TacticsStatus;
        }
        catch (Exception ex)
        {
            TacticsStatus = ex.Message;
        }
    }

    [RelayCommand]
    private async Task MaximizeTacticalUnderstandingAsync()
    {
        if (string.IsNullOrWhiteSpace(BackupSavePath) || !File.Exists(BackupSavePath))
        {
            TacticsStatus = "Load/open the current .fm first so a vault backup can be taken.";
            return;
        }

        IsBusy = true;
        try
        {
            await EnsureAgentAsync();
            var squadIds = Players
                .Where(p => FilterSquad(p) && (p.UniqueId ?? p.Uid) is > 0)
                .Select(p => (p.UniqueId ?? p.Uid)!.Value)
                .Distinct()
                .ToList();

            var response = await _agentClient.SendAsync(new AgentRequest
            {
                Command = "LiveMaximizeTacticalUnderstanding",
                BackupSavePath = BackupSavePath,
                PlayerIds = squadIds.Count > 0 ? squadIds : null
            }, TimeSpan.FromMinutes(3));

            ApplyTacticalFactors(response.TacticalFactors);
            TacticsStatus = response.Ok
                ? response.Message ?? "Maximized."
                : response.Error ?? "Maximize failed.";
            FooterStatus = TacticsStatus;
        }
        catch (Exception ex)
        {
            TacticsStatus = ex.Message;
        }
        finally { IsBusy = false; }
    }

    private void ReplacePlayers(IReadOnlyList<ScoutPlayer> players)
    {
        Players.Clear();
        foreach (var p in players)
            Players.Add(p);
        PlayersView.Refresh();

        AvailableClubs.Clear();
        foreach (var club in ClubIdentity.DistinctClubs(players.Select(p => p.ClubName)))
            AvailableClubs.Add(club);

        var preferred = ClubIdentity.ResolvePreferredClub(
            BackupSavePath ?? _workspace.LoadedPath,
            players.Select(p => p.ClubName),
            _settings.PreferredClubName);
        if (!string.IsNullOrWhiteSpace(preferred))
            SelectedClubName = preferred;

        SquadView.Refresh();
        UpdateClubSummary();
    }

    private void UpdateClubSummary()
    {
        var squad = Players.Where(FilterSquad).Cast<ScoutPlayer>().ToList();
        if (squad.Count == 0)
        {
            ClubSummary = string.IsNullOrWhiteSpace(SelectedClubName)
                ? "No club selected."
                : $"{SelectedClubName}: no players matched in the loaded table.";
            return;
        }

        var avgCa = squad.Where(p => p.Ca is not null).Select(p => p.Ca!.Value).DefaultIfEmpty(0).Average();
        var avgPa = squad.Where(p => p.Pa is not null).Select(p => p.Pa!.Value).DefaultIfEmpty(0).Average();
        var avgAge = squad.Where(p => p.Age is not null).Select(p => p.Age!.Value).DefaultIfEmpty(0).Average();
        ClubSummary = $"{SelectedClubName}: {squad.Count} players · avg CA {avgCa:0} · avg PA {avgPa:0} · avg age {avgAge:0.0}";
    }

    private void SeedTacticalFactorsOffline()
    {
        ApplyTacticalFactors(TacticalUnderstandingFactors.All.Select(f => new TacticalFactorStatusDto
        {
            Id = f.Id,
            DisplayName = f.DisplayName,
            Group = f.Group.ToString(),
            IsMapped = false
        }).ToList());
    }

    private void ApplyTacticalFactors(IReadOnlyList<TacticalFactorStatusDto>? factors)
    {
        TacticalAreaFactors.Clear();
        TacticalComprehensionFactors.Clear();
        foreach (var f in factors ?? Array.Empty<TacticalFactorStatusDto>())
        {
            var row = new TacticalFactorRow(f.Id, f.DisplayName, f.Group, f.IsMapped, f.Entity, f.FieldKey);
            if (string.Equals(f.Group, "Comprehension", StringComparison.OrdinalIgnoreCase))
                TacticalComprehensionFactors.Add(row);
            else
                TacticalAreaFactors.Add(row);
        }
    }

    private void RefreshDocumentsStatus()
    {
        var layout = _documents.Resolve(_settings.DocumentsPathOverride);
        DocumentsStatus = layout.RootExists
            ? $"Found. games={(Directory.Exists(layout.Games) ? "yes" : "no")}"
            : "FM27 documents folder not found yet.";
    }

    private long? ResolveLivePlayerId()
    {
        if (SelectedPlayer?.UniqueId is > 0)
            return SelectedPlayer.UniqueId;
        if (SelectedPlayer?.Uid is > 0)
            return SelectedPlayer.Uid;
        if (EditorIndex > 0)
            return EditorIndex;
        return null;
    }

    private async Task EnsureAgentAsync()
    {
        if (await _agentClient.IsReachableAsync())
            return;
        _agentLauncher.StartElevated();
        for (var i = 0; i < 20 && !await _agentClient.IsReachableAsync(); i++)
            await Task.Delay(250);
        if (!await _agentClient.IsReachableAsync())
            throw new InvalidOperationException("MemoryAgent not reachable (UAC cancelled?).");
    }

    private async Task<bool> IsLiveConnectedAsync()
    {
        try
        {
            if (!await _agentClient.IsReachableAsync())
                return false;
            var status = await _agentClient.GetStatusAsync();
            return status.Status?.LiveConnected == true;
        }
        catch
        {
            return false;
        }
    }

    private static string FormatAgent(AgentResponse response)
    {
        var s = response.Status;
        if (s is null) return response.Error ?? response.Message ?? "";
        return $"Elevated={s.IsElevated}; live={s.LiveConnected}/{s.LivePlayerCount}; attached={s.Attached}; profile={s.ProfileLoaded}; sanity={s.SanityPassed}; {s.Message}";
    }
}
