using Shared;
using Shared.Models.Events;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace WatchTogether
{
    public static class WsEvents
    {
        static int initialized = 0;

        // map of connection_id -> (room_id, uid)
        static readonly ConcurrentDictionary<string, (string roomId, string uid)> eventClients = new();

        public static void Start()
        {
            if (System.Threading.Interlocked.Exchange(ref initialized, 1) == 1)
                return;

            EventListener.NwsMessage += OnNwsMessage;
            EventListener.NwsDisconnected += OnNwsDisconnected;
            GcTask.Start();
        }

        public static void Stop()
        {
            EventListener.NwsMessage -= OnNwsMessage;
            EventListener.NwsDisconnected -= OnNwsDisconnected;
            GcTask.Stop();
            System.Threading.Interlocked.Exchange(ref initialized, 0);
        }

        static void OnNwsDisconnected(EventNwsDisconnected e)
        {
            if (!string.IsNullOrEmpty(e.connectionId))
            {
                if (eventClients.TryRemove(e.connectionId, out var info))
                {
                    _ = RemoveMemberAsync(e.connectionId, info.roomId);
                }
            }
        }

        static async Task RemoveMemberAsync(string connectionId, string roomId)
        {
            RoomDb.Members.TryRemove(connectionId, out _);

            bool hasMembers = RoomDb.Members.Values.Any(m => m.room_id == roomId);
            if (!hasMembers)
            {
                RoomDb.Rooms.TryRemove(roomId, out _);
            }
            else
            {
                await BroadcastRoomMembersCountAsync(roomId);
            }
        }

        static void OnNwsMessage(EventNwsMessage e)
        {
            if (string.IsNullOrEmpty(e.method))
                return;

            string method = e.method.ToLowerInvariant();

            if (method == "watchtogether_join")
            {
                string roomId = GetStringArg(e.args, 0);
                string uid = GetStringArg(e.args, 1);

                if (!string.IsNullOrEmpty(roomId) && !string.IsNullOrEmpty(uid))
                {
                    _ = JoinRoomAsync(e.connectionId, roomId, uid);
                }
                return;
            }

            if (method.StartsWith("watchtogether_"))
            {
                string roomId = GetStringArg(e.args, 0);
                string uid = GetStringArg(e.args, 1);

                if (!string.IsNullOrEmpty(roomId) && !string.IsNullOrEmpty(uid))
                {
                    if (method == "watchtogether_sync")
                    {
                        string state = GetStringArg(e.args, 2); // playing, paused
                        double position = GetDoubleArg(e.args, 3);
                        int season = GetIntArg(e.args, 4);
                        int episode = GetIntArg(e.args, 5);

                        _ = HandleSyncAsync(roomId, e.connectionId, state, position, season, episode);
                    }
                    else if (method == "watchtogether_leave")
                    {
                        if (eventClients.TryRemove(e.connectionId, out _))
                        {
                            _ = RemoveMemberAsync(e.connectionId, roomId);
                        }
                    }
                }
            }
        }

        static async Task JoinRoomAsync(string connectionId, string roomId, string uid)
        {
            var oldConnections = eventClients.Where(x => x.Value.uid == uid && x.Key != connectionId).ToList();
            foreach (var old in oldConnections)
            {
                _ = Startup.Nws.SendAsync(old.Key, "watchtogether_kicked");
                if (eventClients.TryRemove(old.Key, out var info))
                {
                    await RemoveMemberAsync(old.Key, info.roomId);
                }
            }

            eventClients.AddOrUpdate(connectionId, (roomId, uid), (_, __) => (roomId, uid));

            if (!RoomDb.Rooms.TryGetValue(roomId, out var room)) return;

            var member = new RoomMemberModel
            {
                room_id = roomId,
                uid = uid,
                connection_id = connectionId,
                last_seen = DateTime.UtcNow
            };
            RoomDb.Members.AddOrUpdate(connectionId, member, (_, __) => member);

            _ = Startup.Nws.SendAsync(connectionId, "watchtogether_sync_update", room.state, room.position, room.season, room.episode);
            await BroadcastRoomMembersCountAsync(roomId);
        }

        static async Task HandleSyncAsync(string roomId, string senderConnectionId, string state, double position, int season, int episode)
        {
            if (RoomDb.Rooms.TryGetValue(roomId, out var room))
            {
                room.state = state;
                room.position = position;
                if (season > 0) room.season = season;
                if (episode > 0) room.episode = episode;
                room.update_time = DateTime.UtcNow;
            }

            var targets = eventClients.Where(i => i.Value.roomId == roomId && i.Key != senderConnectionId).Select(i => i.Key).ToArray();
            if (targets.Length == 0) return;

            var tasks = targets.Select(t => Startup.Nws.SendAsync(t, "watchtogether_sync_update", state, position, season, episode));
            await Task.WhenAll(tasks);
        }

        static async Task BroadcastRoomMembersCountAsync(string roomId)
        {
            var targets = eventClients.Where(i => i.Value.roomId == roomId).Select(i => i.Key).ToArray();
            var tasks = targets.Select(t => Startup.Nws.SendAsync(t, "watchtogether_members", targets.Length));
            await Task.WhenAll(tasks);
        }

        static string GetStringArg(JsonElement args, int index)
        {
            if (args.ValueKind != JsonValueKind.Array || args.GetArrayLength() <= index)
                return null;

            var element = args[index];
            if (element.ValueKind == JsonValueKind.String)
                return element.GetString();
            if (element.ValueKind == JsonValueKind.Null)
                return null;
            return element.ToString();
        }

        static double GetDoubleArg(JsonElement args, int index)
        {
            if (args.ValueKind != JsonValueKind.Array || args.GetArrayLength() <= index)
                return 0;

            var element = args[index];
            if (element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out double val))
                return val;
            return 0;
        }

        static int GetIntArg(JsonElement args, int index)
        {
            if (args.ValueKind != JsonValueKind.Array || args.GetArrayLength() <= index)
                return 0;

            var element = args[index];
            if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out int val))
                return val;
            return 0;
        }
    }
}