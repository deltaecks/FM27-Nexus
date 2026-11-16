using System.Text.Json;
using System.Text.Json.Serialization;

namespace FM27Nexus.Core.IPC;

public static class AgentProtocol
{
    public const string PipeName = "FM27Nexus.MemoryAgent";
    public const int ProtocolVersion = 1;

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };
}

public sealed class AgentRequest
{
    public required string Command { get; init; }
    public int ProtocolVersion { get; init; } = AgentProtocol.ProtocolVersion;
}

public sealed class AgentResponse
{
    public required bool Ok { get; init; }
    public string? Error { get; init; }
    public string? Command { get; init; }
    public AgentStatusPayload? Status { get; init; }
}

public sealed class AgentStatusPayload
{
    public required bool IsElevated { get; init; }
    public required string MachineName { get; init; }
    public required string AgentVersion { get; init; }
    public required int ProtocolVersion { get; init; }
    public required IReadOnlyList<RunningGameProcess> MatchingProcesses { get; init; }
    public required string Message { get; init; }
}

public sealed class RunningGameProcess
{
    public required int ProcessId { get; init; }
    public required string ProcessName { get; init; }
    public required string? ExecutablePath { get; init; }
    public required bool PathVerifiedAgainstInstall { get; init; }
}
