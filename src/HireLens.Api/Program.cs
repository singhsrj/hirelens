using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using HireLens.Api.Data;
using HireLens.Api.Features.Applications;
using HireLens.Api.Features.Jobs;
using HireLens.Api.Ner;
using MongoDB.Bson;
using MongoDB.Driver;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddValidation(); // .NET 10 built-in DataAnnotations validation for minimal APIs

// Enums travel as camelCase strings ("open", "shortlisted"), same as in Mongo.
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddMongo(builder.Configuration);
builder.Services.AddNerClient(builder.Configuration);
builder.Services.AddScoped<JobService>();
builder.Services.AddScoped<ApplicationService>();

// Per-IP fixed-window limits. Apply is the public write endpoint; skill suggestions run CPU-heavy NER.
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    AddPerIpPolicy(o, ApplicationEndpoints.ApplyRateLimitPolicy, "RateLimiting:Apply", defaultLimit: 5);
    AddPerIpPolicy(o, JobEndpoints.SuggestRateLimitPolicy, "RateLimiting:SuggestSkills", defaultLimit: 20);
});

void AddPerIpPolicy(Microsoft.AspNetCore.RateLimiting.RateLimiterOptions o, string policy, string section, int defaultLimit)
{
    var limit = builder.Configuration.GetValue($"{section}:PermitLimit", defaultLimit);
    var window = TimeSpan.FromSeconds(builder.Configuration.GetValue($"{section}:WindowSeconds", 60));
    o.AddPolicy(policy, http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = limit, Window = window, QueueLimit = 0 }));
}

var app = builder.Build();

// Malformed request bodies (bad JSON, unknown enum values) surface as BadHttpRequestException: return its 400,
// not a 500. Everything else is a genuine server error.
app.UseExceptionHandler(new ExceptionHandlerOptions
{
    StatusCodeSelector = ex => ex is BadHttpRequestException bad ? bad.StatusCode : StatusCodes.Status500InternalServerError,
});
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(); // UI at /scalar
}

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRateLimiter();

// Liveness + Mongo connectivity check.
app.MapGet("/health", async (IMongoDatabase db, CancellationToken ct) =>
{
    await db.RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1), cancellationToken: ct);
    return Results.Ok(new { status = "ok", mongo = "ok" });
}).WithTags("Health");

app.MapJobEndpoints();
app.MapApplicationEndpoints();

app.Run();

// Exposed for WebApplicationFactory in integration tests.
public partial class Program;
