using System;
using System.Linq;
using System.Threading;

namespace WatchTogether
{
    public static class GcTask
    {
        static Timer _timer;

        public static void Start()
        {
            _timer = new Timer(DoWork, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5));
        }

        public static void Stop()
        {
            _timer?.Dispose();
        }

        private static void DoWork(object state)
        {
            try
            {
                var orphanedMembers = RoomDb.Members.Keys
                    .Where(connId => !WsEvents.IsConnectionActive(connId))
                    .ToList();
                foreach (var connId in orphanedMembers)
                    RoomDb.Members.TryRemove(connId, out _);

                var oldLimit = DateTime.UtcNow.AddHours(-Math.Max(1, ModInit.conf.gc_max_lifetime_hours));
                var emptyLimit = DateTime.UtcNow.AddMinutes(-Math.Max(5, ModInit.conf.gc_empty_timeout_minutes));

                var garbageRooms = RoomDb.Rooms.Values.Where(r =>
                    r.update_time < oldLimit ||
                    (r.create_time < emptyLimit && WsEvents.GetConnectionsInRoom(r.id).Length == 0)
                ).Select(r => r.id).ToList();

                foreach (var roomId in garbageRooms)
                {
                    RoomDb.Rooms.TryRemove(roomId, out _);

                    var membersToRemove = RoomDb.Members.Where(m => m.Value.room_id == roomId).Select(m => m.Key).ToList();
                    foreach (var connId in membersToRemove)
                        RoomDb.Members.TryRemove(connId, out _);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WatchTogether] GC Error: {ex.Message}");
            }
        }
    }
}
