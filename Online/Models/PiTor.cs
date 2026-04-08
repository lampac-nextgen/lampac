namespace Online.Models.PiTor
{
    public class Torrent
    {
        public Torrent() { }

        public Torrent(string name, string voice, string magnet, int sid, string tr, string quality, long size, string mediainfo, Result torrent)
        {
            this.name = name;
            this.voice = voice;
            this.magnet = magnet;
            this.sid = sid;
            this.tr = tr;
            this.quality = quality;
            this.size = size;
            this.mediainfo = mediainfo;
            this.torrent = torrent;
        }

        public string name { get; set; }
        public string voice { get; set; }
        public string magnet { get; set; }
        public int sid { get; set; }
        public string tr { get; set; }
        public string quality { get; set; }
        public long size { get; set; }
        public string mediainfo { get; set; }
        public Result torrent { get; set; }
    }

    public class FileStat
    {
        public int id { get; set; }

        public string path { get; set; }
    }

    public class Info
    {
        public string[] voices { get; set; }

        public string sizeName { get; set; }

        public int[] seasons { get; set; }
    }

    public class Result
    {
        public string Tracker { get; set; }
        public string Title { get; set; }
        public long? Size { get; set; }
        public int Seeders { get; set; }
        public string MagnetUri { get; set; }
        public Info info { get; set; }

        public DateTime PublishDate { get; set; }
    }

    public class RootObject
    {
        public Result[] Results { get; set; }
    }

    public class Stat
    {
        public FileStat[] file_stats { get; set; }
    }
}
