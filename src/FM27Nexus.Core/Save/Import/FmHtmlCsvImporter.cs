using System.Globalization;
using System.Text.RegularExpressions;
using CsvHelper;
using CsvHelper.Configuration;

namespace FM27Nexus.Core.Save.Import;

public sealed class FmHtmlCsvImporter
{
    public IReadOnlyList<ScoutPlayer> Import(string path)
    {
        var ext = Path.GetExtension(path);
        if (ext.Equals(".csv", StringComparison.OrdinalIgnoreCase))
            return ImportCsv(path);
        if (ext.Equals(".html", StringComparison.OrdinalIgnoreCase) || ext.Equals(".htm", StringComparison.OrdinalIgnoreCase))
            return ImportHtml(path);
        throw new InvalidDataException("Supported imports: .csv, .html, .htm");
    }

    public IReadOnlyList<ScoutPlayer> ImportCsv(string path)
    {
        using var reader = new StreamReader(path);
        using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            MissingFieldFound = null,
            HeaderValidated = null,
            BadDataFound = null
        });

        csv.Read();
        csv.ReadHeader();
        var headers = csv.HeaderRecord?.Select(NormalizeHeader).ToArray()
                      ?? throw new InvalidDataException("CSV has no header row.");

        var players = new List<ScoutPlayer>();
        while (csv.Read())
        {
            string? Get(params string[] keys)
            {
                foreach (var key in keys)
                {
                    var idx = Array.FindIndex(headers, h => h == NormalizeHeader(key));
                    if (idx >= 0)
                    {
                        var v = csv.GetField(idx);
                        if (!string.IsNullOrWhiteSpace(v))
                            return v.Trim();
                    }
                }

                return null;
            }

            int? GetInt(params string[] keys)
            {
                var text = Get(keys);
                return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;
            }

            var name = Get("Name", "Player", "Inf");
            if (string.IsNullOrWhiteSpace(name))
                continue;

            var positions = Get("Position", "Pos", "Preferred Positions");
            players.Add(new ScoutPlayer
            {
                Name = name,
                Age = GetInt("Age"),
                ClubName = Get("Club", "Team"),
                Ca = GetInt("CA", "Curr. Abl.", "Current Ability"),
                Pa = GetInt("PA", "Pot. Abl.", "Potential Ability"),
                Pace = GetInt("Pace", "Pac"),
                Acceleration = GetInt("Acceleration", "Acc"),
                Finishing = GetInt("Finishing", "Fin"),
                Passing = GetInt("Passing", "Pas"),
                Tackling = GetInt("Tackling", "Tck"),
                Positioning = GetInt("Positioning", "Pos"),
                Decisions = GetInt("Decisions", "Dec"),
                Stamina = GetInt("Stamina", "Sta"),
                Strength = GetInt("Strength", "Str"),
                Dribbling = GetInt("Dribbling", "Dri"),
                FirstTouch = GetInt("First Touch", "Fir"),
                Technique = GetInt("Technique", "Tec"),
                Composure = GetInt("Composure", "Cmp"),
                OffTheBall = GetInt("Off the Ball", "OtB"),
                WorkRate = GetInt("Work Rate", "Wor"),
                Teamwork = GetInt("Teamwork", "Tea"),
                Vision = GetInt("Vision", "Vis"),
                Consistency = GetInt("Consistency", "Cons"),
                ImportantMatches = GetInt("Important Matches", "Imp M"),
                InjuryProneness = GetInt("Injury Proneness", "Inj Pr"),
                Dirtiness = GetInt("Dirtiness", "Dirt"),
                Minutes = GetInt("Mins", "Minutes", "Apps"),
                Goals = GetInt("Gls", "Goals"),
                Assists = GetInt("Ast", "Assists"),
                Shots = GetInt("Shots", "Sh"),
                KeyPasses = GetInt("Key Passes", "KP"),
                NaturalPositions = string.IsNullOrWhiteSpace(positions)
                    ? Array.Empty<string>()
                    : positions.Split(new[] { ',', '/', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                FieldStatus = FieldStatus.Verified,
                Source = "csv"
            });
        }

        return players;
    }

    public IReadOnlyList<ScoutPlayer> ImportHtml(string path)
    {
        var html = File.ReadAllText(path);
        // FM HTML exports are table-based; extract the first large data table into CSV-like rows.
        var tableMatch = Regex.Match(html, "<table[\\s\\S]*?</table>", RegexOptions.IgnoreCase);
        if (!tableMatch.Success)
            throw new InvalidDataException("No HTML table found in export.");

        var rows = Regex.Matches(tableMatch.Value, "<tr[\\s\\S]*?</tr>", RegexOptions.IgnoreCase);
        if (rows.Count < 2)
            throw new InvalidDataException("HTML table has no data rows.");

        static string CellText(string rowHtml)
        {
            var cells = Regex.Matches(rowHtml, "<t[hd][^>]*>([\\s\\S]*?)</t[hd]>", RegexOptions.IgnoreCase);
            return string.Join(",", cells.Select(c =>
            {
                var text = Regex.Replace(c.Groups[1].Value, "<.*?>", string.Empty);
                text = System.Net.WebUtility.HtmlDecode(text).Trim().Replace("\"", "\"\"");
                return $"\"{text}\"";
            }));
        }

        var tmp = Path.GetTempFileName();
        try
        {
            using (var writer = new StreamWriter(tmp))
            {
                foreach (Match row in rows)
                    writer.WriteLine(CellText(row.Value));
            }

            return ImportCsv(tmp).Select(p => new ScoutPlayer
            {
                Uid = p.Uid,
                UniqueId = p.UniqueId,
                Name = p.Name,
                FirstName = p.FirstName,
                LastName = p.LastName,
                Age = p.Age,
                HeightCm = p.HeightCm,
                ClubUid = p.ClubUid,
                ClubName = p.ClubName,
                Ca = p.Ca,
                Pa = p.Pa,
                NationId = p.NationId,
                NationName = p.NationName,
                LeftFoot = p.LeftFoot,
                RightFoot = p.RightFoot,
                NaturalPositions = p.NaturalPositions,
                Consistency = p.Consistency,
                ImportantMatches = p.ImportantMatches,
                InjuryProneness = p.InjuryProneness,
                Dirtiness = p.Dirtiness,
                Pace = p.Pace,
                Acceleration = p.Acceleration,
                Finishing = p.Finishing,
                Passing = p.Passing,
                Tackling = p.Tackling,
                Positioning = p.Positioning,
                Decisions = p.Decisions,
                Stamina = p.Stamina,
                Strength = p.Strength,
                JumpingReach = p.JumpingReach,
                Heading = p.Heading,
                Dribbling = p.Dribbling,
                FirstTouch = p.FirstTouch,
                Technique = p.Technique,
                Composure = p.Composure,
                OffTheBall = p.OffTheBall,
                WorkRate = p.WorkRate,
                Teamwork = p.Teamwork,
                Vision = p.Vision,
                Aggression = p.Aggression,
                Bravery = p.Bravery,
                Determination = p.Determination,
                Flair = p.Flair,
                Leadership = p.Leadership,
                Anticipation = p.Anticipation,
                Concentration = p.Concentration,
                Agility = p.Agility,
                Balance = p.Balance,
                NaturalFitness = p.NaturalFitness,
                Marking = p.Marking,
                Crossing = p.Crossing,
                LongShots = p.LongShots,
                PosGk = p.PosGk,
                PosDc = p.PosDc,
                PosDl = p.PosDl,
                PosDr = p.PosDr,
                PosDm = p.PosDm,
                PosMc = p.PosMc,
                PosMl = p.PosMl,
                PosMr = p.PosMr,
                PosAmc = p.PosAmc,
                PosAml = p.PosAml,
                PosAmr = p.PosAmr,
                PosStc = p.PosStc,
                Ambition = p.Ambition,
                Professionalism = p.Professionalism,
                Minutes = p.Minutes,
                Goals = p.Goals,
                Assists = p.Assists,
                Shots = p.Shots,
                KeyPasses = p.KeyPasses,
                FieldStatus = FieldStatus.Verified,
                Source = "html"
            }).ToList();
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    private static string NormalizeHeader(string header) =>
        Regex.Replace(header.Trim().ToLowerInvariant(), "\\s+", " ");
}
