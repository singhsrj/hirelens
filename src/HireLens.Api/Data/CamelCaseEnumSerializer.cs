using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;

namespace HireLens.Api.Data;

/// <summary>Stores enums as camelCase strings ("open", "shortlisted") to match the API's JSON.</summary>
public sealed class CamelCaseEnumSerializer<TEnum> : StructSerializerBase<TEnum> where TEnum : struct, Enum
{
    private static readonly Dictionary<TEnum, string> ToName =
        Enum.GetValues<TEnum>().ToDictionary(v => v, v => ToCamel(v.ToString()));

    private static readonly Dictionary<string, TEnum> FromName =
        ToName.ToDictionary(kv => kv.Value, kv => kv.Key, StringComparer.OrdinalIgnoreCase);

    public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, TEnum value) =>
        context.Writer.WriteString(ToName[value]);

    public override TEnum Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
    {
        var type = context.Reader.GetCurrentBsonType();
        if (type != BsonType.String)
            throw new FormatException($"Expected a string for {typeof(TEnum).Name}, got {type}.");

        var name = context.Reader.ReadString();
        return FromName.TryGetValue(name, out var value)
            ? value
            : throw new FormatException($"'{name}' is not a valid {typeof(TEnum).Name}.");
    }

    private static string ToCamel(string s) => char.ToLowerInvariant(s[0]) + s[1..];
}
