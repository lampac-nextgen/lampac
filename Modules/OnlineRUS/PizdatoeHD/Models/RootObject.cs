using System.Collections.Generic;

namespace PizdatoeHD
{
    public class RootObject
    {
        public bool IsEmpty { get; set; }

        public string content { get; set; }

        public string trs { get; set; }
    }

    public class MovieModel
    {
        public List<ApiModel> links { get; set; }

        public string subtitlehtml { get; set; }
    }

    public class SearchModel
    {
        public bool IsError { get; set; }

        public bool IsEmpty { get; set; }

        public string content { get; set; }

        public string href { get; set; }

        public List<SimilarModel> similar { get; set; }
    }

    public class SimilarModel
    {
        public SimilarModel() { }

        public SimilarModel(string title, string year, string href, string img)
        {
            this.title = title;
            this.year = year;
            this.href = href;
            this.img = img;
        }

        public string title { get; set; }

        public string year { get; set; }

        public string href { get; set; }

        public string img { get; set; }
    }

    public class ApiModel
    {
        public string title { get; set; }

        public string stream_url { get; set; }
    }
}
