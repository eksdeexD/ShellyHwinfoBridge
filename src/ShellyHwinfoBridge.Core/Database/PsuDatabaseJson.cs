using System.Text.Json;

namespace ShellyHwinfoBridge.Core.Database;

/// <summary>JSON (de)serialization of <see cref="PsuDatabase"/>.</summary>
public static class PsuDatabaseJson
{
    /// <summary>Gets the serializer options (camelCase, indented, comments and trailing commas tolerated).</summary>
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Deserializes a database.</summary>
    /// <param name="json">The JSON text.</param>
    /// <exception cref="JsonException">The text is not valid JSON for the schema.</exception>
    public static PsuDatabase Deserialize(string json) =>
        JsonSerializer.Deserialize<PsuDatabase>(json, Options) ?? throw new JsonException("The database JSON is null.");

    /// <summary>Serializes a database.</summary>
    /// <param name="database">The database.</param>
    public static string Serialize(PsuDatabase database) => JsonSerializer.Serialize(database, Options);
}
