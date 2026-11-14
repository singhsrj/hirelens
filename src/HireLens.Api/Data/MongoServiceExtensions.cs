using HireLens.Api.Domain;
using Microsoft.Extensions.Options;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Conventions;
using MongoDB.Driver;

namespace HireLens.Api.Data;

public static class MongoServiceExtensions
{
    private static readonly Lock MappingLock = new();
    private static bool _mappingRegistered;

    /// <summary>
    /// Registers a singleton <see cref="IMongoClient"/> (thread-safe, owns the connection pool),
    /// the app's <see cref="IMongoDatabase"/>, the repositories, and startup index creation.
    /// </summary>
    public static IServiceCollection AddMongo(this IServiceCollection services, IConfiguration config)
    {
        RegisterBsonMapping();

        services.Configure<MongoSettings>(config.GetSection(MongoSettings.SectionName));

        services.AddSingleton<IMongoClient>(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<MongoSettings>>().Value;
            return new MongoClient(settings.ConnectionString);
        });

        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<MongoSettings>>().Value;
            return sp.GetRequiredService<IMongoClient>().GetDatabase(settings.DatabaseName);
        });

        services.AddSingleton<IJobRepository, JobRepository>();
        services.AddSingleton<IApplicationRepository, ApplicationRepository>();
        services.AddHostedService<MongoIndexInitializer>();

        return services;
    }

    /// <summary>BSON mapping is process-global, so register it once even if the host is built several times (tests).</summary>
    private static void RegisterBsonMapping()
    {
        lock (MappingLock)
        {
            if (_mappingRegistered) return;

            // camelCase field names to match the JSON; ignore unknown fields so the schema can evolve.
            ConventionRegistry.Register("HireLens", new ConventionPack
            {
                new CamelCaseElementNameConvention(),
                new IgnoreExtraElementsConvention(true),
            }, t => t.Namespace?.StartsWith("HireLens") == true);

            BsonSerializer.RegisterSerializer(new CamelCaseEnumSerializer<JobStatus>());
            BsonSerializer.RegisterSerializer(new CamelCaseEnumSerializer<ApplicationStatus>());
            BsonSerializer.RegisterSerializer(new CamelCaseEnumSerializer<NerStatus>());

            _mappingRegistered = true;
        }
    }
}
