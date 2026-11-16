using System.Runtime.InteropServices;
using System.Text;
using FM27Nexus.Core.IPC;
using FM27Nexus.Core.Models;

namespace FM27Nexus.Core.GameProcess;

/// <summary>
/// Matches exact FM27 process names and verifies executable paths against known installs.
/// Never uses substring matching on "fm".
/// </summary>
public sealed class ProcessFingerprint
{
    public static readonly string[] ExactProcessNames = ["fm", "fm_gamepass"];

    public IReadOnlyList<RunningGameProcess> FindMatchingProcesses(IEnumerable<GameInstallInfo> installs)
    {
        var installRoots = installs
            .Select(i => Path.GetFullPath(i.InstallDirectory))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var results = new List<RunningGameProcess>();

        foreach (var name in ExactProcessNames)
        {
            System.Diagnostics.Process[] processes;
            try
            {
                processes = System.Diagnostics.Process.GetProcessesByName(name);
            }
            catch
            {
                continue;
            }

            foreach (var proc in processes)
            {
                string? exePath = null;
                try
                {
                    exePath = TryGetMainModulePath(proc);
                }
                catch
                {
                    // Access denied without elevation is a real state; still report the process.
                }

                var pathVerified = false;
                if (!string.IsNullOrWhiteSpace(exePath))
                {
                    var fullExe = Path.GetFullPath(exePath);
                    var dir = Path.GetDirectoryName(fullExe) ?? fullExe;
                    pathVerified = installRoots.Any(root =>
                        dir.StartsWith(root, StringComparison.OrdinalIgnoreCase) ||
                        fullExe.StartsWith(root, StringComparison.OrdinalIgnoreCase));
                }

                results.Add(new RunningGameProcess
                {
                    ProcessId = proc.Id,
                    ProcessName = proc.ProcessName,
                    ExecutablePath = exePath,
                    PathVerifiedAgainstInstall = pathVerified
                });
            }
        }

        return results;
    }

    private static string? TryGetMainModulePath(System.Diagnostics.Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch (Exception)
        {
            return QueryFullProcessImageName(process.Id);
        }
    }

    private static string? QueryFullProcessImageName(int pid)
    {
        var handle = OpenProcess(ProcessQueryLimitedInformation, false, pid);
        if (handle == IntPtr.Zero)
            return null;

        try
        {
            var buffer = new StringBuilder(1024);
            var size = (uint)buffer.Capacity;
            if (!QueryFullProcessImageName(handle, 0, buffer, ref size))
                return null;
            return buffer.ToString();
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    private const uint ProcessQueryLimitedInformation = 0x1000;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(IntPtr hProcess, uint dwFlags, StringBuilder lpExeName, ref uint lpdwSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);
}
