using System;
using System.Collections.Concurrent;

namespace WatchTogether
{
    public class RoomModel
    {
        public string id { get; set; }
        public string title { get; set; }
        public string state { get; set; }
        public string source { get; set; }
        public string type { get; set; }

        public int tmdb_id { get; set; }
        public int season { get; set; }
        public int episode { get; set; }

        public double position { get; set; }

        public DateTime create_time { get; set; }
        public DateTime update_time { get; set; }
    }

    public class RoomMemberModel
    {
        public string room_id { get; set; }
        public string uid { get; set; }
        public string connection_id { get; set; }

        public DateTime last_seen { get; set; }
    }

    public static class RoomDb
    {
        public static readonly ConcurrentDictionary<string, RoomModel> Rooms = new();        
        public static readonly ConcurrentDictionary<string, RoomMemberModel> Members = new();
    }
}