using FM27Nexus.Core.Save;

namespace FM27Nexus.Core.Tactics;

public sealed class DualPhaseFitScorer
{
    public DualPhaseScore Score(ScoutPlayer player, TacticalRole role)
    {
        var ip = Weighted(player, role.InPossessionWeights);
        var oop = Weighted(player, role.OutOfPossessionWeights);
        var style = Weighted(player, role.StyleWeights);
        var posMult = PositionFamiliarity(player, role.PositionKey);
        return new DualPhaseScore(
            Math.Round(ip * posMult, 1),
            Math.Round(oop * posMult, 1),
            Math.Round(style * posMult, 1),
            role.Name);
    }

    public void ApplyBestFits(IEnumerable<ScoutPlayer> players, IReadOnlyList<TacticalRole> roles)
    {
        foreach (var player in players)
        {
            DualPhaseScore? best = null;
            foreach (var role in roles)
            {
                var score = Score(player, role);
                if (best is null || score.FitPct > best.FitPct)
                    best = score;
            }

            if (best is null) continue;
            player.IpFitPct = best.IpFitPct;
            player.OopFitPct = best.OopFitPct;
            player.StylePct = best.StylePct;
        }
    }

    public static IReadOnlyList<TacticalRole> DefaultRoles() =>
    [
        new TacticalRole("Central Defender (IP Cover / OOP Defend)", "DC",
            new Dictionary<string, double> { ["heading"] = 1, ["jumpingReach"] = 1, ["passing"] = 0.8, ["composure"] = 0.8 },
            new Dictionary<string, double> { ["tackling"] = 1.2, ["marking"] = 1.2, ["positioning"] = 1.2, ["concentration"] = 1, ["strength"] = 0.8 },
            new Dictionary<string, double> { ["decisions"] = 1, ["teamwork"] = 0.8, ["bravery"] = 0.7 }),
        new TacticalRole("Box-to-Box Midfielder", "MC",
            new Dictionary<string, double> { ["passing"] = 1, ["firstTouch"] = 0.8, ["technique"] = 0.8, ["offTheBall"] = 0.9, ["stamina"] = 1 },
            new Dictionary<string, double> { ["tackling"] = 0.9, ["workRate"] = 1.1, ["positioning"] = 0.8, ["stamina"] = 1 },
            new Dictionary<string, double> { ["teamwork"] = 1, ["decisions"] = 0.9, ["determination"] = 0.8 }),
        new TacticalRole("Advanced Forward", "STC",
            new Dictionary<string, double> { ["finishing"] = 1.3, ["offTheBall"] = 1.2, ["composure"] = 1, ["pace"] = 1, ["acceleration"] = 1, ["dribbling"] = 0.7 },
            new Dictionary<string, double> { ["workRate"] = 0.6, ["aggression"] = 0.4, ["positioning"] = 0.5 },
            new Dictionary<string, double> { ["flair"] = 0.7, ["anticipation"] = 1, ["decisions"] = 0.8 })
    ];

    private static double Weighted(ScoutPlayer player, IReadOnlyDictionary<string, double> weights)
    {
        double total = 0;
        double weightSum = 0;
        foreach (var (key, weight) in weights)
        {
            var value = GetAttr(player, key);
            if (value is null) continue;
            total += value.Value * weight;
            weightSum += weight;
        }

        if (weightSum <= 0) return 0;
        // attributes are 1-20 → percentage
        return Math.Clamp(total / weightSum / 20.0 * 100.0, 0, 100);
    }

    private static double PositionFamiliarity(ScoutPlayer player, string positionKey)
    {
        int? rating = positionKey.ToUpperInvariant() switch
        {
            "GK" => player.PosGk,
            "DC" => player.PosDc,
            "DL" => player.PosDl,
            "DR" => player.PosDr,
            "DM" => player.PosDm,
            "MC" => player.PosMc,
            "ML" => player.PosMl,
            "MR" => player.PosMr,
            "AMC" => player.PosAmc,
            "AML" => player.PosAml,
            "AMR" => player.PosAmr,
            "STC" => player.PosStc,
            _ => null
        };

        if (rating is null)
        {
            if (player.NaturalPositions.Any(p => p.Contains(positionKey, StringComparison.OrdinalIgnoreCase)))
                return 1.0;
            return 0.75;
        }

        // FM position ratings: 20 natural, 15 accomplished, etc.
        return rating.Value switch
        {
            >= 20 => 1.0,
            >= 15 => 0.92,
            >= 10 => 0.8,
            >= 5 => 0.65,
            _ => 0.5
        };
    }

    private static int? GetAttr(ScoutPlayer p, string key) => key switch
    {
        "pace" => p.Pace,
        "acceleration" => p.Acceleration,
        "finishing" => p.Finishing,
        "passing" => p.Passing,
        "tackling" => p.Tackling,
        "positioning" => p.Positioning,
        "decisions" => p.Decisions,
        "stamina" => p.Stamina,
        "strength" => p.Strength,
        "jumpingReach" => p.JumpingReach,
        "heading" => p.Heading,
        "dribbling" => p.Dribbling,
        "firstTouch" => p.FirstTouch,
        "technique" => p.Technique,
        "composure" => p.Composure,
        "offTheBall" => p.OffTheBall,
        "workRate" => p.WorkRate,
        "teamwork" => p.Teamwork,
        "vision" => p.Vision,
        "aggression" => p.Aggression,
        "bravery" => p.Bravery,
        "determination" => p.Determination,
        "flair" => p.Flair,
        "anticipation" => p.Anticipation,
        "concentration" => p.Concentration,
        "agility" => p.Agility,
        "marking" => p.Marking,
        "crossing" => p.Crossing,
        _ => null
    };
}

public sealed record TacticalRole(
    string Name,
    string PositionKey,
    IReadOnlyDictionary<string, double> InPossessionWeights,
    IReadOnlyDictionary<string, double> OutOfPossessionWeights,
    IReadOnlyDictionary<string, double> StyleWeights);

public sealed record DualPhaseScore(double IpFitPct, double OopFitPct, double StylePct, string RoleName)
{
    public double FitPct => Math.Round((IpFitPct + OopFitPct) / 2.0, 1);
}
