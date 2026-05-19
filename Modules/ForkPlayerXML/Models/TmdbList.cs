using System.Collections.Generic;

namespace ForkXML;

public class TmdbList
{
    public List<TmdbMovie> results { get; set; }

    public int page { get; set; }

    public int total_pages { get; set; }
}
