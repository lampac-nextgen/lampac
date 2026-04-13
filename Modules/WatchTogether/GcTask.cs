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
            _timer = new Timer(DoWork, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(30));
        }

        public static void Stop()
        {
            _timer?.Dispose();
        }

        private static void DoWork(object state)
        {
            try
            {
                var oldLimit = DateTime.UtcNow.AddHours(-12);
                var emptyLimit = DateTime.UtcNow.AddHours(-1);

                var garbageRooms = RoomDb.Rooms.Values.Where(r =>
                    r.update_time < oldLimit ||
                    (r.create_time < emptyLimit && !RoomDb.Members.Values.Any(m => m.room_id == r.id))
                ).Select(r => r.id).ToList();

                foreach (var roomId in garbageRooms)
                {
                    RoomDb.Rooms.TryRemove(roomId, out _);

                    var membersToRemove = RoomDb.Members.Where(m => m.Value.room_id == roomId).Select(m => m.Key).ToList();
                    foreach (var connId in membersToRemove)
                    {
                        RoomDb.Members.TryRemove(connId, out _);
                    }
                }
            }
            catch { }
        }
    }
}