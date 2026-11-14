using System.Net;
using System.Text;
using HireLens.Api.Ner;
using Microsoft.Extensions.Logging.Abstractions;

namespace HireLens.Tests.Ner;

/// <summary>Exercises the real NerClient against a stubbed HTTP handler: every failure must become null.</summary>
public class NerClientTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastRequest = request;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return await respond(request, ct);
        }
    }

    private static (NerClient Client, StubHandler Handler) Create(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond, TimeSpan? timeout = null)
    {
        var handler = new StubHandler(respond);
        var http = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://ner.test/"),
            Timeout = timeout ?? TimeSpan.FromSeconds(5),
        };
        return (new NerClient(http, NullLogger<NerClient>.Instance), handler);
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task Posts_text_to_extract_and_parses_the_response()
    {
        var (client, handler) = Create((_, _) => Task.FromResult(Json(
            """{"skills":["C#","MongoDB"],"orgs":["Infosys"],"locations":["Pune"],"dates":["2021"]}""")));

        var result = await client.ExtractAsync("resume text", default);

        Assert.NotNull(result);
        Assert.Equal(["C#", "MongoDB"], result.Skills);
        Assert.Equal(["Infosys"], result.Orgs);
        Assert.Equal(["Pune"], result.Locations);
        Assert.Equal(["2021"], result.Dates);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal("http://ner.test/extract", handler.LastRequest.RequestUri!.ToString());
        Assert.Equal("""{"text":"resume text"}""", handler.LastBody);
    }

    [Fact]
    public async Task Missing_entity_lists_default_to_empty()
    {
        var (client, _) = Create((_, _) => Task.FromResult(Json("""{"skills":["C#"]}""")));

        var result = await client.ExtractAsync("text", default);

        Assert.NotNull(result);
        Assert.Empty(result.Orgs);
        Assert.Empty(result.Locations);
        Assert.Empty(result.Dates);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    public async Task Error_status_returns_null(HttpStatusCode status)
    {
        var (client, _) = Create((_, _) => Task.FromResult(Json("{}", status)));

        Assert.Null(await client.ExtractAsync("text", default));
    }

    [Fact]
    public async Task Connection_failure_returns_null()
    {
        var (client, _) = Create((_, _) => throw new HttpRequestException("Connection refused"));

        Assert.Null(await client.ExtractAsync("text", default));
    }

    [Fact]
    public async Task Timeout_returns_null()
    {
        var (client, _) = Create(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(10), ct);
            return Json("""{"skills":[]}""");
        }, timeout: TimeSpan.FromMilliseconds(100));

        Assert.Null(await client.ExtractAsync("text", default));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"unexpected":true}""")]
    [InlineData("null")]
    public async Task Malformed_body_returns_null(string body)
    {
        var (client, _) = Create((_, _) => Task.FromResult(Json(body)));

        Assert.Null(await client.ExtractAsync("text", default));
    }

    [Fact]
    public async Task Caller_cancellation_is_not_swallowed()
    {
        var (client, _) = Create(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(10), ct);
            return Json("""{"skills":[]}""");
        });
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.ExtractAsync("text", cts.Token));
    }
}
