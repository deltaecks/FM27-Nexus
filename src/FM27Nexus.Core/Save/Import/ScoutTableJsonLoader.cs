using System.Text.Json;

namespace FM27Nexus.Core.Save.Import;

public sealed class ScoutTableJsonLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public IReadOnlyList<ScoutPlayer> Load(string path)
    {
        var json = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(json);
        JsonElement playersEl;
        if (doc.RootElement.ValueKind == JsonValueKind.Array)
            playersEl = doc.RootElement;
        else if (doc.RootElement.TryGetProperty("players", out var p))
            playersEl = p;
        else
            throw new InvalidDataException("JSON must be an array or an object with a 'players' array.");

        var list = new List<ScoutPlayer>();
        foreach (var el in playersEl.EnumerateArray())
        {
            var dto = JsonSerializer.Deserialize<ScoutPlayerDto>(el.GetRawText(), Options);
            if (dto is null) continue;
            list.Add(dto.ToPlayer("json"));
        }

        return list;
    }

    private sealed class ScoutPlayerDto
    {
        public long? Uid { get; set; }
        public long? UniqueId { get; set; }
        public string? Name { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public int? Age { get; set; }
        public int? HeightCm { get; set; }
        public long? ClubUid { get; set; }
        public string? ClubName { get; set; }
        public int? Ca { get; set; }
        public int? Pa { get; set; }
        public int? NationId { get; set; }
        public int? LeftFoot { get; set; }
        public int? RightFoot { get; set; }
        public List<string>? NaturalPositions { get; set; }
        public int? Consistency { get; set; }
        public int? ImportantMatches { get; set; }
        public int? InjuryProneness { get; set; }
        public int? Dirtiness { get; set; }
        public int? Pace { get; set; }
        public int? Acceleration { get; set; }
        public int? Finishing { get; set; }
        public int? Passing { get; set; }
        public int? Tackling { get; set; }
        public int? Positioning { get; set; }
        public int? Decisions { get; set; }
        public int? Stamina { get; set; }
        public int? Strength { get; set; }
        public int? JumpingReach { get; set; }
        public int? Heading { get; set; }
        public int? Dribbling { get; set; }
        public int? FirstTouch { get; set; }
        public int? Technique { get; set; }
        public int? Composure { get; set; }
        public int? OffTheBall { get; set; }
        public int? WorkRate { get; set; }
        public int? Teamwork { get; set; }
        public int? Vision { get; set; }
        public int? Aggression { get; set; }
        public int? Bravery { get; set; }
        public int? Determination { get; set; }
        public int? Flair { get; set; }
        public int? Leadership { get; set; }
        public int? Anticipation { get; set; }
        public int? Concentration { get; set; }
        public int? Agility { get; set; }
        public int? Balance { get; set; }
        public int? NaturalFitness { get; set; }
        public int? Marking { get; set; }
        public int? Crossing { get; set; }
        public int? LongShots { get; set; }
        public int? PosGk { get; set; }
        public int? PosDc { get; set; }
        public int? PosDl { get; set; }
        public int? PosDr { get; set; }
        public int? PosDm { get; set; }
        public int? PosMc { get; set; }
        public int? PosMl { get; set; }
        public int? PosMr { get; set; }
        public int? PosAmc { get; set; }
        public int? PosAml { get; set; }
        public int? PosAmr { get; set; }
        public int? PosStc { get; set; }
        public int? Ambition { get; set; }
        public int? Professionalism { get; set; }
        public int? Minutes { get; set; }
        public int? Goals { get; set; }
        public int? Assists { get; set; }
        public int? Shots { get; set; }
        public int? KeyPasses { get; set; }
        public string? FieldStatus { get; set; }

        public ScoutPlayer ToPlayer(string source) => new()
        {
            Uid = Uid,
            UniqueId = UniqueId,
            Name = Name,
            FirstName = FirstName,
            LastName = LastName,
            Age = Age,
            HeightCm = HeightCm,
            ClubUid = ClubUid,
            ClubName = ClubName,
            Ca = Ca,
            Pa = Pa,
            NationId = NationId,
            LeftFoot = LeftFoot,
            RightFoot = RightFoot,
            NaturalPositions = NaturalPositions ?? new List<string>(),
            Consistency = Consistency,
            ImportantMatches = ImportantMatches,
            InjuryProneness = InjuryProneness,
            Dirtiness = Dirtiness,
            Pace = Pace,
            Acceleration = Acceleration,
            Finishing = Finishing,
            Passing = Passing,
            Tackling = Tackling,
            Positioning = Positioning,
            Decisions = Decisions,
            Stamina = Stamina,
            Strength = Strength,
            JumpingReach = JumpingReach,
            Heading = Heading,
            Dribbling = Dribbling,
            FirstTouch = FirstTouch,
            Technique = Technique,
            Composure = Composure,
            OffTheBall = OffTheBall,
            WorkRate = WorkRate,
            Teamwork = Teamwork,
            Vision = Vision,
            Aggression = Aggression,
            Bravery = Bravery,
            Determination = Determination,
            Flair = Flair,
            Leadership = Leadership,
            Anticipation = Anticipation,
            Concentration = Concentration,
            Agility = Agility,
            Balance = Balance,
            NaturalFitness = NaturalFitness,
            Marking = Marking,
            Crossing = Crossing,
            LongShots = LongShots,
            PosGk = PosGk,
            PosDc = PosDc,
            PosDl = PosDl,
            PosDr = PosDr,
            PosDm = PosDm,
            PosMc = PosMc,
            PosMl = PosMl,
            PosMr = PosMr,
            PosAmc = PosAmc,
            PosAml = PosAml,
            PosAmr = PosAmr,
            PosStc = PosStc,
            Ambition = Ambition,
            Professionalism = Professionalism,
            Minutes = Minutes,
            Goals = Goals,
            Assists = Assists,
            Shots = Shots,
            KeyPasses = KeyPasses,
            FieldStatus = string.Equals(FieldStatus, "Verified", StringComparison.OrdinalIgnoreCase)
                ? Save.FieldStatus.Verified
                : Save.FieldStatus.Unverified,
            Source = source
        };
    }
}
