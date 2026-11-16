namespace FM27Nexus.Core.Save;

/// <summary>
/// Unified player record for scouting grids. Unreadable fields stay null with Unverified status.
/// </summary>
public sealed class ScoutPlayer
{
    public long? Uid { get; init; }
    public long? UniqueId { get; init; }
    public string? Name { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public int? Age { get; init; }
    public int? HeightCm { get; init; }
    public long? ClubUid { get; init; }
    public string? ClubName { get; init; }
    public int? Ca { get; init; }
    public int? Pa { get; init; }
    public int? NationId { get; init; }
    public string? NationName { get; init; }
    public int? LeftFoot { get; init; }
    public int? RightFoot { get; init; }
    public IReadOnlyList<string> NaturalPositions { get; init; } = Array.Empty<string>();

    public int? Consistency { get; init; }
    public int? ImportantMatches { get; init; }
    public int? InjuryProneness { get; init; }
    public int? Dirtiness { get; init; }

    public int? Pace { get; init; }
    public int? Acceleration { get; init; }
    public int? Finishing { get; init; }
    public int? Passing { get; init; }
    public int? Tackling { get; init; }
    public int? Positioning { get; init; }
    public int? Decisions { get; init; }
    public int? Stamina { get; init; }
    public int? Strength { get; init; }
    public int? JumpingReach { get; init; }
    public int? Heading { get; init; }
    public int? Dribbling { get; init; }
    public int? FirstTouch { get; init; }
    public int? Technique { get; init; }
    public int? Composure { get; init; }
    public int? OffTheBall { get; init; }
    public int? WorkRate { get; init; }
    public int? Teamwork { get; init; }
    public int? Vision { get; init; }
    public int? Aggression { get; init; }
    public int? Bravery { get; init; }
    public int? Determination { get; init; }
    public int? Flair { get; init; }
    public int? Leadership { get; init; }
    public int? Anticipation { get; init; }
    public int? Concentration { get; init; }
    public int? Agility { get; init; }
    public int? Balance { get; init; }
    public int? NaturalFitness { get; init; }
    public int? Marking { get; init; }
    public int? Crossing { get; init; }
    public int? LongShots { get; init; }

    public int? PosGk { get; init; }
    public int? PosDc { get; init; }
    public int? PosDl { get; init; }
    public int? PosDr { get; init; }
    public int? PosDm { get; init; }
    public int? PosMc { get; init; }
    public int? PosMl { get; init; }
    public int? PosMr { get; init; }
    public int? PosAmc { get; init; }
    public int? PosAml { get; init; }
    public int? PosAmr { get; init; }
    public int? PosStc { get; init; }

    public int? Ambition { get; init; }
    public int? Professionalism { get; init; }

    public int? Minutes { get; set; }
    public int? Goals { get; set; }
    public int? Assists { get; set; }
    public int? Shots { get; set; }
    public int? KeyPasses { get; set; }

    public double? GoalsPer90 { get; set; }
    public double? AssistsPer90 { get; set; }
    public double? ShotsPer90 { get; set; }
    public double? ConversionPct { get; set; }
    public double? RcaBest { get; set; }
    public string? RcaBestRole { get; set; }
    public double? IpFitPct { get; set; }
    public double? OopFitPct { get; set; }
    public double? StylePct { get; set; }

    public FieldStatus FieldStatus { get; init; } = FieldStatus.Unverified;
    public string? Source { get; init; }
}
