using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace TorBox;
public partial class TorBoxController
{
    // A ticket authorizes only this fixed playlist and its already selected settings.
    // Client-mode credentials exist in memory only; no API key is put in relay URLs.
    class ExternalPlaylist {
        public SeriesSession State;
        public string Access, Uid, ClientKey;
        public int Selected;
        public Pick SelectedPick;
        public long SelectedFile;
        public int Earlier=1, StartSeason;
        public volatile bool Ready;
        public bool ServerMode;
        public DateTime Expires=DateTime.UtcNow.AddHours(12), Limit=DateTime.UtcNow.AddDays(7);
        public ConcurrentDictionary<int,(DateTime Until,string Url)> Streams=new();
    }
    static readonly ConcurrentDictionary<string,ExternalPlaylist> externalPlaylists=new();
    public class ExternalInput { public string session {get;set;} public int index {get;set;} public string preparation {get;set;} }
    static string ExternalHash(string value) => SeriesKeyHash(value??"");
    ExternalPlaylist GetExternal(string access) {
        if(string.IsNullOrEmpty(access) || !System.Text.RegularExpressions.Regex.IsMatch(access,"^[a-f0-9]{64}$") ||
            !externalPlaylists.TryGetValue(ExternalHash(access),out var playlist))throw new TorBoxException(410,"Срок действия плейлиста истёк. Откройте сериал в Lampa заново");
        if(playlist.Expires<DateTime.UtcNow || playlist.Limit<DateTime.UtcNow) {
            externalPlaylists.TryRemove(ExternalHash(access),out _);playlist.ClientKey=null;
            throw new TorBoxException(410,"Срок действия плейлиста истёк. Откройте сериал в Lampa заново");
        }
        return playlist;
    }
    static SeriesEpisode CopyEpisode(SeriesEpisode e) => new(){Pick=e.Pick,File=e.File,Season=e.Season,Episode=e.Episode,Name=e.Name,
        Metadata=e.Metadata?.DeepClone(),ExplicitAudio=e.ExplicitAudio,ExplicitSub=e.ExplicitSub};
    static int NextUncoveredEarlier(SeriesSession state,int from,int before) {
        var covered=state.Episodes.Select(e=>e.Season).ToHashSet();
        while(from<before && covered.Contains(from))from++;
        return from;
    }
    [HttpPost,Route("torbox/series/external"),RequestSizeLimit(4096)]
    public Task<ActionResult> PrepareExternal([FromBody] ExternalInput input) => Guard(async(c,key)=>{
        if(input==null || input.index<0)return Error(400,"Некорректный плейлист");
        ExternalPlaylist external;
        if(string.IsNullOrEmpty(input.preparation)) {
            foreach(var item in externalPlaylists.Where(x=>x.Value.Expires<DateTime.UtcNow || x.Value.Limit<DateTime.UtcNow).ToArray()) {
                if(externalPlaylists.TryRemove(item.Key,out var expired))expired.ClientKey=null;
            }
            if(externalPlaylists.Count>=128)return Error(429,"Слишком много внешних плейлистов. Попробуйте позднее");
            var source=GetSeries(input.session,key);
            if(!await source.Gate.WaitAsync(TimeSpan.FromSeconds(2),HttpContext.RequestAborted))return Error(429,"Плейлист ещё подготавливается");
            try {
                if(input.index>=source.Episodes.Count)return Error(400,"Серия отсутствует в плейлисте");
                var selected=source.Episodes[input.index];
                var episodes=source.Episodes.Select(CopyEpisode).ToList();
                // A multi-season pack can include seasons preceding the chosen start.
                var first=source.Episodes.First().Pick;
                int firstSeason=episodes.First().Season;
                episodes.InsertRange(0,EpisodeFiles(first,first.Torrent).Where(e=>e.Season<firstSeason).Select(CopyEpisode).ToArray());
                var state=new SeriesSession{Owner=source.Owner,Title=source.Title,Selection=source.Selection,KeyHash=source.KeyHash,
                    Resolution=source.Resolution,Through=source.Through,Audio=source.Audio?.DeepClone(),Subtitle=source.Subtitle?.DeepClone(),
                    Episodes=episodes,Candidates=source.Candidates.ToList(),Used=new(source.Used,StringComparer.OrdinalIgnoreCase)};
                if(state.Episodes.Count>5000)return Error(409,"Слишком большой плейлист сериала");
                string access=Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
                external=new ExternalPlaylist{State=state,Access=access,Uid=requestInfo.user_uid,ClientKey=serverCredentials?null:key,ServerMode=serverCredentials,
                    SelectedPick=selected.Pick,SelectedFile=selected.File,StartSeason=state.Episodes.Min(e=>e.Season),
                    Selected=state.Episodes.FindIndex(e=>e.Pick==selected.Pick && e.File==selected.File)};
                externalPlaylists[ExternalHash(access)]=external;
            }finally{source.Gate.Release();}
        } else {
            external=GetExternal(input.preparation);
            if(external.State.Owner!=metadataOwner || external.State.KeyHash!=SeriesKeyHash(key) || external.Uid!=requestInfo.user_uid || external.ServerMode!=serverCredentials)
                return Error(403,"Плейлист принадлежит другому аккаунту");
        }
        if(!await external.State.Gate.WaitAsync(TimeSpan.FromSeconds(2),HttpContext.RequestAborted))return Error(429,"Плейлист ещё подготавливается");
        try {
            external.Expires=DateTime.UtcNow.AddHours(12);
            if(!external.Ready) {
                if(external.State.Episodes.Count>=5000)return Error(409,"Достигнут предел плейлиста сериала");
                external.Earlier=NextUncoveredEarlier(external.State,external.Earlier,external.StartSeason);
                if(external.Earlier<external.StartSeason) {
                    int through=external.State.Through;
                    external.State.Through=external.Earlier-1;
                    try{await AppendSeriesSeason(external.State,c,key,external.State.Episodes.Count-1);}
                    finally{external.State.Through=Math.Max(through,external.State.Through);}
                    external.Earlier=NextUncoveredEarlier(external.State,external.Earlier+1,external.StartSeason);
                    return Payload(new{success=true,pending=true,preparation=external.Access,episodes=external.State.Episodes.Count});
                }
                var next=await AppendSeriesSeason(external.State,c,key,external.State.Episodes.Count-1);
                if(next.Index.HasValue)return Payload(new{success=true,pending=true,preparation=external.Access,episodes=external.State.Episodes.Count});
                // Preserve the selected release when earlier packs overlap its episodes.
                external.State.Episodes=external.State.Episodes.GroupBy(e=>(e.Season,e.Episode)).Select(g=>g.First())
                    .OrderBy(e=>e.Season).ThenBy(e=>e.Episode).ToList();
                external.Selected=external.State.Episodes.FindIndex(e=>e.Pick==external.SelectedPick && e.File==external.SelectedFile);
                external.Ready=true;
            }
            return Payload(new{success=true,pending=false,selected_index=external.Selected,
                playlist=external.State.Episodes.Select((e,i)=>new{index=i,title=EpisodeTitle(e),season=e.Season,episode=e.Episode,
                    url=host+"/torbox/series/external/"+external.Access+"/"+i+".m3u8"}).ToArray()});
        }finally{external.State.Gate.Release();}
    });
    [HttpGet,HttpHead,AllowAnonymous,Route("torbox/series/external/{access}/{index:int}.m3u8")]
    public async Task<ActionResult> ExternalSeriesStream(string access,int index) {
        Response.Headers["Cache-Control"]="no-store";
        Response.Headers["Referrer-Policy"]="no-referrer";
        try {
            var external=GetExternal(access);
            if(!external.Ready)return Error(409,"Плейлист ещё подготавливается");
            if(index<0 || index>=external.State.Episodes.Count)return Error(404,"Серия отсутствует в плейлисте");
            var c=Config();
            if((c["allowed_uids"] as JArray)?.Any(x=>string.Equals((string)x,external.Uid,StringComparison.OrdinalIgnoreCase))!=true)
                return Error(403,"Нет доступа к TorBox для этого аккаунта Lampac");
            if(serverCredentials!=external.ServerMode)return Error(410,"Настройки TorBox изменились. Откройте плейлист заново");
            string key=serverCredentials?((string)c["api_key"]??"").Trim():external.ClientKey;
            if(string.IsNullOrEmpty(key) || SeriesKeyHash(key)!=external.State.KeyHash)return Error(410,"Ключ TorBox изменился. Откройте плейлист заново");
            if(!await external.State.Gate.WaitAsync(TimeSpan.FromSeconds(10),HttpContext.RequestAborted))return Error(429,"Поток ещё подготавливается");
            try {
                external.Expires=DateTime.UtcNow.AddHours(12);
                if(!external.Streams.TryGetValue(index,out var cached) || cached.Until<DateTime.UtcNow) {
                    var stream=await ResolveSeriesStream(external.State,index,key);
                    cached=(DateTime.UtcNow.AddSeconds(30),stream.Url);external.Streams[index]=cached;
                }
                return Redirect(cached.Url);
            }finally{external.State.Gate.Release();}
        }
        catch(TorBoxException ex){return Error(ex.Status,ex.Message);}
        catch(System.Net.Http.HttpRequestException){return Error(502,"Не удалось связаться с TorBox");}
        catch(TaskCanceledException){return Error(504,"TorBox не ответил вовремя");}
        catch{return Error(502,"Не удалось подготовить поток TorBox");}
    }
}
