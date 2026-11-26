using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace TorBox;
public partial class TorBoxController
{
    public class AirlockInput { public long? id {get;set;} public bool? enabled {get;set;} public string scope {get;set;} }

    [HttpGet,Route("torbox/airlock/info")]
    public Task<ActionResult> AirlockInfo() => Guard(async(c,key)=>{
        var data=await Call("../user/stats?general=true&bandwidth=false",key);
        var stats=data["general"] as JObject;
        if(stats?["airlocked_downloads"]?.Type!=JTokenType.Integer || stats?["airlock_storage_limit"]?.Type!=JTokenType.Integer)
            return Error(502,"TorBox не сообщил объём Airlock");
        long used=(long)stats["airlocked_downloads"],limit=(long)stats["airlock_storage_limit"];
        if(used<0 || limit<0)return Error(502,"TorBox сообщил некорректный объём Airlock");
        return Payload(new{success=true,scope=DeletionScope(key),used_bytes=used,limit_bytes=limit,remaining_bytes=Math.Max(0,limit-used)});
    });

    [HttpPost,Route("torbox/airlock"),RequestSizeLimit(4096)]
    public Task<ActionResult> SetAirlock([FromBody] AirlockInput input) => Guard(async(c,key)=>{
        if(input?.id==null || input.id<0 || !input.enabled.HasValue)return Error(400,"Укажите раздачу и состояние Airlock");
        if(input.scope!=DeletionScope(key))return Error(409,"Аккаунт TorBox изменился. Обновите список");
        if(!await addGate.WaitAsync(TimeSpan.FromSeconds(3),HttpContext.RequestAborted))return Error(429,"Другая операция TorBox ещё выполняется");
        try {
            var torrent=Rows(await Call("mylist?id="+input.id+"&bypass_cache=true",key)).FirstOrDefault(t=>(long?)t["id"]==input.id);
            if(torrent==null)return Error(404,"Раздача отсутствует в вашем TorBox");
            if(torrent["airlocked"]?.Type!=JTokenType.Boolean)return Error(502,"TorBox не сообщил состояние Airlock. Обновите список");
            if((bool)torrent["airlocked"]==input.enabled.Value)return Payload(new{success=true,confirmed=true,id=input.id,airlocked=input.enabled.Value});
            if((bool?)torrent["download_present"]!=true && (bool?)torrent["cached"]!=true)return Error(409,"Airlock доступен только для готовой раздачи в кэше TorBox");
            using var body=new StringContent(Newtonsoft.Json.JsonConvert.SerializeObject(new{torrent_id=input.id.Value,airlocked=input.enabled.Value}),System.Text.Encoding.UTF8,"application/json");
            await Call("edittorrent",key,body,method:HttpMethod.Put);
            InvalidateOwned(key,input.id);
            // Do not claim that a successful PUT has already changed the visible state.
            using var budget=CancellationTokenSource.CreateLinkedTokenSource(HttpContext.RequestAborted);
            budget.CancelAfter(TimeSpan.FromSeconds(4));
            try {
                for(int attempt=0;attempt<3;attempt++) {
                    var rows=Rows(await Call("mylist?id="+input.id+"&bypass_cache=true",key,cancellationToken:budget.Token));
                    var updated=rows.FirstOrDefault(t=>(long?)t["id"]==input.id);
                    if(updated?["airlocked"]?.Type==JTokenType.Boolean && (bool)updated["airlocked"]==input.enabled.Value)
                        return Payload(new{success=true,confirmed=true,id=input.id,airlocked=input.enabled.Value});
                    if(attempt<2)await Task.Delay(250*(attempt+1),budget.Token);
                }
            } catch(Exception) when(!HttpContext.RequestAborted.IsCancellationRequested) { }
            return Payload(new{success=true,confirmed=false,id=input.id,requested=input.enabled.Value});
        }finally{addGate.Release();}
    });
}
