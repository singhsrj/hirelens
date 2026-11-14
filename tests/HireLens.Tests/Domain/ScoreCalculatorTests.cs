using HireLens.Api.Domain;

namespace HireLens.Tests.Domain;

public class ScoreCalculatorTests
{
    [Fact]
    public void All_must_haves_matched_scores_one()
    {
        var result = ScoreCalculator.Calculate(["C#", "MongoDB"], ["MongoDB", "C#", "Python"]);

        Assert.Equal(1.0, result.Score);
        Assert.Equal(["C#", "MongoDB"], result.Matched);
        Assert.Empty(result.Missing);
    }

    [Fact]
    public void No_must_haves_matched_scores_zero()
    {
        var result = ScoreCalculator.Calculate(["C#", "MongoDB"], ["Java"]);

        Assert.Equal(0.0, result.Score);
        Assert.Empty(result.Matched);
        Assert.Equal(["C#", "MongoDB"], result.Missing);
    }

    [Fact]
    public void Partial_match_is_ratio_rounded_to_four_places()
    {
        var result = ScoreCalculator.Calculate(["C#", "MongoDB", "JavaScript"], ["C#"]);

        Assert.Equal(0.3333, result.Score);
        Assert.Equal(["C#"], result.Matched);
        Assert.Equal(["MongoDB", "JavaScript"], result.Missing);
    }

    [Fact]
    public void Matching_ignores_case_and_whitespace_and_reports_the_jobs_spelling()
    {
        var result = ScoreCalculator.Calculate(["JavaScript", "ASP.NET Core"], ["  javascript ", "asp.net   core"]);

        Assert.Equal(1.0, result.Score);
        Assert.Equal(["JavaScript", "ASP.NET Core"], result.Matched);
    }

    [Fact]
    public void Duplicate_must_haves_count_once()
    {
        var result = ScoreCalculator.Calculate(["C#", "c#", "MongoDB"], ["C#"]);

        Assert.Equal(0.5, result.Score);
    }

    [Fact]
    public void Nice_to_have_skills_do_not_affect_score()
    {
        // Only must-haves are passed in; extra candidate skills never raise the score above 1.
        var result = ScoreCalculator.Calculate(["C#"], ["C#", "Python", "Docker"]);

        Assert.Equal(1.0, result.Score);
    }

    [Fact]
    public void No_must_have_skills_scores_zero_without_dividing_by_zero()
    {
        var result = ScoreCalculator.Calculate([], ["C#"]);

        Assert.Equal(0.0, result.Score);
        Assert.Empty(result.Matched);
        Assert.Empty(result.Missing);
    }

    [Fact]
    public void Candidate_with_no_skills_misses_everything()
    {
        var result = ScoreCalculator.Calculate(["C#"], []);

        Assert.Equal(0.0, result.Score);
        Assert.Equal(["C#"], result.Missing);
    }
}
