using System.Text.Json;
using System.Text.Json.Serialization;

namespace Music;

public static class MusicJson
{
    static readonly JsonSerializerOptions options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, options);

    public static T Deserialize<T>(string value) => JsonSerializer.Deserialize<T>(value, options);
}
