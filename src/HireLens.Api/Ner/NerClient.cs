using System.Text.Json;

namespace HireLens.Api.Ner;

public sealed class NerOptions
{
    public const string SectionName = "Ner";

    /// <summary>127.0.0.1 rather than localhost: on Windows, localhost tries IPv6 first, doubling the wait when NER is down.</summary>
    public string BaseUrl { get; set; } = "http://127.0.0.1:8000";

    /// <summary>Total time allowed for one extraction. Kept short: a slow NER call must not hold up a candidate's application.</summary>
    public double TimeoutSeconds { get; set; } = 5;

    /// <summary>
    /// Time allowed just to open the connection. A stopped service should fail fast; on Windows a refused
    /// connection otherwise takes ~2 s per address because the OS retries the connect.
    /// </summary>
    public double ConnectTimeoutSeconds { get; set; } = 1;

    /// <summary>
    /// Shared secret sent as X-Api-Key. Needed when the NER service is publicly reachable (e.g. Render's free
    /// tier has no private networking); empty locally, where the service only listens on this machine.
    /// </summary>
    public string ApiKey { get; set; } = "";
}

/// <summary>What the Python NER service returns for a piece of text.</summary>
public sealed record NerResult(List<string> Skills, List<string> Orgs, List<string> Locations, List<string> Dates);

public interface INerClient
{
    /// <summary>
    /// Extracts skills and entities from text. Returns null when the service is down, slow or
    /// returns garbage, so callers can fall back (e.g. save the application as "pending") instead of failing.
    /// Cancellation requested by the caller still throws.
    /// </summary>
    Task<NerResult?> ExtractAsync(string text, CancellationToken ct);
}

/// <summary>Typed HttpClient for the NER service. Base address and timeout are set at registration.</summary>
public sealed class NerClient(HttpClient http, ILogger<NerClient> logger) : INerClient
{
    public async Task<NerResult?> ExtractAsync(string text, CancellationToken ct)
    {
        try
        {
            using var response = await http.PostAsJsonAsync("extract", new { text }, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("NER service returned {StatusCode}", (int)response.StatusCode);
                return null;
            }

            var result = await response.Content.ReadFromJsonAsync<NerResult>(ct);
            if (result?.Skills is null)
            {
                logger.LogWarning("NER service returned an unexpected body");
                return null;
            }
            return result with
            {
                Orgs = result.Orgs ?? [],
                Locations = result.Locations ?? [],
                Dates = result.Dates ?? [],
            };
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("NER service timed out after {Timeout}s", http.Timeout.TotalSeconds);
            return null;
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning("NER service unreachable: {Message}", ex.Message);
            return null;
        }
        catch (JsonException ex)
        {
            logger.LogWarning("NER service returned invalid JSON: {Message}", ex.Message);
            return null;
        }
    }
}

public static class NerServiceExtensions
{
    public static IServiceCollection AddNerClient(this IServiceCollection services, IConfiguration config)
    {
        var options = config.GetSection(NerOptions.SectionName).Get<NerOptions>() ?? new NerOptions();

        var baseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");

        services.AddHttpClient<INerClient, NerClient>(http =>
            {
                http.BaseAddress = baseAddress;
                http.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
                if (!string.IsNullOrEmpty(options.ApiKey))
                    http.DefaultRequestHeaders.Add(NerWarmup.ApiKeyHeader, options.ApiKey);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                ConnectTimeout = TimeSpan.FromSeconds(options.ConnectTimeoutSeconds),
            });

        // Separate client with a long timeout: only used to wake a sleeping NER service at startup.
        services.AddHttpClient(NerWarmup.ClientName, http =>
        {
            http.BaseAddress = baseAddress;
            http.Timeout = TimeSpan.FromSeconds(90);
        });
        services.AddHostedService<NerWarmup>();
        return services;
    }
}
