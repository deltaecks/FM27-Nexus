using FM27Nexus.Core.Discovery;
using FM27Nexus.Core.Models;

namespace FM27Nexus.Core.Tests;

public class StorefrontDiscoveryTests
{
    [Fact]
    public void DiscoverAll_DoesNotThrow()
    {
        var service = new StorefrontDiscoveryService();
        var installs = service.DiscoverAll();
        Assert.NotNull(installs);
        foreach (var install in installs)
        {
            Assert.False(string.IsNullOrWhiteSpace(install.ExecutablePath));
            Assert.True(File.Exists(install.ExecutablePath));
            Assert.Contains(install.ProcessName, new[] { "fm", "fm_gamepass" });
        }
    }

    [Fact]
    public void TryResolveManualOverride_FindsExeInTempFolder()
    {
        var temp = Path.Combine(Path.GetTempPath(), "FM27Install_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var exe = Path.Combine(temp, "fm.exe");
        File.WriteAllBytes(exe, [0x4D, 0x5A]); // MZ header stub for existence only
        try
        {
            var service = new StorefrontDiscoveryService();
            var info = service.TryResolveManualOverride(temp);
            Assert.NotNull(info);
            Assert.Equal(exe, info!.ExecutablePath);
            Assert.Equal("fm", info.ProcessName);
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    [Fact]
    public void TryResolveManualOverride_ReturnsNullWhenMissing()
    {
        var temp = Path.Combine(Path.GetTempPath(), "FM27Empty_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            var info = new StorefrontDiscoveryService().TryResolveManualOverride(temp);
            Assert.Null(info);
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    [Fact]
    public void SteamAppId_IsExpected()
    {
        Assert.Equal(4530370, StorefrontDiscoveryService.SteamAppId);
    }

    [Fact]
    public void InferViaXboxPath_ClassifiesGamePass()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "FM27GP_" + Guid.NewGuid().ToString("N"));
        var content = Path.Combine(tempRoot, "XboxGames", "Football Manager 27", "Content");
        Directory.CreateDirectory(content);
        var exe = Path.Combine(content, "fm_gamepass.exe");
        File.WriteAllBytes(exe, [0x4D, 0x5A]);
        try
        {
            var info = new StorefrontDiscoveryService().TryResolveManualOverride(content);
            Assert.NotNull(info);
            Assert.Equal(StorefrontKind.GamePassPc, info!.Storefront);
            Assert.Equal("fm_gamepass", info.ProcessName);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }
}
