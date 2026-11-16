using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using FM27Nexus.Core.Discovery;
using FM27Nexus.Core.IPC;
using FM27Nexus.Core.GameProcess;

namespace FM27Nexus.MemoryAgent;

internal static class Program
{
    private static readonly CancellationTokenSource AppCts = new();

    private static async Task<int> Main(string[] args)
    {
        Console.Title = "FM27 Nexus Memory Agent";
        Console.WriteLine("FM27 Nexus Memory Agent (Milestone 1)");
        Console.WriteLine("Named pipe: " + AgentProtocol.PipeName);
        Console.WriteLine("Commands: Ping, GetStatus. Live memory R/W arrives in Milestone 4.");
        Console.WriteLine("Press Ctrl+C to exit.");

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
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static async Task RunPipeServerAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await using var server = new NamedPipeServerStream(
                AgentProtocol.PipeName,
                PipeDirection.InOut,
                maxNumberOfServerInstances: 1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);

            await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                using var reader = new StreamReader(server, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
                await using var writer = new StreamWriter(server, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };

                var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                var response = HandleLine(line);
                var json = JsonSerializer.Serialize(response, AgentProtocol.JsonOptions);
                await writer.WriteLineAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Console.Error.WriteLine("Client session error: " + ex.Message);
            }
        }
    }

    private static AgentResponse HandleLine(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return new AgentResponse
            {
                Ok = false,
                Error = "Empty request."
            };
        }

        AgentRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<AgentRequest>(line, AgentProtocol.JsonOptions);
        }
        catch (JsonException ex)
        {
            return new AgentResponse
            {
                Ok = false,
                Error = "Invalid JSON: " + ex.Message
            };
        }

        if (request is null || string.IsNullOrWhiteSpace(request.Command))
        {
            return new AgentResponse
            {
                Ok = false,
                Error = "Missing command."
            };
        }

        return request.Command switch
        {
            "Ping" => new AgentResponse
            {
                Ok = true,
                Command = "Ping",
                Status = BuildStatus("Pong")
            },
            "GetStatus" => new AgentResponse
            {
                Ok = true,
                Command = "GetStatus",
                Status = BuildStatus("Status collected without memory attach (Milestone 1).")
            },
            _ => new AgentResponse
            {
                Ok = false,
                Command = request.Command,
                Error = $"Unsupported command '{request.Command}'. Milestone 1 supports Ping and GetStatus only."
            }
        };
    }

    private static AgentStatusPayload BuildStatus(string message)
    {
        var discovery = new StorefrontDiscoveryService();
        var installs = discovery.DiscoverAll();
        var fingerprint = new ProcessFingerprint();
        var matching = fingerprint.FindMatchingProcesses(installs);

        return new AgentStatusPayload
        {
            IsElevated = IsElevated(),
            MachineName = Environment.MachineName,
            AgentVersion = typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.1.0",
            ProtocolVersion = AgentProtocol.ProtocolVersion,
            MatchingProcesses = matching,
            Message = message
        };
    }

    private static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }
}
