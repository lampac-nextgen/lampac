using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using Shared.Models.Templates;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace TorBox;
public partial class TorBoxController
{
    class Pick {
        public string Owner, Link, Hash, Name, Movie, Next;
        public int Quality, Seeds;
        public bool Serial;
        public Pick[] SeriesPicks;
        public JToken Torrent,ReadyTorrent;
        public string ReadyTicket,ReadyKeyHash,BoundKeyHash;
        public DateTime ReadyUntil;
        public bool Remembered;
        public long? Id;
        public FilmInput Film;
        public DateTime Expires = DateTime.UtcNow.AddHours(6);
        public SemaphoreSlim Gate = new(1,1);
        public ConcurrentDictionary<long,JToken> Metadata = new();
    }
    static readonly ConcurrentDictionary<string,Pick> picks = new();
    static readonly ConcurrentDictionary<string,(DateTime Expires,JArray Rows)> searchCache = new();

    ActionResult Similar(SimilarTpl t, params (int Season,int Episode)[] frames) {
        string html=t.ToHtml();
        if(frames.Length>0){
            int index=0;
            html=Regex.Replace(html,"data-json='\\{",m=>{
                var frame=frames[Math.Min(index++,frames.Length-1)];
                return "data-json='{\"torbox_preview\":true,\"season\":"+frame.Season+",\"episode\":"+frame.Episode+",";
            });
        }
        return Content(html,"text/html; charset=utf-8");
    }
    string OnlineUrl(string path) => host + "/lite/torbox/" + path;
    Pick GetPick(string ticket) {
        if (ticket == null || !picks.TryGetValue(ticket,out var p) || p.Expires < DateTime.UtcNow || p.Owner != metadataOwner)
            throw new TorBoxException(410,"Список устарел. Откройте TorBox заново из карточки фильма");
        return p;
    }
    static int Resolution(JToken t) {
        string s = ((string)t["Title"] ?? "").ToLowerInvariant();
        if(Regex.IsMatch(s,"2160|\\b4k\\b|\\buhd\\b"))return 2160;
        if(s.Contains("1440"))return 1440;
        if(s.Contains("1080"))return 1080;
        if(s.Contains("720"))return 720;
        if(s.Contains("576"))return 576;
        if(s.Contains("480"))return 480;
        return 360;
    }
    static string Normalize(string s) => Regex.Replace((s??"").ToLowerInvariant(),"[^\\p{L}\\p{N}]+", " ").Trim();
    static bool VideoName(string name) => Regex.IsMatch(name??"",@"\.(mkv|mp4|avi|m4v|mov|ts|m2ts|webm|mpg|mpeg|wmv)$",RegexOptions.IgnoreCase) && !Regex.IsMatch(name??"",@"\b(sample|trailer|трейлер)\b",RegexOptions.IgnoreCase);
    static string FileName(JToken f) => (string)f["short_name"] ?? (string)f["name"] ?? "Видео";

