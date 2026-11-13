using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace HireLens.Api.Endpoints;

/// <summary>
/// Phase 1 smoke test: proves the API can write to and read from MongoDB.
/// Will be removed once the real Jobs/Applications endpoints exist.
/// </summary>
public static class HelloEndpoints
{
    public sealed class Greeting
    {
        [BsonId, BsonRepresentation(BsonType.ObjectId)]
        public string? Id { get; set; }
        public required string Message { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public sealed record CreateGreeting(string Message);

    public static IEndpointRouteBuilder MapHelloEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/hello").WithTags("Hello");

        group.MapPost("/", async (CreateGreeting body, IMongoDatabase db, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.Message))
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["message"] = ["Message is required."],
                });

            var greeting = new Greeting { Message = body.Message.Trim() };
            await db.GetCollection<Greeting>("greetings").InsertOneAsync(greeting, cancellationToken: ct);
            return Results.Created($"/api/hello/{greeting.Id}", greeting);
        });

        group.MapGet("/", async (IMongoDatabase db, CancellationToken ct) =>
        {
            var latest = await db.GetCollection<Greeting>("greetings")
                .Find(FilterDefinition<Greeting>.Empty)
                .SortByDescending(g => g.CreatedAt)
                .Limit(10)
                .ToListAsync(ct);
            return Results.Ok(latest);
        });

        return app;
    }
}
