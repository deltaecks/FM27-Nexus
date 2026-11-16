namespace FM27Nexus.Core.Club;

/// <summary>
/// Resolves the user's club from a career save file name and scout table.
/// FM default save names look like "Manager Name - Club Name.fm".
/// </summary>
public static class ClubIdentity
{
    public static string? TryParseClubFromSavePath(string? savePath)
    {
        if (string.IsNullOrWhiteSpace(savePath))
            return null;
        var name = Path.GetFileNameWithoutExtension(savePath);
        var idx = name.LastIndexOf(" - ", StringComparison.Ordinal);
        if (idx < 0 || idx + 3 >= name.Length)
            return null;
        var club = name[(idx + 3)..].Trim();
        return string.IsNullOrWhiteSpace(club) ? null : club;
    }

    public static string? ResolvePreferredClub(
        string? savePath,
        IEnumerable<string?> clubNamesInTable,
        string? settingsOverride)
    {
        if (!string.IsNullOrWhiteSpace(settingsOverride))
            return settingsOverride.Trim();

        var fromSave = TryParseClubFromSavePath(savePath);
        if (!string.IsNullOrWhiteSpace(fromSave))
        {
            var match = clubNamesInTable
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .FirstOrDefault(c => string.Equals(c, fromSave, StringComparison.OrdinalIgnoreCase)
                                     || c!.Contains(fromSave, StringComparison.OrdinalIgnoreCase)
                                     || fromSave.Contains(c, StringComparison.OrdinalIgnoreCase));
            return match ?? fromSave;
        }

        return clubNamesInTable
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .GroupBy(c => c!, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .FirstOrDefault();
    }

    public static IReadOnlyList<string> DistinctClubs(IEnumerable<string?> clubNames) =>
        clubNames
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
            .Select(c => c!)
            .ToList();
}
