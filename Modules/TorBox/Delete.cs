using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TorBox;
public partial class TorBoxController
{
    class DeleteConfirmation {
        public string Ticket,Owner,KeyHash;
        public long Id;
        public JToken Torrent;
        public bool Confirmed,MetadataRemoved;
        public DateTime CheckAfter=DateTime.UtcNow.AddMinutes(1);
        public DateTime Expires=DateTime.UtcNow.AddDays(7);
    }
    static readonly ConcurrentDictionary<string,DeleteConfirmation> deleteConfirmations=new();
    public class DeleteInput { public long? id {get;set;} public bool confirmed {get;set;} }
    public class DeleteCheckInput { public string ticket {get;set;} public long? id {get;set;} public string scope {get;set;} }
    string DeletionScope(string key) => SeriesKeyHash(metadataOwner+"|"+SeriesKeyHash(key));
    ActionResult AcceptedDeletion(DeleteConfirmation pending,string key) => Payload(new{
        success=true,id=pending.Id,accepted=true,deletion_confirmed=false,pending=true,ticket=pending.Ticket,
        scope=DeletionScope(key),check_after=new DateTimeOffset(pending.CheckAfter).ToUnixTimeMilliseconds(),metadata_removed=false});
    bool DeletionAbsent(JToken data,long id) {
        if(data is not JArray && data is not JObject)throw new TorBoxException(502,"Не удалось проверить удаление");
        var rows=Rows(data);
        if(rows.Any(t=>t is not JObject || !((long?)t["id"]).HasValue))throw new TorBoxException(502,"TorBox вернул некорректную проверку удаления");
        return !rows.Any(t=>(long?)t["id"]==id);
    }
    async Task<ActionResult> ConfirmDeletion(DeleteConfirmation pending,string key) {
        if(!pending.Confirmed && pending.CheckAfter>DateTime.UtcNow)return AcceptedDeletion(pending,key);
        if(!pending.Confirmed) {
            bool gone=false;
            using var budget=CancellationTokenSource.CreateLinkedTokenSource(HttpContext.RequestAborted);
            budget.CancelAfter(TimeSpan.FromSeconds(4));
            try {
                for(int attempt=0;attempt<3;attempt++) {
                    var data=await Call("mylist?id="+pending.Id+"&bypass_cache=true",key,cancellationToken:budget.Token,allowMissing:true);
                    if(DeletionAbsent(data,pending.Id)){gone=true;break;}
                    if(attempt<2)await Task.Delay(250*(attempt+1),budget.Token);
                }
            }
            catch(Exception) when(!HttpContext.RequestAborted.IsCancellationRequested) {
                // The mutation was accepted. A failed check is pending, not a failed delete.
            }
            if(!gone)return Payload(new{success=true,id=pending.Id,deletion_confirmed=false,pending=false,metadata_removed=false});
            pending.Confirmed=true;pending.MetadataRemoved=true;
            InvalidateOwned(key,pending.Id);
            try{RemoveFilm(pending.Torrent);}catch(Exception ex){pending.MetadataRemoved=false;MetadataWarning("delete",ex);}
        }
        return Payload(new{success=true,id=pending.Id,accepted=true,deletion_confirmed=true,pending=false,metadata_removed=pending.MetadataRemoved});
    }
    [HttpPost,Route("torbox/delete"),RequestSizeLimit(4096)]
    public Task<ActionResult> DeleteTorrent([FromBody] DeleteInput input) => Guard(async(c,key)=>{
        if(input?.id==null || input.id<0 || !input.confirmed)return Error(400,"Подтвердите удаление выбранной раздачи");
        if(!await addGate.WaitAsync(TimeSpan.FromSeconds(3),HttpContext.RequestAborted))return Error(429,"Другая операция TorBox ещё выполняется");
        try {
            foreach(var expired in deleteConfirmations.Where(x=>x.Value.Expires<DateTime.UtcNow).Select(x=>x.Key).ToArray())deleteConfirmations.TryRemove(expired,out _);
            string digest=SeriesKeyHash(key);
            var pending=deleteConfirmations.Values.FirstOrDefault(x=>!x.Confirmed && x.Owner==metadataOwner && x.KeyHash==digest && x.Id==input.id.Value);
            if(pending!=null)return AcceptedDeletion(pending,key);
            if(deleteConfirmations.Count>=128) {
                foreach(var done in deleteConfirmations.Where(x=>x.Value.Confirmed).Select(x=>x.Key).ToArray())deleteConfirmations.TryRemove(done,out _);
                if(deleteConfirmations.Count>=128)return Error(429,"Слишком много ожидающих подтверждения удалений");
            }
            var torrent=Rows(await Call("mylist?id="+input.id+"&bypass_cache=true",key)).FirstOrDefault(t=>(long?)t["id"]==input.id.Value);
            if(torrent==null)return Error(404,"Раздача уже отсутствует в вашем TorBox. Обновите список");
            using var body=new System.Net.Http.StringContent(Newtonsoft.Json.JsonConvert.SerializeObject(new{torrent_id=input.id.Value,operation="delete",all=false}),System.Text.Encoding.UTF8,"application/json");
            await Call("controltorrent",key,body);
            pending=new DeleteConfirmation{Ticket=Guid.NewGuid().ToString("N"),Owner=metadataOwner,KeyHash=digest,Id=input.id.Value,Torrent=torrent.DeepClone()};
            deleteConfirmations[pending.Ticket]=pending;InvalidateOwned(key,pending.Id);
            return AcceptedDeletion(pending,key);
        }finally{addGate.Release();}
    });
    [HttpPost,Route("torbox/delete/check"),RequestSizeLimit(4096)]
    public Task<ActionResult> CheckDeletion([FromBody] DeleteCheckInput input) => Guard(async(c,key)=>{
        if(input?.ticket==null || !deleteConfirmations.TryGetValue(input.ticket,out var pending) || pending.Expires<DateTime.UtcNow ||
            pending.Owner!=metadataOwner || pending.KeyHash!=SeriesKeyHash(key)) {
            // A restart loses in-memory tickets. Verify read-only with the current account scope.
            if(input?.id==null || input.id<0 || input.scope!=DeletionScope(key))return Error(410,"Подтверждение удаления устарело. Обновите список");
            var data=await Call("mylist?id="+input.id+"&bypass_cache=true",key,allowMissing:true);
            return Payload(new{success=true,id=input.id,deletion_confirmed=DeletionAbsent(data,input.id.Value),pending=false});
        }
        if(!await addGate.WaitAsync(TimeSpan.FromSeconds(3),HttpContext.RequestAborted))return Error(429,"Другая операция TorBox ещё выполняется");
        try{return await ConfirmDeletion(pending,key);}finally{addGate.Release();}
    });
}
