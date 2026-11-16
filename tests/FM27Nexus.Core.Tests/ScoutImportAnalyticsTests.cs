using FM27Nexus.Core.Analytics;
using FM27Nexus.Core.Save;
using FM27Nexus.Core.Save.Import;
using FM27Nexus.Core.Tactics;

namespace FM27Nexus.Core.Tests;

public class ScoutImportAnalyticsTests
{
    private static string Fixture(string name)
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "fixtures", name),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "fixtures", name)),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "fixtures", name))
        };
        return candidates.First(File.Exists);
    }

    [Fact]
    public void LoadJsonFixture_AndComputeMoneyballAndFit()
    {
        var path = Fixture("players_sample.json");
        Assert.True(File.Exists(path), "Missing fixture " + path);
        var players = new ScoutTableJsonLoader().Load(path).ToList();
        Assert.NotEmpty(players);

        foreach (var p in players.Take(10))
        {
            p.Minutes ??= 900;
            p.Goals ??= 3;
            p.Assists ??= 1;
            p.Shots ??= 10;
        }

        RcaCalculator.Apply(players);
        new MoneyballCalculator().ApplyPer90(players);
        new DualPhaseFitScorer().ApplyBestFits(players, DualPhaseFitScorer.DefaultRoles());

        Assert.Contains(players, p => p.RcaBest is > 0);
        Assert.Contains(players, p => p.GoalsPer90 is not null);
        Assert.Contains(players, p => p.IpFitPct is not null);
    }

    [Fact]
    public void ImportCsvFixture()
    {
        var path = Fixture("players_sample.csv");
        Assert.True(File.Exists(path));
        var players = new FmHtmlCsvImporter().ImportCsv(path);
        Assert.NotEmpty(players);
        Assert.All(players, p => Assert.Equal(FieldStatus.Verified, p.FieldStatus));
    }
}
