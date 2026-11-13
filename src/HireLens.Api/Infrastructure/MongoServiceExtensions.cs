using Microsoft.Extensions.Options;
using MongoDB.Bson.Serialization.Conventions;
using MongoDB.Driver;

namespace HireLens.Api.Infrastructure;

public static class MongoServiceExtensions
{
    /// <summary>
    /// Registers a singleton <see cref="IMongoClient"/> (it is thread-safe and pools connections)
    /// and the <see cref="IMongoDatabase"/> for the app.
    /// </summary>
    public static IServiceCollection AddMongo(this IServiceCollection services, IConfiguration config)
    {
        // camelCase field names in Mongo, ignore unknown fields so the schema can evolve.
        var conventions = new ConventionPack
        {
            new CamelCaseElementNameConvention(),
            new IgnoreExtraElementsConvention(true),
        };
        ConventionRegistry.Register("HireLensConventions", conventions, _ => true);

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

        return services;
    }
}
