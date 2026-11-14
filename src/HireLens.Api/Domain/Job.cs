using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace HireLens.Api.Domain;

public sealed class Job
{
    [BsonId, BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    public string Title { get; set; } = "";
    public string Location { get; set; } = "";
    public string DescriptionText { get; set; } = "";
    public List<string> MustHaveSkills { get; set; } = [];
    public List<string> NiceToHaveSkills { get; set; } = [];
    public JobStatus Status { get; set; } = JobStatus.Open;
    public DateTime CreatedAt { get; set; }
}
