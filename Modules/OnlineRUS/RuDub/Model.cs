using System;
using System.Collections.Generic;

namespace RuDub;

public class RudubCatalogItem
{
    public string slug { get; set; }
    public string name { get; set; }
    public int episodes { get; set; }
}

public class RudubCard
{
    public int season { get; set; }
    public int episode { get; set; }
    public string episodeID { get; set; }
}

public class RudubEpisode
{
    public int num { get; set; }
    public string hash { get; set; }
}

public class RudubSeason
{
    public int num { get; set; }
    public string quality { get; set; }
    public List<RudubEpisode> episodes { get; set; } = new List<RudubEpisode>();
}

public class RudubShow
{
    public List<RudubSeason> seasons { get; set; } = new List<RudubSeason>();
    public DateTime expires { get; set; }
}

public class RudubVariant
{
    public string label { get; set; }
    public string url { get; set; }
}

public class RudubStream
{
    public string player { get; set; }
    public List<RudubVariant> variants { get; set; } = new List<RudubVariant>();
}
