namespace HireLens.Api.Ner;

/// <summary>
/// Pings the NER service once in the background when the API starts. On hosts that put idle services to
/// sleep (Render's free tier), the API and NER sleep together; without this, the first application after a
/// wake-up would hit a cold NER service, time out and be saved as "pending". Never blocks startup.
/// </summary>
public sealed class NerWarmup(IHttpClientFactory factory, ILogger<NerWarmup> logger) : BackgroundService
{
    public const string ClientName = "ner-warmup";
    public const string ApiKeyHeader = "X-Api-Key";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var response = await factory.CreateClient(ClientName).GetAsync("health", stoppingToken);
            logger.LogInformation("NER warm-up: {StatusCode}", (int)response.StatusCode);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !stoppingToken.IsCancellationRequested)
        {
            logger.LogWarning("NER warm-up failed: {Message}. Applications will be saved as pending until it's reachable", ex.Message);
        }
    }
}
