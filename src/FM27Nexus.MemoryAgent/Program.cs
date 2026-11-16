using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using FM27Nexus.Core.Discovery;
using FM27Nexus.Core.Editor;
using FM27Nexus.Core.GameProcess;
using FM27Nexus.Core.IPC;
using FM27Nexus.Core.Memory;
using FM27Nexus.Core.Paths;
using FM27Nexus.Core.Vault;

namespace FM27Nexus.MemoryAgent;

internal static class Program
{
    private static readonly CancellationTokenSource AppCts = new();
    private static readonly AppPaths Paths = new();
    private static readonly JsonOffsetProvider Offsets = new(Paths);
    private static readonly ProcessMemoryScanner Scanner = new(Offsets);
    private static readonly EditQueue Queue = new();
    private static readonly EditorContext EditorContext = new();
    private static readonly SaveVaultService Vault = new(Paths);
    private static ScanValidationResult? _lastSanity;
    private static bool _sessionBackupTaken;
    private static string? _activeBuildId;
    private static string? _activeStorefront;

    private static async Task<int> Main(string[] args)
    {
        Console.Title = "FM27 Nexus Memory Agent";
        Console.WriteLine("FM27 Nexus Memory Agent (Milestone 4)");
        Console.WriteLine("Pipe: " + AgentProtocol.PipeName);
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            AppCts.Cancel();
        };

