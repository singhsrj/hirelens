using System.Text.RegularExpressions;

namespace HireLens.Api.Domain;

public static partial class SkillList
{
    /// <summary>
    /// Trims each skill, collapses inner whitespace, drops blanks and removes case-insensitive
    /// duplicates (the first spelling wins). Order is preserved.
    /// </summary>
    public static List<string> Normalize(IEnumerable<string?>? skills)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        if (skills is null) return result;

        foreach (var raw in skills)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var skill = Whitespace().Replace(raw.Trim(), " ");
            if (seen.Add(skill)) result.Add(skill);
        }
        return result;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
