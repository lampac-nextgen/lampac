namespace Online.Models.FlixCDN
{
    public class SearchItem
    {
        public SimilarTpl similar { get; set; }

        public string iframe_url { get; set; }

        public string type { get; set; }

        public string title_rus { get; set; }

        public string title_orig { get; set; }

        public int? year { get; set; }

        public string poster { get; set; }

        public List<Voice> translations { get; set; }
    }

    public class SearchRoot
    {
        public SearchItem[] result { get; set; }
    }

    public class Voice
    {
        public int id { get; set; }

        public string title { get; set; }

        public int season { get; set; }

        public int episode { get; set; }
    }
}