    [HttpGet, Route("lite/torbox")]
    public Task<ActionResult> Online(string title, string original_title, int year=0, byte serial=0, bool rjson=true, long id=0, long tmdb_id=0, string source=null) => Guard(async(c,key)=>{
        if(string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(original_title))return Error(400,"Не указано название фильма");
        string searchKey=Normalize(title)+":"+Normalize(original_title)+":"+year+":"+serial;
        JArray rows;
        if(searchCache.TryGetValue(searchKey,out var hit) && hit.Expires>DateTime.UtcNow)rows=hit.Rows;
        else {
            var parser=c["jacred"] as JObject;
            if(parser==null || !Uri.TryCreate((string)parser["base_url"],UriKind.Absolute,out var baseUri))return Error(503,"JacRed для TorBox не настроен");
            string url=baseUri.ToString().TrimEnd('/')+"/api/v2.0/indexers/all/results?title="+Uri.EscapeDataString(title??original_title)+"&title_original="+Uri.EscapeDataString(original_title??title)+"&year="+(serial==1?0:year)+"&is_serial="+(serial==1?2:1)+"&apikey="+Uri.EscapeDataString((string)parser["api_key"]??"");
            using var response=await client.GetAsync(url,HttpContext.RequestAborted);
            if(!response.IsSuccessStatusCode)return Error(502,"JacRed не ответил на поиск");
            var parsed=JObject.Parse(await response.Content.ReadAsStringAsync());
            rows=parsed["Results"] as JArray ?? new JArray();
            if(searchCache.Count>256)searchCache.Clear();
            searchCache[searchKey]=(DateTime.UtcNow.AddMinutes(5),rows);
        }
        var candidates=rows.Where(t=>!string.IsNullOrWhiteSpace((string)t["Title"]) &&
            ((!string.IsNullOrWhiteSpace(title) && Normalize((string)t["Title"]).Contains(Normalize(title))) ||
             (!string.IsNullOrWhiteSpace(original_title) && Normalize((string)t["Title"]).Contains(Normalize(original_title)))) &&
            !Regex.IsMatch((string)t["Title"],@"\b(CAMRip|HDCAM|HDTS|TSRip|TELESYNC)\b",RegexOptions.IgnoreCase) &&
            (serial==1 || year<=0 || !Regex.IsMatch((string)t["Title"],@"\b(19|20)\d{2}\b") || Regex.IsMatch((string)t["Title"],@"\b"+year+@"\b")))
            .OrderByDescending(Resolution).ThenByDescending(t=>(int?)t["Seeders"]??0).Take(serial==1?96:12).ToArray();
        if(candidates.Length==0)return Error(404,"JacRed не нашёл подходящих раздач фильма");
        foreach(var expired in picks.Where(p=>p.Value.Expires<DateTime.UtcNow).ToArray())picks.TryRemove(expired.Key,out _);
        if(picks.Count+candidates.Length>512)return Error(429,"Слишком много открытых подборок. Попробуйте позднее");
        var tpl=new SimilarTpl();
        string previous=null;
        var seriesPicks=new List<Pick>();
        for(int i=0;i<candidates.Length;i++) {
            var t=candidates[i];string link=(string)t["MagnetUri"] ?? (string)t["Link"];
            if(string.IsNullOrWhiteSpace(link))continue;
            string hash=Regex.Match(Uri.UnescapeDataString(link),@"urn:btih:([a-z0-9]+)",RegexOptions.IgnoreCase).Groups[1].Value;
            if(string.IsNullOrEmpty(hash))hash=(string)t["InfoHash"]??(string)t["Hash"]??"";
            hash=hash.ToLowerInvariant();
            string ticket=Guid.NewGuid().ToString("N");
            var p=new Pick{Owner=metadataOwner,Link=link,Hash=hash,Name=(string)t["Title"],Movie=title??original_title,Serial=serial==1,Quality=Resolution(t),Seeds=(int?)t["Seeders"]??0,
                Film=new FilmInput{tmdb_id=tmdb_id>0 ? tmdb_id : (source=="tmdb" || source=="cub" ? id : 0),title=string.IsNullOrWhiteSpace(title)?original_title:title,original_title=original_title,year=year,media_type=serial==1?"tv":"movie"}};
            picks[ticket]=p;seriesPicks.Add(p);
            if(previous!=null)picks[previous].Next=ticket;
            previous=ticket;
            tpl.Append(p.Name,year.ToString(),p.Quality+"p · сидов "+p.Seeds,OnlineUrl("choose?pick="+ticket));
        }
        if(serial==1){var peers=seriesPicks.ToArray();foreach(var p in peers)p.SeriesPicks=peers;}
        return Similar(tpl,(0,0));
    });

