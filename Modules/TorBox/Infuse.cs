using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TorBox;
public partial class TorBoxController
{
    static bool InfuseVideo(JToken file) => System.Text.RegularExpressions.Regex.IsMatch(FileName(file),@"\.(mkv|mp4|avi|m4v|mov|ts|m2ts|webm|mpg|mpeg|wmv|flv|vob|ogv)$",System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    public class InfuseInput { public long id {get;set;} public long[] files {get;set;} }
    [HttpPost,Route("torbox/infuse/links"),RequestSizeLimit(4096)]
    public Task<ActionResult> InfuseLinks([FromBody] InfuseInput input) => Guard(async(c,key)=>{
        if(input==null || input.id<0 || input.files==null || input.files.Length<1 || input.files.Length>8 ||
            input.files.Any(f=>f<0) || input.files.Distinct().Count()!=input.files.Length)return Error(400,"Некорректный список файлов Infuse");
        var torrent=Rows(await Call("mylist?id="+input.id+"&bypass_cache=true",key)).FirstOrDefault(t=>(long?)t["id"]==input.id);
        if(torrent==null)return Error(404,"Раздача отсутствует в вашем TorBox");
        bool ready=torrent["download_present"]?.Type==JTokenType.Boolean ? (bool)torrent["download_present"] : (bool?)torrent["download_finished"]==true;
        if(!ready)return Error(409,"Раздача ещё не готова в TorBox");
        var files=torrent["files"] as JArray??new JArray();
        if(input.files.Any(id=>!files.Any(f=>(long?)f["id"]==id && InfuseVideo(f))))return Error(400,"Выберите видеофайлы этой раздачи");
        var film=ReadFilm(torrent);
        bool serial=(string)film?["media_type"]=="tv" || SeasonHint((string)torrent["name"])>0 ||
            files.Any(f=>System.Text.RegularExpressions.Regex.IsMatch((string)f["name"]??FileName(f),@"(?<![\p{L}\p{N}])S\d{1,2}[ ._-]*E\d{1,3}|(?<!\d)\d{1,2}x\d{1,3}",System.Text.RegularExpressions.RegexOptions.IgnoreCase));
        var episodes=serial?EpisodeFiles(new Pick{Name=(string)torrent["name"]},torrent):null;
        using var gate=new SemaphoreSlim(2,2);
        // Validate the whole batch before requesting any download links.
        var links=await Task.WhenAll(input.files.Select(async id=>{
            await gate.WaitAsync(HttpContext.RequestAborted);
            try {
                string path="requestdl?token="+Uri.EscapeDataString(key)+"&torrent_id="+input.id+"&file_id="+id+"&redirect=false";
                if(!requestInfo.IsLocalIp && !string.IsNullOrEmpty(requestInfo.IP))path+="&user_ip="+Uri.EscapeDataString(requestInfo.IP);
                string url=(string)await Call(path,key);
                if(!Uri.TryCreate(url,UriKind.Absolute,out var uri) || uri.Scheme!="https")throw new TorBoxException(502,"TorBox вернул некорректную ссылку файла");
                var file=files.First(f=>(long?)f["id"]==id);var episode=episodes?.FirstOrDefault(e=>e.File==id);
                return new{id,url,name=(string)file["name"]??FileName(file),season=(int?)episode?.Season,episode=(int?)episode?.Episode};
            }finally{gate.Release();}
        }));
        return Payload(new{success=true,data=links});
    });
}
