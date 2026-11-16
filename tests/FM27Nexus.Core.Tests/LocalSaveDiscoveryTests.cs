using FM27Nexus.Core.Save;

namespace FM27Nexus.Core.Tests;

public class LocalSaveDiscoveryTests
{
    [Fact]
    public void FindCurrentSave_PrefersNonBackupWhenPresent()
    {
        var discovery = new LocalSaveDiscovery();
        var current = discovery.FindCurrentSave();
        if (current is null)
            return;

        Assert.True(File.Exists(current.Path));
        Assert.EndsWith(".fm", current.Path, StringComparison.OrdinalIgnoreCase);
        if (discovery.ListSaves().Any(s => !s.IsAutoBackup))
            Assert.False(current.IsAutoBackup);
    }
}
