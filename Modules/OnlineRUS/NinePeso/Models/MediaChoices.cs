using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NinePeso;

/// <summary>
/// One selectable Lampa voice. A regular voice has its own media ID.
/// A nested audio track shares the media ID but has a distinct track number.
/// </summary>
public sealed record MediaChoice(string Key, string Name, long MediaId, int TrackNumber, int Quality)
{
    public string QualityLabel => Quality > 0 ? $"{Quality}p" : null;
}

public static class MediaChoices
{
    public static IEnumerable<MediaChoice> Enumerate(JArray media)
    {
        if (media == null)
            yield break;

        foreach (var item in media.OfType<JObject>())
        {
            long mediaId = item.Value<long?>("id") ?? 0;
            // The API can advertise a stream_path even when its CDN copy is
            // unavailable. Skip that media and all its nested audio tracks.
            // Missing/null availability preserves compatibility with older responses.
            if (mediaId <= 0 || string.IsNullOrWhiteSpace(item.Value<string>("stream_path")) ||
                (item["cdn_available"] is JValue { Type: JTokenType.Boolean } available &&
                 available.Value<bool>() == false))
                continue;

            int quality = item.Value<int?>("max_quality") ?? 0;
            string mediaName = item["translation"]?["title"]?.ToString()
                ?? "По умолчанию";
            string mediaKey = TranslationKey(item["translation"], mediaName);
            yield return new MediaChoice(mediaKey, mediaName, mediaId, 0, quality);

            if (item["audio_tracks"] is not JArray tracks)
                continue;

            var seen = new HashSet<string>(StringComparer.Ordinal) { mediaKey };
            foreach (var track in tracks.OfType<JObject>())
            {
                int trackNumber = track.Value<int?>("track_num") ?? 0;
                string path = track.Value<string>("stream_path");
                if (trackNumber <= 0 || string.IsNullOrWhiteSpace(path))
                    continue;

                string name = track["orig_title"]?.ToString()
                    ?? track["translation"]?["title"]?.ToString()
                    ?? $"Аудиодорожка {trackNumber}";
                string key = TranslationKey(track["translation"], name);

                // Track 1 is commonly the media's default voice, so avoid
                // showing a duplicate next to its regular translation.
                if (seen.Add(key))
                    yield return new MediaChoice(key, name, mediaId, trackNumber, quality);
            }
        }
    }

    static string TranslationKey(JToken translation, string fallbackName)
        => translation?["id"]?.ToString()
           ?? translation?["title"]?.ToString()
           ?? $"audio:{fallbackName}";
}
