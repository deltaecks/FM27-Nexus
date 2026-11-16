using FM27Nexus.Core.Save;

namespace FM27Nexus.Core.Analytics;

public sealed class MoneyballCalculator
{
    public void ApplyPer90(IEnumerable<ScoutPlayer> players)
    {
        foreach (var p in players)
        {
            if (p.Minutes is > 0)
            {
                var factor = 90.0 / p.Minutes.Value;
                p.GoalsPer90 = p.Goals is null ? null : Round(p.Goals.Value * factor);
                p.AssistsPer90 = p.Assists is null ? null : Round(p.Assists.Value * factor);
                p.ShotsPer90 = p.Shots is null ? null : Round(p.Shots.Value * factor);
            }
            else
            {
                p.GoalsPer90 = null;
                p.AssistsPer90 = null;
                p.ShotsPer90 = null;
            }

            if (p.Shots is > 0 && p.Goals is not null)
                p.ConversionPct = Round(100.0 * p.Goals.Value / p.Shots.Value);
            else
                p.ConversionPct = null;
        }
    }

    public IReadOnlyList<AgeCurvePoint> BuildAgeCurve(IEnumerable<ScoutPlayer> players)
    {
        return players
            .Where(p => p.Age is not null && p.Ca is not null)
            .GroupBy(p => p.Age!.Value)
            .OrderBy(g => g.Key)
            .Select(g => new AgeCurvePoint(g.Key, g.Average(x => x.Ca!.Value), g.Count()))
            .ToList();
    }

    public IReadOnlyList<SquadGapRow> RankSquadGaps(IEnumerable<ScoutPlayer> squad, string roleKey)
    {
        return squad
            .Select(p =>
            {
                var rca = RcaCalculator.ComputeBest(p);
                return new SquadGapRow(p.Name ?? "(unnamed)", roleKey, rca.Score, rca.Role, p.Ca, p.Pa, p.Age);
            })
            .OrderByDescending(r => r.Rca)
            .ToList();
    }

    private static double Round(double value) => Math.Round(value, 2);
}

public sealed record AgeCurvePoint(int Age, double AverageCa, int SampleSize);
public sealed record SquadGapRow(string Name, string RoleKey, double Rca, string Role, int? Ca, int? Pa, int? Age);
