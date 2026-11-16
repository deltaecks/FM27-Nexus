using System.Text.Json;
using System.Text.Json.Serialization;
using FM27Nexus.Core.Editor;

namespace FM27Nexus.Core.IPC;

public static class AgentProtocol
{
    public const string PipeName = "FM27Nexus.MemoryAgent";
    public const int ProtocolVersion = 3;

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
    public long? LivePlayerId { get; init; }
    public long? LiveEntityId { get; init; }
    public string? ClubName { get; init; }
    public Dictionary<string, long>? Fields { get; init; }
    public List<string>? FieldNames { get; init; }
    public List<long>? PlayerIds { get; init; }
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
    public int? LivePlayerCount { get; init; }
    public int? LiveClubCount { get; init; }
    public int? LiveTeamCount { get; init; }
    public string? LiveDefinitionVersion { get; init; }
    public string? LiveModuleVersion { get; init; }
    public IReadOnlyList<string>? AppliedFields { get; init; }
    public Dictionary<string, long?>? FieldValues { get; init; }
    public IReadOnlyList<TacticalFactorStatusDto>? TacticalFactors { get; init; }
    public long? ResolvedClubId { get; init; }
    public IReadOnlyList<string>? ClubFields { get; init; }
}

public sealed class TacticalFactorStatusDto
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required string Group { get; init; }
    public required bool IsMapped { get; init; }
    public string? Entity { get; init; }
    public string? FieldKey { get; init; }
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
    public bool LiveConnected { get; init; }
    public int LivePlayerCount { get; init; }
    public int LiveClubCount { get; init; }
    public int LiveTeamCount { get; init; }
    public string? LiveDefinitionVersion { get; init; }
    public string? LiveModuleVersion { get; init; }
}

public sealed class RunningGameProcess
{
    public required int ProcessId { get; init; }
    public required string ProcessName { get; init; }
    public required string? ExecutablePath { get; init; }
    public required bool PathVerifiedAgainstInstall { get; init; }
}