    [HttpGet, Route("lite/torbox/choose")]
    public Task<ActionResult> Choose(string pick, long file=-1, int audio=-1, int sub=-2, int resolution=-1) => Guard(async(c,key)=>{
        var requested=GetPick(pick);
        if(!await requested.Gate.WaitAsync(TimeSpan.FromSeconds(2),HttpContext.RequestAborted))return Error(429,"TorBox уже обрабатывает эту раздачу");
        (string Ticket, Pick Selection, JToken Torrent)? ready;
        try {
            ready=await FindReadyFrom(pick,c,key);
            if(ready!=null && !ready.Value.Selection.Remembered)ready.Value.Selection.Remembered=RememberFilm(ready.Value.Torrent,ready.Value.Selection.Film);
        }
        finally { requested.Gate.Release(); }
        if(ready==null)return Error(404,"В выбранной и следующих раздачах нет готового видео в кэше TorBox. Загрузки не запускались");
        var p=ready.Value.Selection;
        var t=ready.Value.Torrent;
        if(pick!=ready.Value.Ticket){file=-1;audio=-1;sub=-2;resolution=-1;}
        pick=ready.Value.Ticket;
        var files=(t["files"] as JArray??new JArray()).Where(f=>VideoName(FileName(f))).OrderByDescending(f=>(long?)f["size"]??0).ToArray();
        if(files.Length==0)return Error(404,"В раздаче нет видеофайлов");
        if(p.Serial){
            p.Torrent=t.DeepClone();
            var episodes=EpisodeFiles(p,t);
            if(file<0){
                var episodeTpl=new SimilarTpl();
                foreach(var episode in episodes)episodeTpl.Append(EpisodeTitle(episode),null,null,OnlineUrl("choose?pick="+pick+"&file="+episode.File));
                return Similar(episodeTpl,episodes.Select(e=>(e.Season,e.Episode)).ToArray());
            }
            if(!episodes.Any(e=>e.File==file))return Error(400,"Выбранный файл не распознан как серия");
        }
        if(file<0)file=(long?)files[0]["id"]??-1;
        var frame=p.Serial?EpisodeFiles(p,t).FirstOrDefault(e=>e.File==file):null;
        if(!files.Any(f=>(long?)f["id"]==file))return Error(404,"Видеофайл отсутствует в раздаче");
        if(!p.Metadata.TryGetValue(file,out var data)){
            data=await StreamData(key,p.Id.Value,file,0,-1);p.Metadata[file]=data;
        }
        var audios=data["metadata"]?["audios"] as JArray??new JArray();
        var subtitles=data["metadata"]?["subtitles"] as JArray??new JArray();
        string route="choose?pick="+pick+"&file="+file;
        var tpl=new SimilarTpl();
        var video=data["metadata"]?["video"];
        int width=(int?)video?["width"]??0,height=(int?)video?["height"]??0;
        var resolutions=new[]{1440,1080,720,480,360}.Where(x=>x<height).ToArray();
        if(resolution==-1){
            tpl.Append("Разрешение: исходное",null,width+" × "+height,OnlineUrl(route+"&resolution=0"));
            foreach(int res in resolutions)tpl.Append("Разрешение: "+res+"p",null,"TorBox",OnlineUrl(route+"&resolution="+res));
            if(!p.Serial && files.Length>1)foreach(var f in files.Where(f=>(long?)f["id"]!=file))tpl.Append("Другой файл: "+FileName(f),null,null,OnlineUrl("choose?pick="+pick+"&file="+f["id"]));
            return Similar(tpl,(frame?.Season??0,frame?.Episode??0));
        }
        if(resolution!=0 && !resolutions.Contains(resolution))return Error(400,"Разрешение недоступно для этого файла");
        route+="&resolution="+resolution;
        if(audio==-1){
            var choices=audios.Select((a,i)=>new{Track=a,Index=i}).OrderByDescending(x=>((string)x.Track["language"]??"").StartsWith("ru")).ThenByDescending(x=>(bool?)x.Track["default"]==true);
            foreach(var x in choices)tpl.Append("Аудио: "+TrackLabel(x.Track),null,(string)x.Track["codec"]+" · каналов: "+(string)x.Track["channels"],OnlineUrl(route+"&audio="+x.Index));
            if(audios.Count==0)tpl.Append("Аудио: по умолчанию",null,null,OnlineUrl(route+"&audio=0"));
            return Similar(tpl,(frame?.Season??0,frame?.Episode??0));
        }
        if(audio<0 || audio>=Math.Max(1,audios.Count))return Error(400,"Звуковая дорожка отсутствует");
        route+="&audio="+audio;
        if(sub==-2){
            tpl.Append("Без субтитров",null,null,OnlineUrl(route+"&sub=-1"));
            for(int i=0;i<subtitles.Count;i++)tpl.Append("Субтитры: "+TrackLabel(subtitles[i]),null,(string)subtitles[i]["codec"],OnlineUrl(route+"&sub="+i));
            return Similar(tpl,(frame?.Season??0,frame?.Episode??0));
        }
        if(sub < -1 || sub>=subtitles.Count)return Error(400,"Дорожка субтитров отсутствует");
        var movie=new MovieTpl(p.Movie,p.Movie);
        string selection=(resolution==0?"Исходное разрешение":resolution+"p")+" · "+(audios.Count>audio?TrackLabel(audios[audio]):"Звук по умолчанию")+" · "+(sub<0?"Без субтитров":TrackLabel(subtitles[sub]));
        movie.Append(string.IsNullOrWhiteSpace(p.Movie)?p.Name:p.Movie,OnlineUrl("play?pick="+pick+"&file="+file+"&resolution="+resolution+"&audio="+audio+"&sub="+sub),method:"call",voice_name:selection,hls_manifest_timeout:60000);
        return Content(movie.ToHtml(),"text/html; charset=utf-8");
    });
    static bool CachedReady(JToken t) =>
        (t?["download_present"]?.Type==JTokenType.Boolean ? (bool)t["download_present"] : (bool?)t?["download_finished"]==true) &&
        (t?["files"] as JArray)?.Any(f=>VideoName(FileName(f)))==true;

