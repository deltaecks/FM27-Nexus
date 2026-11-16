using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace FM27Nexus.Core.IPC;

public sealed class MemoryAgentClient
{
    private readonly TimeSpan _timeout;

    public MemoryAgentClient(TimeSpan? timeout = null)
    {
        _timeout = timeout ?? TimeSpan.FromSeconds(5);
    }

    public async Task<AgentResponse> PingAsync(CancellationToken cancellationToken = default)
    {
        return await SendAsync(new AgentRequest { Command = "Ping" }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<AgentResponse> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        return await SendAsync(new AgentRequest { Command = "GetStatus" }, cancellationToken).ConfigureAwait(false);
    }

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

    private async Task<AgentResponse> SendAsync(AgentRequest request, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(_timeout);

        await using var pipe = new NamedPipeClientStream(
            ".",
            AgentProtocol.PipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);

        await pipe.ConnectAsync(cts.Token).ConfigureAwait(false);

        var payload = JsonSerializer.Serialize(request, AgentProtocol.JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(payload + "\n");
        await pipe.WriteAsync(bytes, cts.Token).ConfigureAwait(false);
        await pipe.FlushAsync(cts.Token).ConfigureAwait(false);

        using var reader = new StreamReader(pipe, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        var line = await reader.ReadLineAsync(cts.Token).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(line))
            throw new InvalidOperationException("MemoryAgent returned an empty response.");

        var response = JsonSerializer.Deserialize<AgentResponse>(line, AgentProtocol.JsonOptions);
        return response ?? throw new InvalidOperationException("MemoryAgent returned an unreadable response.");
    }
}
