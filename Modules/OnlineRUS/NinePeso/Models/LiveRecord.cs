namespace NinePeso;

public sealed class LiveRecord
{
    public int id { get; set; }
    public string imdb_id { get; set; }
    public string kinopoisk_id { get; set; }
    public string ru_title { get; set; }
    public string orig_title { get; set; }
    public string released { get; set; }

    // Provider metadata may contain either YYYY or a full YYYY-MM-DD date.
    public static string ReleaseYear(string released)
        => released?.Split('-')[0];
}
