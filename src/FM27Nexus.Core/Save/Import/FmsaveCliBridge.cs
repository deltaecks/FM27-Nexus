using System.Diagnostics;
using System.Text;

namespace FM27Nexus.Core.Save.Import;

/// <summary>
/// Exports player tables from a .fm save via the MIT fmsave Python package when available.
/// Copies locked in-use saves to a temp file before reading.
/// </summary>
public sealed class FmsaveCliBridge
{
    public bool TryResolvePython(out string pythonPath, out string details)
    {
        foreach (var candidate in EnumeratePythonCandidates())
        {
            if (TryProbeFmsave(candidate, out pythonPath, out details))
                return true;
        }

        pythonPath = string.Empty;
        details = "Python + fmsave not found. Install with: pip install fmsave (then restart FM27 Nexus).";
        return false;
    }

    public async Task<IReadOnlyList<ScoutPlayer>> ExportPlayersAsync(
        string fmPath,
        string outputJsonPath,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!TryResolvePython(out var python, out var details))
            throw new InvalidOperationException(details);

        progress?.Report("Preparing save copy (works even if FM27 has the file open)…");
        var readablePath = await CreateReadableCopyAsync(fmPath, cancellationToken).ConfigureAwait(false);
        try
        {
            progress?.Report("Exporting players with fmsave (large saves can take 1–3 minutes)…");

            var script = BuildExportScript();
            var scriptPath = Path.Combine(Path.GetTempPath(), "fm27nexus_fmsave_export.py");
            await File.WriteAllTextAsync(scriptPath, script, cancellationToken).ConfigureAwait(false);

            var args = IsPyLauncher(python)
                ? $"-3 \"{scriptPath}\" \"{readablePath}\" \"{outputJsonPath}\""
                : $"\"{scriptPath}\" \"{readablePath}\" \"{outputJsonPath}\"";

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
            {
                var err = stderr.ToString().Trim();
                if (string.IsNullOrWhiteSpace(err))
                    err = stdout.ToString().Trim();
                throw new InvalidOperationException("fmsave export failed: " + err);
            }

            if (!File.Exists(outputJsonPath))
                throw new InvalidOperationException("fmsave export produced no output file.");

            progress?.Report("Loading exported player table…");
            return new ScoutTableJsonLoader().Load(outputJsonPath);
        }
        finally
        {
            try
            {
                if (!string.Equals(readablePath, Path.GetFullPath(fmPath), StringComparison.OrdinalIgnoreCase)
                    && File.Exists(readablePath))
                    File.Delete(readablePath);
            }
            catch
            {
                // temp cleanup is best-effort
            }
        }
    }

    private static async Task<string> CreateReadableCopyAsync(string fmPath, CancellationToken cancellationToken)
    {
        var full = Path.GetFullPath(fmPath);
        var temp = Path.Combine(Path.GetTempPath(), "FM27Nexus_" + Guid.NewGuid().ToString("N") + ".fm");

        try
        {
            await using var source = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            await using var dest = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None);
            await source.CopyToAsync(dest, cancellationToken).ConfigureAwait(false);
            return temp;
        }
        catch (IOException)
        {
            // Fall back to direct path if copy fails; caller may still succeed.
            if (File.Exists(temp))
            {
                try { File.Delete(temp); } catch { /* ignore */ }
            }

            return full;
        }
    }

    private static IEnumerable<string> EnumeratePythonCandidates()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<string>();
        void Push(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            if (seen.Add(path))
                list.Add(path);
        }

        Push("python");
        Push("py");

        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        Push(Path.Combine(local, @"Python\pythoncore-3.15-64\python.exe"));
        Push(Path.Combine(local, @"Python\pythoncore-3.14-64\python.exe"));
        Push(Path.Combine(local, @"Python\pythoncore-3.13-64\python.exe"));
        Push(Path.Combine(local, @"Programs\Python\Python313\python.exe"));
        Push(Path.Combine(local, @"Programs\Python\Python312\python.exe"));
        Push(Path.Combine(local, @"Programs\Python\Python311\python.exe"));
        Push(Path.Combine(programFiles, @"Python313\python.exe"));
        Push(Path.Combine(programFiles, @"Python312\python.exe"));

        var pythonRoot = Path.Combine(local, "Python");
        if (Directory.Exists(pythonRoot))
        {
            foreach (var exe in Directory.EnumerateFiles(pythonRoot, "python.exe", SearchOption.AllDirectories)
                         .Take(20))
                Push(exe);
        }

        var programsPython = Path.Combine(local, "Programs", "Python");
        if (Directory.Exists(programsPython))
        {
            foreach (var exe in Directory.EnumerateFiles(programsPython, "python.exe", SearchOption.AllDirectories)
                         .Take(20))
                Push(exe);
        }

        return list;
    }

    private static bool TryProbeFmsave(string candidate, out string pythonPath, out string details)
    {
        pythonPath = string.Empty;
        details = string.Empty;
        try
        {
            var fileName = IsPyLauncher(candidate) ? "py" : candidate;
            if (!IsPyLauncher(candidate) && !File.Exists(candidate) && candidate.Contains('\\'))
                return false;

            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = IsPyLauncher(candidate)
                    ? "-3 -c \"import fmsave; print('ok')\""
                    : "-c \"import fmsave; print('ok')\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc is null) return false;
            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(20000);
            if (proc.ExitCode == 0 && output.Contains("ok", StringComparison.OrdinalIgnoreCase))
            {
                pythonPath = fileName;
                details = "fmsave importable via " + fileName;
                return true;
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    private static bool IsPyLauncher(string candidate) =>
        candidate.Equals("py", StringComparison.OrdinalIgnoreCase);

    private static string BuildExportScript() => """
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
}
