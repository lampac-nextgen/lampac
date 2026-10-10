using Shared.Models.Templates;
using System.Collections.Generic;

namespace KinoPub;

public class Audio
{
    public int index { get; set; }

    public string lang { get; set; }

    public string codec { get; set; }

    public Author author { get; set; }

    public Author type { get; set; }
}

public class Author
{
    public int? id { get; set; }

    public string title { get; set; }
}

public class Episode
{
    public long id { get; set; }

    public short number { get; set; }

    public string title { get; set; }

    public Subtitle[] subtitles { get; set; }

    public File[] files { get; set; }

    public Audio[] audios { get; set; }
}

public class File
{
    public string quality { get; set; }

    public string codec { get; set; }

    public int w { get; set; }

    public int h { get; set; }

    public int quality_id { get; set; }

    public string file { get; set; }

    public Url url { get; set; }
}

public class Item
{
    public bool advert { get; set; }

    public short quality { get; set; }

    public string voice { get; set; }

    public Video[] videos { get; set; }

    public Season[] seasons { get; set; }
}

public class RootObject
{
    public int status { get; set; }

    public Item item { get; set; }
}

public class SearchItem
{
    public int id { get; set; }

    public string type { get; set; }

    public string title { get; set; }

    public string voice { get; set; }

    public long? kinopoisk { get; set; }

    public long? imdb { get; set; }

    public short year { get; set; }

    public Dictionary<string, string> posters { get; set; }
}

public class SearchObject
{
    public SearchItem[] items { get; set; }
}

public class SearchResult
{
    public int id { get; set; }

    public SimilarTpl similars { get; set; }
}

public class Season
{
    public short number { get; set; }

    public Episode[] episodes { get; set; }
}

public class Subtitle
{
    public string lang { get; set; }

    public string url { get; set; }
}

public class Url
{
    public string http { get; set; }

    public string hls { get; set; }

    public string hls4 { get; set; }

    public string hls2 { get; set; }
}

public class Video
{
    public long id { get; set; }

    public string title { get; set; }

    public Subtitle[] subtitles { get; set; }

    public File[] files { get; set; }

    public Audio[] audios { get; set; }
}
