using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using HireLens.Api.Data;
using HireLens.Api.Features.Applications;
using HireLens.Api.Features.Auth;
using HireLens.Api.Features.Jobs;
using HireLens.Api.Ner;
using HireLens.Api.Seed;
using MongoDB.Bson;
using MongoDB.Driver;
using Scalar.AspNetCore;

// ---------- Command: hash-password (prints a hash for Recruiter__PasswordHash, then exits) ----------
if (args is ["hash-password", ..])
{
    Console.Error.Write("Password: ");
    var password = Console.IsInputRedirected ? Console.ReadLine() ?? "" : ReadHidden();
    Console.Error.WriteLine();
    if (password.Length < 12)
    {
        Console.Error.WriteLine("Use at least 12 characters.");
        return 1;
    }
    Console.WriteLine(RecruiterAuthenticator.Hash(password));
    return 0;
}

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddValidation(); // .NET 10 built-in DataAnnotations validation for minimal APIs

// Enums travel as camelCase strings ("open", "shortlisted"), same as in Mongo.
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddMongo(builder.Configuration);
builder.Services.AddMongoDataProtection();
builder.Services.AddRecruiterAuth(builder.Configuration);
builder.Services.AddNerClient(builder.Configuration);
builder.Services.AddScoped<JobService>();
builder.Services.AddScoped<ApplicationService>();
builder.Services.AddScoped<DemoSeeder>();

// Per-IP fixed-window limits: apply is the public write endpoint, skill suggestions run CPU-heavy NER,
// and sign-in is limited to slow down password guessing.
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    AddPerIpPolicy(o, ApplicationEndpoints.ApplyRateLimitPolicy, "RateLimiting:Apply", defaultLimit: 5);
    AddPerIpPolicy(o, JobEndpoints.SuggestRateLimitPolicy, "RateLimiting:SuggestSkills", defaultLimit: 20);
    AddPerIpPolicy(o, AuthEndpoints.LoginRateLimitPolicy, "RateLimiting:Login", defaultLimit: 5);
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

// ---------- Command: seed [--reset] (loads demo data, then exits) ----------
if (args is ["seed", ..])
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetServices<IHostedService>().OfType<MongoIndexInitializer>().Single().StartAsync(default);
    return await scope.ServiceProvider.GetRequiredService<DemoSeeder>().RunAsync(reset: args.Contains("--reset"), default);
}

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

app.UseAuthentication();
app.UseRecruiterPageGate();   // /recruiter/* pages need a signed-in recruiter
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        // Recruiter pages must never come from a browser cache, or a signed-out user could still see one
        // (the server-side gate only runs if the request reaches the server). Other HTML revalidates so
        // deploys show up immediately; CSS/JS keep normal caching.
        var path = ctx.Context.Request.Path;
        if (path.StartsWithSegments("/recruiter"))
            ctx.Context.Response.Headers.CacheControl = "no-store";
        else if (path.Value?.EndsWith(".html", StringComparison.OrdinalIgnoreCase) == true || path == "/")
            ctx.Context.Response.Headers.CacheControl = "no-cache";
    },
});
app.UseRateLimiter();
app.UseAuthorization();

// Liveness + Mongo connectivity check.
app.MapGet("/health", async (IMongoDatabase db, CancellationToken ct) =>
{
    await db.RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1), cancellationToken: ct);
    return Results.Ok(new { status = "ok", mongo = "ok" });
}).WithTags("Health");

app.MapAuthEndpoints();
app.MapJobEndpoints();
app.MapApplicationEndpoints();

app.Run();
return 0;

static string ReadHidden()
{
    var chars = new List<char>();
    while (true)
    {
        var key = Console.ReadKey(intercept: true);
        if (key.Key == ConsoleKey.Enter) return new string([.. chars]);
        if (key.Key == ConsoleKey.Backspace) { if (chars.Count > 0) chars.RemoveAt(chars.Count - 1); }
        else if (!char.IsControl(key.KeyChar)) chars.Add(key.KeyChar);
    }
}

// Exposed for WebApplicationFactory in integration tests.
public partial class Program;
