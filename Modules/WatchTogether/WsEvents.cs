using Shared;
using Shared.Models.Events;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace WatchTogether
{
    public static class WsEvents
    {
        static int initialized = 0;
        static Timer heartbeatTimer;

        static readonly ConcurrentDictionary<string, (string roomId, string uid, string displayName)> eventClients = new();
        static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, bool>> roomConnections = new();

        public static bool IsConnectionActive(string connectionId) =>
            !string.IsNullOrEmpty(connectionId) && eventClients.ContainsKey(connectionId);

        static long ServerNowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        public static void Start()
        {
            if (System.Threading.Interlocked.Exchange(ref initialized, 1) == 1)
                return;

            EventListener.NwsMessage += OnNwsMessage;
            EventListener.NwsDisconnected += OnNwsDisconnected;
            GcTask.Start();

            int pingInterval = Math.Max(5, ModInit.conf.ws_ping_interval);
            heartbeatTimer = new Timer(SendHeartbeats, null, TimeSpan.FromSeconds(pingInterval), TimeSpan.FromSeconds(pingInterval));
        }

        public static void Stop()
        {
            EventListener.NwsMessage -= OnNwsMessage;
            EventListener.NwsDisconnected -= OnNwsDisconnected;
            GcTask.Stop();
            heartbeatTimer?.Dispose();
            heartbeatTimer = null;
            System.Threading.Interlocked.Exchange(ref initialized, 0);
        }

        static void SendHeartbeats(object state)
        {
            try
            {
                long now = ServerNowMs();
                var connectionIds = eventClients.Keys; // Returns an enumerator directly over keys
                foreach (var cid in connectionIds)
                {
                    try
                    {
                        _ = Startup.Nws.SendAsync(cid, "watchtogether_server_ping", now);
                    }
                    catch { }
                }
            }
            catch { }
        }

        static void OnNwsDisconnected(EventNwsDisconnected e)
        {
            if (string.IsNullOrEmpty(e.connectionId)) return;

            if (eventClients.TryRemove(e.connectionId, out var info))
            {
                _ = LeaveAsync(e.connectionId, info.roomId, info.uid, info.displayName, broadcastLeft: true);
            }
        }

        static void OnNwsMessage(EventNwsMessage e)
        {
            if (string.IsNullOrEmpty(e.method)) return;

            string method = e.method.ToLowerInvariant();
            if (!method.StartsWith("watchtogether_")) return;

            if (method == "watchtogether_join")
            {
                string roomId = GetStringArg(e.args, 0);
                string uid = GetStringArg(e.args, 1);
                string baseName = GetStringArg(e.args, 2);
                string password = GetStringArg(e.args, 3);

                if (!string.IsNullOrEmpty(roomId) && !string.IsNullOrEmpty(uid))
                    _ = JoinAsync(e.connectionId, roomId, uid, baseName, password);
                return;
            }

            if (method == "watchtogether_ping")
            {
                long t0 = GetLongArg(e.args, 0);
                _ = Startup.Nws.SendAsync(e.connectionId, "watchtogether_pong", t0, ServerNowMs());
                return;
            }

            string rId = GetStringArg(e.args, 0);
            string usrId = GetStringArg(e.args, 1);
            if (string.IsNullOrEmpty(rId) || string.IsNullOrEmpty(usrId)) return;

            if (method == "watchtogether_sync" || method == "watchtogether_action")
            {
                if (!eventClients.TryGetValue(e.connectionId, out var senderInfo)) return;
                if (!string.Equals(senderInfo.roomId, rId, StringComparison.Ordinal)) return;
                if (!string.Equals(senderInfo.uid, usrId, StringComparison.Ordinal)) return;

                string state = GetStringArg(e.args, 2);
                if (state != "playing" && state != "paused") return;
                double position = GetDoubleArg(e.args, 3);
                if (double.IsNaN(position) || double.IsInfinity(position) || position < 0 || position > 2592000) return;
                bool isAction = method == "watchtogether_action";
                _ = HandleSyncAsync(senderInfo.roomId, e.connectionId, state, position, broadcastNotice: isAction);
            }
            else if (method == "watchtogether_url_change")
            {
                if (!eventClients.TryGetValue(e.connectionId, out var senderInfo)) return;
                if (!string.Equals(senderInfo.roomId, rId, StringComparison.Ordinal)) return;
                if (!string.Equals(senderInfo.uid, usrId, StringComparison.Ordinal)) return;
                if (!RoomDb.Rooms.TryGetValue(senderInfo.roomId, out var room)) return;
                if (!string.Equals(room.owner_uid, usrId, StringComparison.Ordinal)) return;

                string newUrl = GetStringArg(e.args, 2);
                if (string.IsNullOrWhiteSpace(newUrl) || newUrl.Length > 8000) return;
                string newTitle = GetStringArg(e.args, 3) ?? string.Empty;
                if (newTitle.Length > 500) return;

                room.stream_url = newUrl.Trim();
                if (!string.IsNullOrEmpty(newTitle)) room.title = newTitle.Trim();
                room.state = "paused";
                room.position = 0;
                room.at_server_time = ServerNowMs();
                room.update_time = DateTime.UtcNow;

                _ = BroadcastUrlChangeAsync(senderInfo.roomId, e.connectionId, room.stream_url, room.title);
            }
            else if (method == "watchtogether_leave")
            {
                if (eventClients.TryRemove(e.connectionId, out var info))
                    _ = LeaveAsync(e.connectionId, info.roomId, info.uid, info.displayName, broadcastLeft: true);
            }
        }

        static async Task JoinAsync(string connectionId, string roomId, string uid, string baseName, string password)
        {
            if (!ModInit.conf.allow_anonymous && uid.StartsWith("web_"))
            {
                _ = Startup.Nws.SendAsync(connectionId, "watchtogether_error", "anonymous_disabled");
                return;
            }

            var oldConnections = eventClients.Where(x => x.Value.uid == uid && x.Key != connectionId).ToList();
            foreach (var old in oldConnections)
            {
                _ = Startup.Nws.SendAsync(old.Key, "watchtogether_kicked");
                if (eventClients.TryRemove(old.Key, out var info))
                    await LeaveAsync(old.Key, info.roomId, info.uid, info.displayName, broadcastLeft: false);
            }

            if (!RoomDb.Rooms.TryGetValue(roomId, out var room))
            {
                _ = Startup.Nws.SendAsync(connectionId, "watchtogether_error", "room_not_found");
                return;
            }

            if (!RoomDb.VerifyPassword(room, password))
            {
                _ = Startup.Nws.SendAsync(connectionId, "watchtogether_error", "wrong_password");
                return;
            }

            string displayName = RoomDb.AssignDisplayName(roomId, baseName);

            eventClients.AddOrUpdate(connectionId, (roomId, uid, displayName), (_, __) => (roomId, uid, displayName));
            roomConnections.AddOrUpdate(roomId,
                _ => new ConcurrentDictionary<string, bool> { [connectionId] = true },
                (_, dict) => { dict[connectionId] = true; return dict; });

            var member = new RoomMemberModel
            {
                room_id = roomId,
                uid = uid,
                connection_id = connectionId,
                base_name = baseName,
                display_name = displayName,
                last_seen = DateTime.UtcNow
            };
            RoomDb.Members.AddOrUpdate(connectionId, member, (_, __) => member);

            long at = room.at_server_time > 0 ? room.at_server_time : ServerNowMs();
            _ = Startup.Nws.SendAsync(connectionId, "watchtogether_joined", displayName, room.state, room.position, at);
            _ = Startup.Nws.SendAsync(connectionId, "watchtogether_sync_update", room.state, room.position, at);

            await BroadcastMembersAsync(roomId);
            await BroadcastNoticeAsync(roomId, connectionId, "joined", displayName);
        }

        public static string[] GetConnectionsInRoom(string roomId)
        {
            if (roomConnections.TryGetValue(roomId, out var dict))
                return dict.Keys.ToArray();
            return Array.Empty<string>();
        }

        static async Task LeaveAsync(string connectionId, string roomId, string uid, string displayName, bool broadcastLeft)
        {
            RoomDb.Members.TryRemove(connectionId, out _);
            if (roomConnections.TryGetValue(roomId, out var conns))
            {
                conns.TryRemove(connectionId, out _);
                if (conns.IsEmpty) roomConnections.TryRemove(roomId, out _);
            }

            if (!RoomDb.Rooms.TryGetValue(roomId, out var room))
                return;

            bool wasHost = !string.IsNullOrEmpty(room.owner_uid) &&
                           !string.IsNullOrEmpty(uid) &&
                           string.Equals(room.owner_uid, uid, StringComparison.Ordinal);

            var remaining = GetConnectionsInRoom(roomId);

            if (wasHost)
            {
                var remainingOthers = remaining.Where(x => x != connectionId).ToArray();
                if (remainingOthers.Length == 0)
                {
                    RoomDb.Rooms.TryRemove(roomId, out _);
                    return;
                }

                var newHostId = remainingOthers[0];
                if (eventClients.TryGetValue(newHostId, out var newHostInfo))
                {
                    room.owner_uid = newHostInfo.uid;
                    room.owner_name = newHostInfo.displayName;
                    room.update_time = DateTime.UtcNow;

                    if (broadcastLeft && !string.IsNullOrEmpty(displayName))
                        await BroadcastNoticeAsync(roomId, connectionId, "left", displayName);

                    var notifyTargets = remainingOthers;
                    var hostTasks = notifyTargets.Select(t => Startup.Nws.SendAsync(t, "watchtogether_host_changed", newHostInfo.uid, newHostInfo.displayName));
                    await Task.WhenAll(hostTasks);

                    var noticeTasks = notifyTargets.Select(t => Startup.Nws.SendAsync(t, "watchtogether_notice", "host_changed", newHostInfo.displayName));
                    await Task.WhenAll(noticeTasks);
                }

                await BroadcastMembersAsync(roomId);
                return;
            }

            bool hasMembers = remaining.Length > 0;
            if (!hasMembers)
            {
                RoomDb.Rooms.TryRemove(roomId, out _);
            }
            else
            {
                if (broadcastLeft && !string.IsNullOrEmpty(displayName))
                    await BroadcastNoticeAsync(roomId, connectionId, "left", displayName);
                await BroadcastMembersAsync(roomId);
            }
        }

        static async Task HandleSyncAsync(string roomId, string senderConnectionId, string state, double position, bool broadcastNotice)
        {
            long atServer = ServerNowMs();

            if (RoomDb.Rooms.TryGetValue(roomId, out var room))
            {
                room.state = state;
                room.position = position;
                room.at_server_time = atServer;
                room.update_time = DateTime.UtcNow;
            }

            var targets = GetConnectionsInRoom(roomId).Where(k => k != senderConnectionId).ToArray();
            if (targets.Length > 0)
            {
                var tasks = targets.Select(t => Startup.Nws.SendAsync(t, "watchtogether_sync_update", state, position, atServer));
                await Task.WhenAll(tasks);
            }

            if (broadcastNotice && eventClients.TryGetValue(senderConnectionId, out var who))
            {
                string verb = state == "paused" ? "paused" : (state == "playing" ? "resumed" : "seeked");
                await BroadcastNoticeAsync(roomId, senderConnectionId, verb, who.displayName);
            }
        }

        static async Task BroadcastUrlChangeAsync(string roomId, string excludeConnectionId, string url, string title)
        {
            var targets = GetConnectionsInRoom(roomId).Where(k => k != excludeConnectionId).ToArray();
            if (targets.Length == 0) return;

            var tasks = targets.Select(t => Startup.Nws.SendAsync(t, "watchtogether_url_change", url, title));
            await Task.WhenAll(tasks);
        }

        static async Task BroadcastNoticeAsync(string roomId, string excludeConnectionId, string verb, string displayName)
        {
            var targets = GetConnectionsInRoom(roomId).Where(k => k != excludeConnectionId).ToArray();
            if (targets.Length == 0) return;

            var tasks = targets.Select(t => Startup.Nws.SendAsync(t, "watchtogether_notice", verb, displayName));
            await Task.WhenAll(tasks);
        }

        static async Task BroadcastMembersAsync(string roomId)
        {
            var targets = GetConnectionsInRoom(roomId);
            if (targets.Length == 0) return;

            var names = targets.Select(t => eventClients.TryGetValue(t, out var info) ? info.displayName : "Unknown").ToArray();
            var tasks = targets.Select(t => Startup.Nws.SendAsync(t, "watchtogether_members", targets.Length, names));
            await Task.WhenAll(tasks);
        }

        static string GetStringArg(JsonElement args, int index)
        {
            if (args.ValueKind != JsonValueKind.Array || args.GetArrayLength() <= index) return null;
            var element = args[index];
            if (element.ValueKind == JsonValueKind.String) return element.GetString();
            if (element.ValueKind == JsonValueKind.Null) return null;
            return element.ToString();
        }

        static double GetDoubleArg(JsonElement args, int index)
        {
            if (args.ValueKind != JsonValueKind.Array || args.GetArrayLength() <= index) return 0;
            var element = args[index];
            if (element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out double val)) return val;
            return 0;
        }

        static long GetLongArg(JsonElement args, int index)
        {
            if (args.ValueKind != JsonValueKind.Array || args.GetArrayLength() <= index) return 0;
            var element = args[index];
            if (element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out long val)) return val;
            return 0;
        }
    }
}
