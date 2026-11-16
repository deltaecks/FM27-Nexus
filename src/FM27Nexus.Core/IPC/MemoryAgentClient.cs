using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace FM27Nexus.Core.IPC;

public sealed class MemoryAgentClient
{
    private readonly TimeSpan _timeout;

    public MemoryAgentClient(TimeSpan? timeout = null)
    {
        _timeout = timeout ?? TimeSpan.FromSeconds(30);
    }

    public Task<AgentResponse> PingAsync(CancellationToken cancellationToken = default) =>
        SendAsync(new AgentRequest { Command = "Ping" }, cancellationToken);

    public Task<AgentResponse> GetStatusAsync(CancellationToken cancellationToken = default) =>
        SendAsync(new AgentRequest { Command = "GetStatus" }, cancellationToken);

    public Task<AgentResponse> SendAsync(AgentRequest request, CancellationToken cancellationToken = default) =>
        SendCoreAsync(request, cancellationToken);

    public async Task<bool> IsReachableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await PingAsync(cancellationToken).ConfigureAwait(false);
            return response.Ok;
        }
        catch
        {
            return false;
        }
    }

    private async Task<AgentResponse> SendCoreAsync(AgentRequest request, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(_timeout);

        await using var pipe = new NamedPipeClientStream(".", AgentProtocol.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(cts.Token).ConfigureAwait(false);

        var payload = JsonSerializer.Serialize(request, AgentProtocol.JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(payload + "\n");
        await pipe.WriteAsync(bytes, cts.Token).ConfigureAwait(false);
        await pipe.FlushAsync(cts.Token).ConfigureAwait(false);

        using var reader = new StreamReader(pipe, Encoding.UTF8, false, leaveOpen: true);
        var line = await reader.ReadLineAsync(cts.Token).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(line))
            throw new InvalidOperationException("MemoryAgent returned an empty response.");

        return JsonSerializer.Deserialize<AgentResponse>(line, AgentProtocol.JsonOptions)
               ?? throw new InvalidOperationException("MemoryAgent returned an unreadable response.");
    }
}
