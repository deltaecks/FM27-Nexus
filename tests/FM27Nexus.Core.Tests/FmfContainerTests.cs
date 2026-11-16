using FM27Nexus.Core.Save.Fmf;

namespace FM27Nexus.Core.Tests;

public class FmfContainerTests
{
    private static string? FindSampleSave()
    {
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var games = Path.Combine(docs, "Sports Interactive", "Football Manager 27", "games");
        if (!Directory.Exists(games)) return null;
        return Directory.EnumerateFiles(games, "*.fm").OrderByDescending(f => new FileInfo(f).Length).FirstOrDefault();
    }

    [Fact]
    public void ReadIndex_OnRealFm27Save_Succeeds()
    {
        var save = FindSampleSave();
        if (save is null)
        {
            // Environment without FM27 saves — skip without inventing data.
            return;
        }

        var index = new FmfContainerReader().ReadIndex(save);
        Assert.False(string.IsNullOrWhiteSpace(index.SaveName));
        Assert.True(index.Sections.Count > 0);
        Assert.Contains(index.Sections.Keys, k => k.Length > 0);
    }

    [Fact]
    public void Validate_RejectsNonSave()
    {
        var temp = Path.GetTempFileName();
        File.WriteAllText(temp, "not an fm save");
        try
        {
            var report = new FmfContainerReader().Validate(temp);
            Assert.False(report.Ok);
            Assert.NotEmpty(report.Issues);
        }
        finally
        {
            File.Delete(temp);
        }
    }
}
