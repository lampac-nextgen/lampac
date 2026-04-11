using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

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

        private static async void DoWork(object state)
        {
            try
            {
                await using (var db = SqlContext.Create())
                {
                    var oldLimit = DateTime.UtcNow.AddHours(-12);
                    var emptyLimit = DateTime.UtcNow.AddHours(-1);

                    var garbageRooms = await db.Rooms.Where(r =>
                        r.update_time < oldLimit ||
                        (r.create_time < emptyLimit && !db.RoomMembers.Any(m => m.room_id == r.id))
                    ).ToListAsync();

                    if (garbageRooms.Any())
                    {
                        foreach (var r in garbageRooms)
                        {
                            var members = await db.RoomMembers.Where(m => m.room_id == r.id).ToListAsync();
                            db.RoomMembers.RemoveRange(members);
                        }
                        db.Rooms.RemoveRange(garbageRooms);
                        await db.SaveChangesAsync();
                    }
                }
            }
            catch { }
        }
    }
}