using FM27Nexus.Core.GameProcess;
using FM27Nexus.Core.Models;

namespace FM27Nexus.Core.Tests;

public class ProcessFingerprintTests
{
    [Fact]
    public void ExactProcessNames_AreNotSubstringFm()
    {
        Assert.DoesNotContain(ProcessFingerprint.ExactProcessNames, n => n == "7zFM");
        Assert.Contains("fm", ProcessFingerprint.ExactProcessNames);
        Assert.Contains("fm_gamepass", ProcessFingerprint.ExactProcessNames);
    }

    [Fact]
    public void FindMatchingProcesses_AcceptsEmptyInstallList()
    {
        var result = new ProcessFingerprint().FindMatchingProcesses(Array.Empty<GameInstallInfo>());
        Assert.NotNull(result);
    }
}
