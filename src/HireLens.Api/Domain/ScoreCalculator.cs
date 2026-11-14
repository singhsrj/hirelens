namespace HireLens.Api.Domain;

public sealed record MatchResult(double Score, IReadOnlyList<string> Matched, IReadOnlyList<string> Missing);

public static class ScoreCalculator
{
    /// <summary>
    /// Score = must-have skills the candidate has ÷ total must-have skills, rounded to 4 places.
    /// Matching is case-insensitive; matched/missing use the job's spelling so the recruiter sees their own terms.
    /// A job with no must-have skills scores everyone 0 (job validation requires at least one).
    /// </summary>
    public static MatchResult Calculate(IEnumerable<string> mustHaveSkills, IEnumerable<string> candidateSkills)
    {
        var required = SkillList.Normalize(mustHaveSkills);
        if (required.Count == 0)
            return new MatchResult(0, [], []);

        var has = new HashSet<string>(SkillList.Normalize(candidateSkills), StringComparer.OrdinalIgnoreCase);

        var matched = new List<string>();
        var missing = new List<string>();
        foreach (var skill in required)
            (has.Contains(skill) ? matched : missing).Add(skill);

        var score = Math.Round((double)matched.Count / required.Count, 4);
        return new MatchResult(score, matched, missing);
    }
}
