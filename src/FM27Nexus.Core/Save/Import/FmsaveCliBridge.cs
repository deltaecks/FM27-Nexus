using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace FM27Nexus.Core.Save.Import;

/// <summary>
/// Exports player tables from a .fm save via the MIT fmsave Python package when available.
/// Native FMF container validation always runs separately; this bridge fills scout tables
/// without inventing binary table layouts in C#.
/// </summary>
public sealed class FmsaveCliBridge
{
    public bool TryResolvePython(out string pythonPath, out string details)
    {
        var candidates = new[]
        {
            "python",
            "py",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Python\pythoncore-3.15-64\python.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Programs\Python\Python312\python.exe")
        };

        foreach (var candidate in candidates)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = candidate == "py" ? "py" : candidate,
                    Arguments = candidate == "py" ? "-3 -c \"import fmsave; print('ok')\"" : "-c \"import fmsave; print('ok')\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                if (proc is null) continue;
                var output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(15000);
                if (proc.ExitCode == 0 && output.Contains("ok", StringComparison.OrdinalIgnoreCase))
                {
                    pythonPath = candidate == "py" ? "py" : candidate;
                    details = "fmsave importable";
                    return true;
                }
            }
            catch
            {
                // try next
            }
        }

        pythonPath = string.Empty;
        details = "Python + fmsave not found. Install with: pip install fmsave";
        return false;
    }

    public async Task<IReadOnlyList<ScoutPlayer>> ExportPlayersAsync(string fmPath, string outputJsonPath, CancellationToken cancellationToken = default)
    {
        if (!TryResolvePython(out var python, out var details))
            throw new InvalidOperationException(details);

        var script = """
import fmsave, json, sys, warnings
warnings.filterwarnings('ignore')
src, dst = sys.argv[1], sys.argv[2]
with fmsave.open(src) as s:
    rows = []
    for pl in s.players():
        at = pl.attributes
        pers = pl.personality
        pos = pl.positions
        rows.append({
            'uid': pl.uid, 'uniqueId': pl.unique_id, 'name': pl.name,
            'firstName': pl.first_name, 'lastName': pl.last_name, 'age': pl.age,
            'heightCm': pl.height_cm, 'clubUid': pl.club_uid, 'clubName': pl.club_name,
            'ca': pl.ability.current if pl.ability else None,
            'pa': pl.ability.potential if pl.ability else None,
            'nationId': pl.nation_id, 'leftFoot': pl.left_foot, 'rightFoot': pl.right_foot,
            'naturalPositions': list(pl.natural_positions or []),
            'consistency': at.consistency, 'importantMatches': at.important_matches,
            'injuryProneness': at.injury_proneness, 'dirtiness': at.dirtiness,
            'pace': at.pace, 'acceleration': at.acceleration, 'finishing': at.finishing,
            'passing': at.passing, 'tackling': at.tackling, 'positioning': at.positioning,
            'decisions': at.decisions, 'stamina': at.stamina, 'strength': at.strength,
            'jumpingReach': at.jumping_reach, 'heading': at.heading, 'dribbling': at.dribbling,
            'firstTouch': at.first_touch, 'technique': at.technique, 'composure': at.composure,
            'offTheBall': at.off_the_ball, 'workRate': at.work_rate, 'teamwork': at.teamwork,
            'vision': at.vision, 'aggression': at.aggression, 'bravery': at.bravery,
            'determination': at.determination, 'flair': at.flair, 'leadership': at.leadership,
            'anticipation': at.anticipation, 'concentration': at.concentration,
            'agility': at.agility, 'balance': at.balance, 'naturalFitness': at.natural_fitness,
            'marking': at.marking, 'crossing': at.crossing, 'longShots': at.long_shots,
            'posGk': pos.gk, 'posDc': pos.dc, 'posDl': pos.dl, 'posDr': pos.dr,
            'posDm': pos.dm, 'posMc': pos.mc, 'posMl': pos.ml, 'posMr': pos.mr,
            'posAmc': pos.amc, 'posAml': pos.aml, 'posAmr': pos.amr, 'posStc': pos.stc,
            'ambition': pers.ambition if pers else None,
            'professionalism': pers.professionalism if pers else None,
            'fieldStatus': 'Verified'
        })
    with open(dst, 'w', encoding='utf-8') as f:
        json.dump({'players': rows}, f)
print(len(rows))
""";

        var scriptPath = Path.Combine(Path.GetTempPath(), "fm27nexus_fmsave_export.py");
        await File.WriteAllTextAsync(scriptPath, script, cancellationToken).ConfigureAwait(false);

        var args = python == "py"
            ? $"-3 \"{scriptPath}\" \"{fmPath}\" \"{outputJsonPath}\""
            : $"\"{scriptPath}\" \"{fmPath}\" \"{outputJsonPath}\"";

        var psi = new ProcessStartInfo
        {
            FileName = python,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start Python.");
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        proc.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        proc.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        await proc.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        if (proc.ExitCode != 0)
            throw new InvalidOperationException("fmsave export failed: " + stderr);

        if (!File.Exists(outputJsonPath))
            throw new InvalidOperationException("fmsave export produced no output file.");

        return new ScoutTableJsonLoader().Load(outputJsonPath);
    }
}
