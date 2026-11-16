using System.Reflection;

namespace FM27Nexus.Core.IPC;

public sealed class MemoryAgentLauncher
{
    public string ResolveAgentPath()
    {
        var baseDir = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDir, "FM27Nexus.MemoryAgent.exe"),
            Path.Combine(baseDir, "..", "FM27Nexus.MemoryAgent", "FM27Nexus.MemoryAgent.exe"),
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "FM27Nexus.MemoryAgent", "bin", "Debug", "net8.0-windows", "FM27Nexus.MemoryAgent.exe")),
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "FM27Nexus.MemoryAgent", "bin", "Release", "net8.0-windows", "FM27Nexus.MemoryAgent.exe"))
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
                return Path.GetFullPath(candidate);
        }

        throw new FileNotFoundException(
            "FM27Nexus.MemoryAgent.exe was not found next to the application. Publish both binaries into the same folder.",
            "FM27Nexus.MemoryAgent.exe");
    }

    /// <summary>
    /// Launches the elevated MemoryAgent. Triggers UAC via runas when not already elevated.
    /// </summary>
    public System.Diagnostics.Process StartElevated()
    {
        var path = ResolveAgentPath();
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true,
            Verb = "runas",
            WorkingDirectory = Path.GetDirectoryName(path) ?? AppContext.BaseDirectory
        };

        var process = System.Diagnostics.Process.Start(psi);
        if (process is null)
            throw new InvalidOperationException("Failed to start FM27Nexus.MemoryAgent.exe (UAC may have been cancelled).");

        return process;
    }

    public static string GetUiVersion()
    {
        return Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "0.1.0";
    }
}
