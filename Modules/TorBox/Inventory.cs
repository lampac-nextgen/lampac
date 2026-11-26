using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TorBox;
public partial class TorBoxController
{
    class InventoryEntry {
        public readonly SemaphoreSlim Gate=new(1,1);
        public JArray Rows;
        public DateTime Until;
        public DateTime LastUsed=DateTime.UtcNow;
        public int Generation;
    }
    static readonly object inventoryLock=new();
    static readonly Dictionary<string,InventoryEntry> inventories=new();
    async Task<JArray> OwnedTorrents(string key) {
        string digest=SeriesKeyHash(key);
        InventoryEntry entry;
        lock(inventoryLock) {
            if(!inventories.TryGetValue(digest,out entry)) {
                foreach(var expired in inventories.Where(x=>x.Value.LastUsed<DateTime.UtcNow.AddMinutes(-1) && x.Value.Gate.CurrentCount==1).Select(x=>x.Key).ToArray())inventories.Remove(expired);
                if(inventories.Count>=128)throw new TorBoxException(429,"Слишком много активных аккаунтов TorBox. Попробуйте позднее");
                entry=new InventoryEntry();inventories[digest]=entry;
            }
            entry.LastUsed=DateTime.UtcNow;
        }
        if(!await entry.Gate.WaitAsync(TimeSpan.FromSeconds(22),HttpContext.RequestAborted))throw new TorBoxException(429,"Список TorBox ещё обновляется");
        try {
            if(entry.Rows!=null && entry.Until>DateTime.UtcNow)return entry.Rows;
            for(int attempt=0;attempt<2;attempt++) {
                int generation=Volatile.Read(ref entry.Generation);
                var result=new JArray();var seen=new HashSet<long>();bool complete=false;
                const int pageSize=1000;
                for(int offset=0;offset<100000;offset+=pageSize) {
                    var data=await Call("mylist?limit="+pageSize+"&offset="+offset+"&bypass_cache=true",key);
                    if(data is not JArray page)throw new TorBoxException(502,"TorBox вернул некорректную страницу списка");
                    int added=0;
                    foreach(var row in page) {
                        long? id=(long?)row["id"];
                        if(!id.HasValue || id<0)throw new TorBoxException(502,"TorBox вернул некорректный список раздач");
                        if(seen.Add(id.Value)){result.Add(row.DeepClone());added++;}
                    }
                    if(page.Count<pageSize){complete=true;break;}
                    if(added==0)throw new TorBoxException(502,"TorBox повторил страницу списка. Повторите запрос позднее");
                }
                if(!complete)throw new TorBoxException(502,"Список TorBox превышает поддерживаемый размер");
                lock(inventoryLock) {
                    if(generation==entry.Generation) {
                        entry.Rows=result;entry.Until=DateTime.UtcNow.AddSeconds(15);
                        return result; // Immutable snapshot; callers never modify its rows.
                    }
                }
            }
            throw new TorBoxException(429,"Список TorBox изменился во время чтения. Повторите выбор");
        }finally{entry.Gate.Release();}
    }
    static void InvalidateOwned(string key,long? id=null) {
        string digest=SeriesKeyHash(key);
        lock(inventoryLock) {
            if(inventories.TryGetValue(digest,out var entry)) {
                Interlocked.Increment(ref entry.Generation);entry.Until=DateTime.MinValue;
            }
        }
        foreach(var pick in picks.Values.Where(p=>p.ReadyKeyHash==digest && (!id.HasValue || p.Id==id || (long?)p.ReadyTorrent?["id"]==id)))pick.ReadyUntil=DateTime.MinValue;
    }
    static void BindReady(Pick pick,JToken torrent,string key) {
        string digest=SeriesKeyHash(key);long? id=(long?)torrent["id"];
        if((pick.BoundKeyHash!=null && pick.BoundKeyHash!=digest) || (pick.Id.HasValue && pick.Id!=id)) {
            pick.Metadata.Clear();pick.Remembered=false;
        }
        pick.Id=id;pick.BoundKeyHash=digest;pick.Torrent=torrent.DeepClone();
    }
}
