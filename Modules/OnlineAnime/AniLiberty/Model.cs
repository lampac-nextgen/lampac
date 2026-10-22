namespace AniLiberty;

public class Name
{
    public string main { get; set; }

    public string english { get; set; }
}

public class Poster
{
    public string src { get; set; }
}

public class SearchItem
{
    public int id { get; set; }

    public int year { get; set; }

    public Name name { get; set; }

    public Poster poster { get; set; }
}

public class Episode
{
    public string ordinal { get; set; }

    public string name { get; set; }

    public string hls_1080 { get; set; }

    public string hls_720 { get; set; }

    public string hls_480 { get; set; }
}

public class Release
{
    public string alias { get; set; }

    public Name name { get; set; }

    public Episode[] episodes { get; set; }
}
