using FM27Nexus.Core.Paths;
using FM27Nexus.Core.Settings;

namespace FM27Nexus.Core.Tests;

public class SettingsStoreTests
{
    [Fact]
    public void SaveAndLoad_RoundTripsOverride()
    {
        var temp = Path.Combine(Path.GetTempPath(), "FM27Settings_" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPaths(temp);
            var store = new SettingsStore(paths);
            var settings = new AppSettings
            {
                DocumentsPathOverride = @"C:\Custom\Sports Interactive\Football Manager 27",
                GitHubRepoUrl = "https://github.com/deltaecks/FM27-Nexus"
            };
            store.Save(settings);

            var loaded = store.Load();
            Assert.Equal(settings.DocumentsPathOverride, loaded.DocumentsPathOverride);
            Assert.Equal(settings.GitHubRepoUrl, loaded.GitHubRepoUrl);
        }
        finally
        {
            if (Directory.Exists(temp))
                Directory.Delete(temp, recursive: true);
        }
    }
}
