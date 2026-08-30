using System.Collections.Generic;

namespace FlixCDN;

public class PlayerPayload
{
    public int id { get; set; }

    public bool is_serial { get; set; }

    public string type { get; set; }

    public int translate { get; set; }

    public string translateTitle { get; set; }

    public short season { get; set; }

    public int[] episodes { get; set; }

    public Dictionary<string, int> seasons { get; set; }

    public Dictionary<string, int[]> seasons_episodes { get; set; }

    public List<PlayerTranslation> translations { get; set; }
}

public class PlayerTranslation
{
    public int id { get; set; }

    public string title { get; set; }

    public int episodes_qty { get; set; }
}

public class PlayerFiles
{
    public string file { get; set; }

    public long media_id { get; set; }
}
