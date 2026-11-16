using FM27Nexus.Core.Memory;
using FM27Nexus.Core.Paths;
using FM27Nexus.Core.Save.Fmf;
using FM27Nexus.Core.Vault;

namespace FM27Nexus.Core.Tests;

public class VaultAndOffsetTests
{
    [Fact]
    public void OffsetProvider_RejectsMissingProfile()
    {
        var temp = Path.Combine(Path.GetTempPath(), "FM27Off_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            var paths = new AppPaths(temp);
            var provider = new JsonOffsetProvider(paths);
            var result = provider.LoadForBuild("no-such-build", "steam");
            Assert.False(result.Ok);
            Assert.Contains("No offset profile", result.Error);
        }
        finally
        {
            Directory.Delete(temp, true);
        }
    }

    [Fact]
    public void Vault_SnapshotsRealSave_WhenPresent()
    {
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var games = Path.Combine(docs, "Sports Interactive", "Football Manager 27", "games");
        if (!Directory.Exists(games)) return;
        var save = Directory.EnumerateFiles(games, "*.fm").OrderBy(f => new FileInfo(f).Length).FirstOrDefault();
        if (save is null) return;

        // Use a smaller save if possible for speed; integrity still required.
        var tempRoot = Path.Combine(Path.GetTempPath(), "FM27Vault_" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPaths(tempRoot);
            var vault = new SaveVaultService(paths);
            var entry = vault.CreateSnapshot(save, "test");
            Assert.True(File.Exists(entry.BackupPath));
            Assert.True(entry.SectionCount > 0);
            Assert.False(string.IsNullOrWhiteSpace(entry.Sha256));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, true);
        }
    }

    [Fact]
    public void ModDetect_RecognizesFmf()
    {
        Assert.Equal(FM27Nexus.Core.Mods.ModKind.EditorData,
            FM27Nexus.Core.Mods.ModManagerService.DetectKind(@"C:\mods\pack.fmf"));
    }
}