    async Task<(string Ticket, Pick Selection, JToken Torrent)?> FindReadyFrom(string ticket,JObject config,string key)
    {
        var requested=GetPick(ticket);string digest=SeriesKeyHash(key);
        if(requested.ReadyKeyHash==digest && requested.ReadyUntil>DateTime.UtcNow && requested.ReadyTorrent!=null) {
            var selected=GetPick(requested.ReadyTicket);
            if(selected.BoundKeyHash==digest && selected.Id==(long?)requested.ReadyTorrent["id"])return(requested.ReadyTicket,selected,requested.ReadyTorrent);
        }
        var chain=new List<(string Ticket,Pick Selection)>();
        for(int i=0;ticket!=null && i<96;i++) {
            var candidate=GetPick(ticket);var first=chain.FirstOrDefault().Selection;
            if(first==null || !first.Serial || SeasonHint(first.Name)==0 || SeasonHint(candidate.Name)==SeasonHint(first.Name))chain.Add((ticket,candidate));
            ticket=candidate.Next;
        }
        var owned=await OwnedTorrents(key);
        var hashes=chain.Select(x=>x.Selection.Hash).Where(h=>Regex.IsMatch(h??"","^([a-f0-9]{40}|[a-z2-7]{32})$",RegexOptions.IgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        JObject cache=null;bool checkedCache=false;
        foreach(var entry in chain) {
            var p=entry.Selection;
            var existing=owned.FirstOrDefault(t=>((p.Id.HasValue && (long?)t["id"]==p.Id) ||
                (!string.IsNullOrEmpty(p.Hash) && string.Equals((string)t["hash"],p.Hash,StringComparison.OrdinalIgnoreCase))) && CachedReady(t));
            JToken torrent=existing;
            if(torrent==null) {
                if(!string.IsNullOrEmpty(p.Hash)) {
                    if(!checkedCache) {cache=hashes.Length>0?await Call("checkcached?hash="+Uri.EscapeDataString(string.Join(",",hashes))+"&format=object",key) as JObject:null;checkedCache=true;}
                    var cached=cache?.Properties().FirstOrDefault(x=>string.Equals(x.Name,p.Hash,StringComparison.OrdinalIgnoreCase))?.Value as JObject;
                    if(cached==null || !cached.HasValues || (bool?)cached["cached"]==false)continue;
                }
                torrent=await AttachCachedOnly(p,config,key);
            }
            if(torrent==null)continue;
            BindReady(p,torrent,key);
            requested.ReadyTicket=entry.Ticket;requested.ReadyKeyHash=digest;requested.ReadyTorrent=torrent.DeepClone();requested.ReadyUntil=DateTime.UtcNow.AddSeconds(30);
            return(entry.Ticket,p,requested.ReadyTorrent);
        }
        return null;
    }

    async Task<JToken> AttachCachedOnly(Pick p,JObject config,string key)
    {
        using var form=new MultipartFormDataContent();
        form.Add(new StringContent("true"),"add_only_if_cached");
        form.Add(new StringContent("false"),"as_queued");
        form.Add(new StringContent("false"),"allow_zip");
        if(!string.IsNullOrEmpty(p.Hash))form.Add(new StringContent("magnet:?xt=urn:btih:"+p.Hash),"magnet");
        else if(p.Link.StartsWith("magnet:?",StringComparison.OrdinalIgnoreCase))form.Add(new StringContent(p.Link),"magnet");
        else{
            if(!Uri.TryCreate(p.Link,UriKind.Absolute,out var source) || !HostAllowed(config,source))return null;
            var file=new ByteArrayContent(await FetchTorrent(config,source));
            file.Headers.ContentType=new System.Net.Http.Headers.MediaTypeHeaderValue("application/x-bittorrent");
            form.Add(file,"file","cached.torrent");
        }
        using var request=new HttpRequestMessage(HttpMethod.Post,api+"createtorrent");
        request.Headers.Authorization=new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",key);
        request.Headers.UserAgent.ParseAdd("Lampac-TorBox/1.0");request.Content=form;
        using var response=await client.SendAsync(request,HttpContext.RequestAborted);
        if((int)response.StatusCode==429)throw new TorBoxException(429,"TorBox ограничил частоту запросов. Попробуйте позже");
        JObject result;
        try { result=JObject.Parse(await response.Content.ReadAsStringAsync()); }
        catch { throw new TorBoxException(502,"TorBox не подтвердил наличие готового кэша. Загрузка не запускалась"); }
        if((bool?)result["success"]!=true){
            string code=(string)result["error"]??"",detail=(string)result["detail"]??"";
            if(code.IndexOf("CACH",StringComparison.OrdinalIgnoreCase)>=0 || code=="ITEM_NOT_FOUND" || detail.IndexOf("not cached",StringComparison.OrdinalIgnoreCase)>=0)return null;
            throw new TorBoxException(502,"TorBox не подтвердил готовую раздачу. Загрузка не запускалась");
        }
        if(!response.IsSuccessStatusCode)throw new TorBoxException(502,"TorBox не подтвердил готовую раздачу. Загрузка не запускалась");
        long? id=(long?)result["data"]?["torrent_id"]??(long?)result["data"]?["id"];
        if(!id.HasValue)return null;
        InvalidateOwned(key,id);
        var row=Rows(await Call("mylist?id="+id+"&bypass_cache=true",key)).FirstOrDefault(t=>(long?)t["id"]==id);
        return CachedReady(row)?row:null;
    }

    static string TrackLabel(JToken t) => ((string)t["language_full"]??(string)t["language"]??"Не указан язык")+(!string.IsNullOrWhiteSpace((string)t["title"])?" · "+(string)t["title"]:"");
    // TorBox expects a preset index, while the UI uses pixel heights.
    static int? ResolutionIndex(int resolution) => resolution switch {
        360 => 2, 480 => 3, 720 => 4, 1080 => 5, 1440 => 6, _ => null
    };
    async Task<JToken> StreamData(string key,long id,long file,int audio,int sub,int? resolutionIndex=null){
        return await Call("../stream/createstream?id="+id+"&file_id="+file+"&type=torrent&scrobbling_enabled=false&chosen_audio_index="+audio+"&chosen_subtitle_index="+(sub<0?"null":sub.ToString())+(resolutionIndex.HasValue?"&chosen_resolution_index="+resolutionIndex.Value:""),key);
    }

    [HttpGet, Route("lite/torbox/play")]
    public Task<ActionResult> OnlinePlay(string pick,long file,int audio=-1,int sub=-2,int resolution=-1) => Guard(async(c,key)=>{
        var p=GetPick(pick);
        if(p.Id==null || !p.Metadata.TryGetValue(file,out var known))return Error(400,"Сначала выберите параметры видео");
        int ac=(known["metadata"]?["audios"] as JArray)?.Count??0,sc=(known["metadata"]?["subtitles"] as JArray)?.Count??0;
        int height=(int?)known["metadata"]?["video"]?["height"]??0;
        if(audio<0 || audio>=Math.Max(1,ac) || sub < -1 || sub>=sc)return Error(400,"Выберите аудиодорожку и субтитры");
        if(resolution!=0 && !(new[]{1440,1080,720,480,360}.Contains(resolution) && resolution<height))return Error(400,"Выберите доступное разрешение");
        if(p.Serial){
            string selection=(resolution==0?"Исходное разрешение":resolution+"p")+" · "+(ac>audio?TrackLabel(known["metadata"]["audios"][audio]):"Звук по умолчанию")+" · "+(sub<0?"Без субтитров":TrackLabel(known["metadata"]["subtitles"][sub]));
            return await StartSeries(p,file,resolution,audio,sub,selection,key);
        }
        var data=await StreamData(key,p.Id.Value,file,audio,sub,resolution==0?null:ResolutionIndex(resolution));
        string url=(string)data["hls_url"];
        if(!Uri.TryCreate(url,UriKind.Absolute,out var uri)||uri.Scheme!="https")return Error(502,"TorBox не вернул HLS-поток");
        // One TorBox-issued URL for the exact preselected combination, for either player.
        // Empty quality map prevents client defaults from overriding the selected resolution.
        // No runtime TorBox descriptor: selections are made before playback.
        return Payload(new{success=true,torbox_stream=true,url,quality=new JObject(),hls_manifest_timeout=60000});
    });
}
