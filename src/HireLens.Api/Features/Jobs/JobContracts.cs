using System.ComponentModel.DataAnnotations;
using HireLens.Api.Domain;

namespace HireLens.Api.Features.Jobs;

/// <summary>Body for creating (POST) or editing (PUT) a job. On create, a missing status means "open".</summary>
public sealed record JobRequest(
    [Required, StringLength(120, MinimumLength = 2)] string Title,
    [StringLength(120)] string? Location,
    [Required, StringLength(20_000, MinimumLength = 20)] string DescriptionText,
    [Required, MaxLength(SkillRules.MaxSkills)] List<string> MustHaveSkills,
    [MaxLength(SkillRules.MaxSkills)] List<string>? NiceToHaveSkills,
    JobStatus? Status) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (SkillList.Normalize(MustHaveSkills).Count == 0)
            yield return new ValidationResult("At least one must-have skill is required.", [nameof(MustHaveSkills)]);

        foreach (var error in SkillRules.Check(MustHaveSkills, nameof(MustHaveSkills)))
            yield return error;
        foreach (var error in SkillRules.Check(NiceToHaveSkills, nameof(NiceToHaveSkills)))
            yield return error;
    }
}

public sealed record SuggestSkillsRequest([Required, StringLength(20_000, MinimumLength = 20)] string DescriptionText);

public sealed record SuggestSkillsResponse(List<string> Skills);

public sealed record JobResponse(
    string Id,
    string Title,
    string Location,
    string DescriptionText,
    List<string> MustHaveSkills,
    List<string> NiceToHaveSkills,
    JobStatus Status,
    DateTime CreatedAt)
{
    public static JobResponse From(Job j) => new(
        j.Id!, j.Title, j.Location, j.DescriptionText, j.MustHaveSkills, j.NiceToHaveSkills, j.Status, j.CreatedAt);
}

public static class SkillRules
{
    public const int MaxSkills = 100;
    public const int MaxSkillLength = 50;

    public static IEnumerable<ValidationResult> Check(IEnumerable<string?>? skills, string member)
    {
        if (skills is null) yield break;
        if (skills.Any(s => s is { Length: > MaxSkillLength }))
            yield return new ValidationResult($"Each skill must be at most {MaxSkillLength} characters.", [member]);
    }
}
