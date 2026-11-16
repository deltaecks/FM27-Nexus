using FM27Nexus.Core.Paths;

namespace FM27Nexus.Core.Tests;

public class AppPathsTests
{
    [Fact]
    public void EnsureCreated_WritesUnderAppDataNotVelopackCurrent()
    {
        var temp = Path.Combine(Path.GetTempPath(), "FM27NexusTests", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPaths(temp);
            paths.EnsureCreated();

            Assert.True(Directory.Exists(paths.Settings));
            Assert.True(Directory.Exists(paths.Backups));
            Assert.True(Directory.Exists(paths.Offsets));
            Assert.False(AppPaths.IsUnderVelopackCurrent(paths.SettingsFile));
        }
        finally
        {
            if (Directory.Exists(temp))
                Directory.Delete(temp, recursive: true);
        }
    }

    [Fact]
    public void IsUnderVelopackCurrent_DetectsCurrentFolder()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var underCurrent = Path.Combine(local, "FM27Nexus", "current", "settings.json");
        Assert.True(AppPaths.IsUnderVelopackCurrent(underCurrent));
    }
}
