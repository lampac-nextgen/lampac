using System.Text.Json;

namespace Music;

// Общие хелперы импортёров плейлистов (SoundCloud / Spotify / Apple Music).
//
// Контракт импорта: результат либо ПОЛНЫЙ, либо available=false с message —
// частично скачанный список сохранять нельзя (sync перезаписывает payload
// целиком, усечёнка молча потеряла бы треки пользователя). Единственное
// допустимое усечение — явный кап MaxImportTracks с truncated=true.
public static class MusicPlaylistImportHelpers
{
    public static MusicUserPlaylistImportResult ImportUnavailable(string message)
        => new()
        {
            available = false,
            message = message,
            tracks = new List<MusicTrack>()
        };

    // дедуп по id с перенумерацией track_number по итоговому порядку
    public static List<MusicTrack> DeduplicateTracks(IEnumerable<MusicTrack> tracks)
    {
        var result = new List<MusicTrack>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var track in tracks ?? Enumerable.Empty<MusicTrack>())
        {
            if (track == null || string.IsNullOrWhiteSpace(track.id) || !seen.Add(track.id))
                continue;

            track.track_number = result.Count + 1;
            result.Add(track);
        }

        return result;
    }

    public static JsonElement? GetProperty(JsonElement element, params string[] path)
    {
        var current = element;
        foreach (var name in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(name, out var next) || next.ValueKind == JsonValueKind.Null)
                return null;

            current = next;
        }

        return current;
    }

    public static JsonElement? GetProperty(JsonElement? element, params string[] path)
        => element == null ? null : GetProperty(element.Value, path);

    public static string GetString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var property))
            return null;

        return property.ValueKind == JsonValueKind.String ? property.GetString() : null;
    }

    public static int? GetInt(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var property))
            return null;

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out int value))
            return value;

        if (property.ValueKind == JsonValueKind.String && int.TryParse(property.GetString(), out int parsed))
            return parsed;

        return null;
    }
}
