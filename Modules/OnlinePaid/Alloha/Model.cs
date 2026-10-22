using System.Collections.Generic;

namespace Alloha;

public class ContentRoot
{
    public MediaItem data { get; set; }
}

public class SearchListRoot
{
    public List<MediaItem> data { get; set; }
}

public class MediaItem
{
    public string name { get; set; }
    public string original_name { get; set; }
    public int year { get; set; }
    public string token { get; set; }
    public string poster { get; set; }
    public Category category { get; set; }
    public List<TranslationItem> translations { get; set; }
    public List<Season> seasons { get; set; }
    public Flag flags { get; set; }
}

public class Category
{
    public string slug { get; set; }
}

public class Flag
{
    public bool uhd { get; set; }
    public bool directors_cut { get; set; }
}

public class Season
{
    public int season { get; set; }
    public List<Episode> episodes { get; set; }
}

public class Episode
{
    public int episode { get; set; }
    public List<TranslationItem> translations { get; set; }
}

public class TranslationItem
{
    public int id { get; set; }
    public string name { get; set; }
    public string quality { get; set; }
    public bool uhd { get; set; }
}

public class DirectRoot
{
    public DirectData data { get; set; }
}

public class DirectData
{
    public FileData file { get; set; }
}

public class FileData
{
    public List<Track> tracks { get; set; }
    public List<HlsSource> hlsSource { get; set; }
    public string skipTime { get; set; }
    public string removeTime { get; set; }
}

public class Track
{
    public string label { get; set; }
    public string src { get; set; }
}

public class HlsSource
{
    public bool @default { get; set; }
    public Dictionary<string, string> quality { get; set; }
    public Dictionary<string, string> reserve { get; set; }
}
