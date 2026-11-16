using System.Text.Json;
using System.Text.Json.Serialization;
using FM27Nexus.Core.Editor;

namespace FM27Nexus.Core.IPC;

public static class AgentProtocol
{
    public const string PipeName = "FM27Nexus.MemoryAgent";
    public const int ProtocolVersion = 2;

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
}

public sealed class AgentRequest
{
    public required string Command { get; init; }
    public int ProtocolVersion { get; init; } = AgentProtocol.ProtocolVersion;
    public int? ProcessId { get; init; }
    public string? ExecutablePath { get; init; }
    public string? BuildId { get; init; }
    public string? Storefront { get; init; }
    public string? PackPath { get; init; }
    public string? Entity { get; init; }
    public int? Index { get; init; }
    public string? Field { get; init; }
    public long? Value { get; init; }
    public bool? IsFreeze { get; init; }
    public bool? ConfirmFreezeWarning { get; init; }
    public string? EditId { get; init; }
    public string? BackupSavePath { get; init; }
}

public sealed class AgentResponse
{
    public required bool Ok { get; init; }
    public string? Error { get; init; }
    public string? Command { get; init; }
    public string? Message { get; init; }
    public AgentStatusPayload? Status { get; init; }
    public long? Value { get; init; }
    public bool? WithinBounds { get; init; }
    public bool? WritesEnabled { get; init; }
    public IReadOnlyList<PendingEdit>? Edits { get; init; }
    public string? ProfilePath { get; init; }
}

public sealed class AgentStatusPayload
{
    public required bool IsElevated { get; init; }
    public required string MachineName { get; init; }
    public required string AgentVersion { get; init; }
    public required int ProtocolVersion { get; init; }
    public required IReadOnlyList<RunningGameProcess> MatchingProcesses { get; init; }
    public required string Message { get; init; }
    public bool Attached { get; init; }
    public int? AttachedPid { get; init; }
    public string? ActiveBuildId { get; init; }
    public string? ActiveStorefront { get; init; }
    public bool ProfileLoaded { get; init; }
    public bool SanityPassed { get; init; }
    public bool SessionBackupTaken { get; init; }
}

public sealed class RunningGameProcess
{
    public required int ProcessId { get; init; }
    public required string ProcessName { get; init; }
    public required string? ExecutablePath { get; init; }
    public required bool PathVerifiedAgainstInstall { get; init; }
}
