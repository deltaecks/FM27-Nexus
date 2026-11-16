using FM27Nexus.Core.Save;

namespace FM27Nexus.Core.Analytics;

public static class RcaCalculator
{
    public static (string Role, double Score) ComputeBest(ScoutPlayer p)
    {
        var roles = new (string Role, double Score)[]
        {
            ("GK", Avg(p.Positioning, p.Anticipation, p.Decisions, p.Concentration, p.Agility, p.JumpingReach)),
            ("CD", Avg(p.Tackling, p.Marking, p.Positioning, p.Heading, p.Strength, p.JumpingReach, p.Concentration)),
            ("FB", Avg(p.Tackling, p.Marking, p.Pace, p.Acceleration, p.Stamina, p.Crossing, p.WorkRate)),
            ("DM", Avg(p.Tackling, p.Marking, p.Positioning, p.Passing, p.Decisions, p.Teamwork, p.Stamina)),
            ("CM", Avg(p.Passing, p.FirstTouch, p.Decisions, p.Teamwork, p.Vision, p.Stamina, p.Technique)),
            ("AM", Avg(p.Passing, p.Vision, p.Flair, p.FirstTouch, p.Technique, p.OffTheBall, p.Composure)),
            ("W", Avg(p.Pace, p.Acceleration, p.Dribbling, p.Crossing, p.FirstTouch, p.Technique, p.OffTheBall)),
            ("ST", Avg(p.Finishing, p.OffTheBall, p.Composure, p.Heading, p.Pace, p.Acceleration, p.Strength))
        };

        return roles.OrderByDescending(r => r.Score).First();
    }

    public static void Apply(IEnumerable<ScoutPlayer> players)
    {
        foreach (var p in players)
        {
            var best = ComputeBest(p);
            p.RcaBest = best.Score;
            p.RcaBestRole = best.Role;
        }
    }

    private static double Avg(params int?[] values)
    {
        var present = values.Where(v => v is not null).Select(v => v!.Value).ToArray();
        return present.Length == 0 ? 0 : Math.Round(present.Average(), 2);
    }
}
