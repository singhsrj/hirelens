using HireLens.Api.Ner;

namespace HireLens.Tests.Fakes;

/// <summary>
/// Stand-in for the NER service. By default it "extracts" the comma-separated list after
/// "Skills:" in the text. Set <see cref="IsDown"/> to simulate the service being unavailable.
/// </summary>
public sealed class FakeNerClient : INerClient
{
    public bool IsDown { get; set; }
    public int Calls { get; private set; }

    public Task<NerResult?> ExtractAsync(string text, CancellationToken ct)
    {
        Calls++;
        if (IsDown) return Task.FromResult<NerResult?>(null);

        var marker = text.IndexOf("Skills:", StringComparison.Ordinal);
        var skills = marker < 0
            ? []
            : text[(marker + "Skills:".Length)..].TrimEnd('.').Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();

        return Task.FromResult<NerResult?>(new NerResult(skills, ["Acme Corp"], ["Pune"], ["2021"]));
    }
}
