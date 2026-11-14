namespace HireLens.Api.Infrastructure;

/// <summary>Bound from the "Mongo" section of configuration.</summary>
public sealed class MongoSettings
{
    public const string SectionName = "Mongo";

    public string ConnectionString { get; set; } = "mongodb://localhost:27017";
    public string DatabaseName { get; set; } = "hirelens";
}
