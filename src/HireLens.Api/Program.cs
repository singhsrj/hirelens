using HireLens.Api.Endpoints;
using HireLens.Api.Infrastructure;
using MongoDB.Bson;
using MongoDB.Driver;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddMongo(builder.Configuration);

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(); // UI at /scalar
}

app.UseDefaultFiles();
app.UseStaticFiles();

// Liveness + Mongo connectivity check.
app.MapGet("/health", async (IMongoDatabase db, CancellationToken ct) =>
{
    await db.RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1), cancellationToken: ct);
    return Results.Ok(new { status = "ok", mongo = "ok" });
}).WithTags("Health");

app.MapHelloEndpoints();

app.Run();

// Exposed for WebApplicationFactory in integration tests.
public partial class Program;
