using System;
using System.Collections.Concurrent;
using System.Linq;
using Shared.Services.Utilities;

namespace WatchTogether
{
    public class RoomModel
    {
        public string id { get; set; }
        public string name { get; set; }
        public string owner_uid { get; set; }
        public string owner_name { get; set; }

        public string stream_url { get; set; }
        public string title { get; set; }
        public string poster { get; set; }

        public string source { get; set; }
        public string type { get; set; }
        public int tmdb_id { get; set; }
        public int season { get; set; }
        public int episode { get; set; }

        public string state { get; set; }
        public double position { get; set; }

        public long at_server_time { get; set; }

        public string password_hash { get; set; }
        public bool has_password => !string.IsNullOrEmpty(password_hash);

        public DateTime create_time { get; set; }
        public DateTime update_time { get; set; }
    }

    public class RoomMemberModel
    {
        public string room_id { get; set; }
        public string uid { get; set; }
        public string connection_id { get; set; }

        public string base_name { get; set; }
        public string display_name { get; set; }

        public DateTime last_seen { get; set; }
    }

    public static class RoomDb
    {
        public static readonly ConcurrentDictionary<string, RoomModel> Rooms = new();
        public static readonly ConcurrentDictionary<string, RoomMemberModel> Members = new();

        public static string HashPassword(string password)
        {
            if (string.IsNullOrEmpty(password)) return null;
            using (var sha256 = System.Security.Cryptography.SHA256.Create())
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(password);
                var hash = sha256.ComputeHash(bytes);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }

        public static bool VerifyPassword(RoomModel room, string password)
        {
            if (room == null) return false;
            if (!room.has_password) return true;
            return string.Equals(HashPassword(password), room.password_hash, StringComparison.OrdinalIgnoreCase);
        }

        public static string AssignDisplayName(string roomId, string baseName)
        {
            string normalized = string.IsNullOrWhiteSpace(baseName) ? "Guest" : baseName.Trim();

            var taken = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var connections = WsEvents.GetConnectionsInRoom(roomId);
            foreach (var conn in connections)
            {
                if (Members.TryGetValue(conn, out var member) && !string.IsNullOrEmpty(member.display_name))
                    taken.Add(member.display_name);
            }

            if (!taken.Contains(normalized)) return normalized;

            for (int i = 2; i < 10000; i++)
            {
                string candidate = normalized + " #" + i;
                if (!taken.Contains(candidate)) return candidate;
            }
            return normalized + " #" + Guid.NewGuid().ToString("N").Substring(0, 4);
        }
    }
}
