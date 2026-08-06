using System.Text.Json;
using System.Text.Json.Serialization;

namespace Restaurant.App;

public class MenuItem
{
    public int Id { get; set; }
    
    public string Name { get; set; } = string.Empty;

    // Maps the database/API "Description" column ("Noodles", "Beef", etc.) to Category
    [JsonPropertyName("description")]
    public string Category { get; set; } = string.Empty;

    public decimal Price { get; set; }

    [JsonPropertyName("isAvailable")]
    [JsonConverter(typeof(BoolFromIntJsonConverter))]
    public bool IsAvailable { get; set; }

    [JsonPropertyName("imageUrl")]
    public string ImageUrl { get; set; } = string.Empty;

    public string FormattedPrice => $"${Price:F2}";
}

public sealed class BoolFromIntJsonConverter : JsonConverter<bool>
{
    public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.True) return true;
        if (reader.TokenType == JsonTokenType.False) return false;
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out int value)) return value == 1;

        throw new JsonException("Expected boolean or 0/1 for isAvailable.");
    }

    public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options)
    {
        writer.WriteBooleanValue(value);
    }
}