using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace HireLens.Api.Data;

/// <summary>
/// Stores ASP.NET Core Data Protection keys (which sign the auth cookie) in MongoDB.
/// Without this, keys live in the container's filesystem and vanish on every restart, so a hosting tier
/// that sleeps idle containers would sign the recruiter out each time it wakes.
/// Keys are stored unencrypted; MongoDB Atlas encrypts data at rest.
/// </summary>
public sealed class MongoXmlRepository(IMongoDatabase db) : IXmlRepository
{
    public const string CollectionName = "dataProtectionKeys";

    public sealed class KeyDocument
    {
        [BsonId, BsonRepresentation(BsonType.ObjectId)]
        public string? Id { get; set; }
        public string FriendlyName { get; set; } = "";
        public string Xml { get; set; } = "";
    }

    private readonly IMongoCollection<KeyDocument> _keys = db.GetCollection<KeyDocument>(CollectionName);

    public IReadOnlyCollection<XElement> GetAllElements() =>
        _keys.Find(FilterDefinition<KeyDocument>.Empty).ToList().Select(k => XElement.Parse(k.Xml)).ToList();

    public void StoreElement(XElement element, string friendlyName) =>
        _keys.InsertOne(new KeyDocument { FriendlyName = friendlyName, Xml = element.ToString(SaveOptions.DisableFormatting) });
}

public static class DataProtectionExtensions
{
    public static IServiceCollection AddMongoDataProtection(this IServiceCollection services)
    {
        services.AddDataProtection().SetApplicationName("HireLens");
        services.AddSingleton<IConfigureOptions<KeyManagementOptions>>(sp =>
            new ConfigureOptions<KeyManagementOptions>(o =>
                o.XmlRepository = new MongoXmlRepository(sp.GetRequiredService<IMongoDatabase>())));
        return services;
    }
}
