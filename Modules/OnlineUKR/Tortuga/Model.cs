using System.Collections.Generic;

namespace Tortuga;

public class EmbedModel
{
    public bool IsEmpty { get; set; }

    public string hls { get; set; }

    public List<Voice> serial { get; set; }
}

public class Season
{
    public string title { get; set; }

    public string number { get; set; }

    public string file { get; set; }

    public string subtitle { get; set; }

    public Series[] folder { get; set; }
}

public class Series
{
    public string title { get; set; }

    public string file { get; set; }

    public string subtitle { get; set; }
}

public class Voice
{
    public string title { get; set; }

    public string season { get; set; }

    public Season[] folder { get; set; }
}
