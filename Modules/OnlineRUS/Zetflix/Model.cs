using System.Collections.Generic;

namespace Zetflix
{
    public class EmbedModel
    {
        public List<RootObject> pl { get; set; }

        public bool movie { get; set; }

        public string quality { get; set; }

        public string check_url { get; set; }
    }

    public struct RootObject
    {
        public string title { get; set; }

        public string file { get; set; }

        public Folder[] folder { get; set; }
    }

    public struct Folder
    {
        public string comment { get; set; }

        public string file { get; set; }
    }
}
