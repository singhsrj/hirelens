using HireLens.Api.Domain;

namespace HireLens.Tests.Domain;

public class SkillListTests
{
    [Fact]
    public void Trims_collapses_whitespace_and_drops_blanks()
    {
        var result = SkillList.Normalize(["  C# ", "", "   ", null, "ASP.NET    Core"]);

        Assert.Equal(["C#", "ASP.NET Core"], result);
    }

    [Fact]
    public void Removes_case_insensitive_duplicates_keeping_first_spelling_and_order()
    {
        var result = SkillList.Normalize(["MongoDB", "C#", "mongodb", "c#", "Python"]);

        Assert.Equal(["MongoDB", "C#", "Python"], result);
    }

    [Fact]
    public void Null_input_gives_empty_list()
    {
        Assert.Empty(SkillList.Normalize(null));
    }
}