        try
        {
            await RunPipeServerAsync(AppCts.Token).ConfigureAwait(false);
            return 0;
        }
        catch (OperationCanceledException)
        {
            return 0;
        }
        finally
        {
            Scanner.Dispose();
        }
    }

    private static async Task RunPipeServerAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await using var server = new NamedPipeServerStream(
                AgentProtocol.PipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);

            await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                using var reader = new StreamReader(server, Encoding.UTF8, false, leaveOpen: true);
                await using var writer = new StreamWriter(server, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };
                var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                var response = Handle(line);
                await writer.WriteLineAsync(JsonSerializer.Serialize(response, AgentProtocol.JsonOptions).AsMemory(), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Console.Error.WriteLine(ex.Message);
            }
        }
    }

    private static AgentResponse Handle(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return Fail("Empty request.");

        AgentRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<AgentRequest>(line, AgentProtocol.JsonOptions);
        }
        catch (JsonException ex)
        {
            return Fail("Invalid JSON: " + ex.Message);
        }

        if (request is null || string.IsNullOrWhiteSpace(request.Command))
            return Fail("Missing command.");

        return request.Command switch
        {
            "Ping" => Ok("Ping", "Pong"),
            "GetStatus" => Status("Status collected."),
            "LoadProfile" => LoadProfile(request),
            "ImportPack" => ImportPack(request),
            "Attach" => Attach(request),
            "Detach" => Detach(),
            "ValidateSanity" => ValidateSanity(),
            "ReadField" => ReadField(request),
            "StageEdit" => StageEdit(request),
            "ApplyPending" => ApplyPending(request),
            "UndoLast" => UndoLast(),
            "GetEdits" => new AgentResponse { Ok = true, Command = "GetEdits", Edits = Queue.Edits.ToList() },
            _ => Fail($"Unsupported command '{request.Command}'.")
        };
    }

    private static AgentResponse LoadProfile(AgentRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.BuildId) || string.IsNullOrWhiteSpace(request.Storefront))
            return Fail("BuildId and Storefront are required.");
        var result = Offsets.LoadForBuild(request.BuildId, request.Storefront);
        if (!result.Ok)
            return Fail(result.Error ?? "Load failed.");
        _activeBuildId = request.BuildId;
        _activeStorefront = request.Storefront;
        _lastSanity = null;
        return Ok("LoadProfile", "Profile loaded: " + result.Path, profilePath: result.Path);
    }

    private static AgentResponse ImportPack(AgentRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.PackPath))
            return Fail("PackPath required.");
        var result = Offsets.ImportCommunityPack(request.PackPath);
        if (!result.Ok)
            return Fail(result.Error ?? "Import failed.");
        _activeBuildId = result.Profile!.BuildId;
        _activeStorefront = result.Profile.Storefront;
        return Ok("ImportPack", "Imported " + result.Path, profilePath: result.Path);
    }

    private static AgentResponse Attach(AgentRequest request)
    {
        if (request.ProcessId is null || string.IsNullOrWhiteSpace(request.ExecutablePath))
            return Fail("ProcessId and ExecutablePath required.");
        var result = Scanner.Attach(request.ProcessId.Value, request.ExecutablePath);
        if (!result.Ok)
            return Fail(result.Error ?? "Attach failed.");
        return Status(result.Error ?? "Attached.");
    }

    private static AgentResponse Detach()
    {
        Scanner.Detach();
        _lastSanity = null;
        return Status("Detached.");
    }

    private static AgentResponse ValidateSanity()
    {
        _lastSanity = Scanner.ValidateReadSanity();
        return new AgentResponse
        {
            Ok = _lastSanity.Ok,
            Command = "ValidateSanity",
            Message = _lastSanity.Ok
                ? $"Sanity passed {_lastSanity.FieldsPassed}/{_lastSanity.FieldsChecked}."
                : _lastSanity.Error,
            WritesEnabled = _lastSanity.Ok,
            Status = BuildStatusPayload(_lastSanity.Ok ? "Sanity OK" : _lastSanity.Error ?? "Sanity failed")
        };
    }

    private static AgentResponse ReadField(AgentRequest request)
    {
        if (request.Entity is null || request.Index is null || request.Field is null)
            return Fail("Entity, Index, Field required.");
        var read = Scanner.ReadField(request.Entity, request.Index.Value, request.Field);
        return new AgentResponse
        {
            Ok = read.Ok,
            Command = "ReadField",
            Error = read.Error,
            Value = read.Value,
            WithinBounds = read.WithinExpectedBounds
        };
    }

    private static AgentResponse StageEdit(AgentRequest request)
    {
        if (request.Entity is null || request.Index is null || request.Field is null || request.Value is null)
            return Fail("Entity, Index, Field, Value required.");

        var edit = Queue.Stage(request.Entity, request.Index.Value, request.Field, request.Value.Value, request.IsFreeze == true);
        var safety = EditorSafety.Evaluate(edit, Queue, EditorContext);
        if (safety is not null && safety.StartsWith("Blocked", StringComparison.OrdinalIgnoreCase))
        {
            Queue.MarkBlocked(edit.Id, safety);
            return new AgentResponse { Ok = false, Command = "StageEdit", Error = safety, Edits = Queue.Edits.ToList() };
        }

        if (safety is not null && safety.StartsWith("Warning", StringComparison.OrdinalIgnoreCase) && request.ConfirmFreezeWarning != true)
        {
            return new AgentResponse { Ok = false, Command = "StageEdit", Error = safety, Edits = Queue.Edits.ToList() };
        }

        return new AgentResponse { Ok = true, Command = "StageEdit", Message = "Staged " + edit.Id, Edits = Queue.Edits.ToList() };
    }

    private static AgentResponse ApplyPending(AgentRequest request)
    {
        if (_lastSanity is not { Ok: true })
            return Fail("Writes require a successful ValidateSanity first.");

        if (!_sessionBackupTaken)
        {
            if (string.IsNullOrWhiteSpace(request.BackupSavePath) || !File.Exists(request.BackupSavePath))
                return Fail("BackupSavePath (.fm) required before the first write of a session.");
            try
            {
                Vault.CreateSnapshot(request.BackupSavePath, "Pre-write session backup");
                _sessionBackupTaken = true;
            }
            catch (Exception ex)
            {
                return Fail("Backup failed: " + ex.Message);
            }
        }

        foreach (var edit in Queue.Pending.ToList())
        {
            var before = Scanner.ReadField(edit.Entity, edit.Index, edit.Field);
            var write = Scanner.WriteField(edit.Entity, edit.Index, edit.Field, edit.NewValue);
            if (!write.Ok)
            {
                Queue.MarkBlocked(edit.Id, write.Error ?? "Write failed");
                return new AgentResponse { Ok = false, Command = "ApplyPending", Error = write.Error, Edits = Queue.Edits.ToList() };
            }

            Queue.MarkApplied(edit.Id, before.Value);
        }

        return new AgentResponse { Ok = true, Command = "ApplyPending", Message = "Applied pending edits.", Edits = Queue.Edits.ToList(), WritesEnabled = true };
    }

    private static AgentResponse UndoLast()
    {
        var edit = Queue.PopUndo();
        if (edit is null)
            return Fail("Nothing to undo.");
        if (edit.OldValue is null)
            return Fail("Missing old value for undo.");
        var write = Scanner.WriteField(edit.Entity, edit.Index, edit.Field, edit.OldValue.Value, force: true);
        if (!write.Ok)
            return Fail(write.Error ?? "Undo write failed.");
        Queue.MarkUndone(edit.Id);
        return new AgentResponse { Ok = true, Command = "UndoLast", Message = "Undone " + edit.Id, Edits = Queue.Edits.ToList() };
    }

    private static AgentResponse Status(string message) => new()
    {
        Ok = true,
        Command = "GetStatus",
        Message = message,
        Status = BuildStatusPayload(message),
        WritesEnabled = _lastSanity?.Ok == true,
        Edits = Queue.Edits.ToList(),
        ProfilePath = Offsets.ActiveProfile is null ? null : "loaded"
    };

    private static AgentResponse Ok(string command, string message, string? profilePath = null) => new()
    {
        Ok = true,
        Command = command,
        Message = message,
        ProfilePath = profilePath,
        Status = BuildStatusPayload(message)
    };

    private static AgentResponse Fail(string error) => new()
    {
        Ok = false,
        Error = error,
        Status = BuildStatusPayload(error)
    };

    private static AgentStatusPayload BuildStatusPayload(string message)
    {
        var discovery = new StorefrontDiscoveryService();
        var installs = discovery.DiscoverAll();
        var matching = new ProcessFingerprint().FindMatchingProcesses(installs);
        return new AgentStatusPayload
        {
            IsElevated = IsElevated(),
            MachineName = Environment.MachineName,
            AgentVersion = typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.1.0",
            ProtocolVersion = AgentProtocol.ProtocolVersion,
            MatchingProcesses = matching,
            Message = message,
            Attached = Scanner.IsAttached,
            AttachedPid = Scanner.ProcessId,
            ActiveBuildId = _activeBuildId,
            ActiveStorefront = _activeStorefront,
            ProfileLoaded = Offsets.ActiveProfile is not null,
            SanityPassed = _lastSanity?.Ok == true,
            SessionBackupTaken = _sessionBackupTaken
        };
    }

    private static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }
}
