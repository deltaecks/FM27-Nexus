using System.Globalization;
using System.Text;
using System.Text.Json;
using CsvHelper;
using CsvHelper.Configuration;

namespace FM27Nexus.Core.Save;

public sealed class ScoutExportService
{
    public void ExportCsv(IEnumerable<ScoutPlayer> players, string path)
    {
        using var writer = new StreamWriter(path, false, Encoding.UTF8);
        using var csv = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture));
        csv.WriteRecords(players.Select(p => new
        {
            p.Name, p.Age, p.ClubName, p.Ca, p.Pa, p.RcaBest, p.RcaBestRole,
            p.GoalsPer90, p.AssistsPer90, p.ConversionPct, p.IpFitPct, p.OopFitPct, p.StylePct,
            p.Consistency, p.ImportantMatches, p.InjuryProneness, p.Dirtiness, p.FieldStatus, p.Source
        }));
    }

    public void ExportJson(IEnumerable<ScoutPlayer> players, string path)
    {
        var json = JsonSerializer.Serialize(new { players }, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json, Encoding.UTF8);
    }

    public void ExportExcelCompatibleCsv(IEnumerable<ScoutPlayer> players, string path)
    {
        // Excel-friendly UTF-8 BOM CSV
        using var writer = new StreamWriter(path, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        using var csv = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture));
        csv.WriteRecords(players);
    }
}
