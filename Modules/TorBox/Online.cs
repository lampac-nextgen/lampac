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
    class PickGroup {
        public DateTime LastUsed = DateTime.UtcNow, CacheUntil;
        public string KeyHash;
        public Dictionary<string,bool?> Cache;
        public SemaphoreSlim CacheGate = new(1,1);
    }
    class Pick {
        public PickGroup Group = new();
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
    static readonly object pickCacheGate = new();
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
        lock(pickCacheGate) {
        if (ticket == null || !picks.TryGetValue(ticket,out var p) || p.Expires < DateTime.UtcNow || p.Owner != metadataOwner)
            throw new TorBoxException(410,"Список устарел. Откройте TorBox заново из карточки фильма");
        p.Group.LastUsed=DateTime.UtcNow;
        return p;
        }
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
    public Task<ActionResult> Online(string title, string original_title, int year=0, byte serial=0, bool rjson=true, long id=0, long tmdb_id=0, string source=null, string tb_voice=null) => Guard(async(c,key)=>{
        if(string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(original_title))return Error(400,"Не указано название фильма");
        string searchKey=SeriesKeyHash(metadataOwner+":"+c["jacred"]?.ToString(Newtonsoft.Json.Formatting.None))+":"+Normalize(title)+":"+Normalize(original_title)+":"+year+":"+serial;
        JArray rows;
        if(searchCache.TryGetValue(searchKey,out var hit) && hit.Expires>DateTime.UtcNow)rows=hit.Rows;
        else {
            var parser=c["jacred"] as JObject;
            if(parser==null || !Uri.TryCreate((string)parser["base_url"],UriKind.Absolute,out var baseUri))return Error(503,"JacRed для TorBox не настроен");
            string url=baseUri.ToString().TrimEnd('/')+"/api/v2.0/indexers/all/results?title="+Uri.EscapeDataString(title??original_title)+"&title_original="+Uri.EscapeDataString(original_title??title)+"&year="+(serial==1?0:year)+"&is_serial="+(serial==1?2:1)+"&apikey="+Uri.EscapeDataString((string)parser["api_key"]??"");
            using var response=await ParserClient.GetAsync(url,HttpContext.RequestAborted);
            if(!response.IsSuccessStatusCode)return Error(502,"JacRed не ответил на поиск");
            var parsed=JObject.Parse(await response.Content.ReadAsStringAsync());
            rows=parsed["Results"] as JArray ?? new JArray();
            if(searchCache.Count>256)searchCache.Clear();
            searchCache[searchKey]=(DateTime.UtcNow.AddMinutes(5),rows);
        }
        var eligible=rows.OfType<JObject>().Where(t=>!string.IsNullOrWhiteSpace((string)t["Title"]) &&
            ((!string.IsNullOrWhiteSpace(title) && Normalize((string)t["Title"]).Contains(Normalize(title))) ||
             (!string.IsNullOrWhiteSpace(original_title) && Normalize((string)t["Title"]).Contains(Normalize(original_title)))) &&
            !Regex.IsMatch((string)t["Title"],@"\b(CAMRip|HDCAM|HDTS|TSRip|TELESYNC)\b",RegexOptions.IgnoreCase) &&
            (serial==1 || year<=0 || !Regex.IsMatch((string)t["Title"],@"\b(19|20)\d{2}\b") || Regex.IsMatch((string)t["Title"],@"\b"+year+@"\b")))
            .OrderByDescending(Resolution).ThenByDescending(t=>(int?)t["Seeders"]??0).ToArray();
        if(eligible.Length==0)return Error(404,"JacRed не нашёл подходящих раздач фильма");
        var voices=eligible.SelectMany(ReleaseVoices).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(v=>v,StringComparer.OrdinalIgnoreCase).ToArray();
        string selected=(tb_voice??"").Trim();
        if(selected.Length>160)return Error(400,"Слишком длинное название озвучки");
        selected=voices.FirstOrDefault(v=>string.Equals(v,selected,StringComparison.OrdinalIgnoreCase))??selected;
        string search=host+"/lite/torbox?title="+Uri.EscapeDataString(title??"")+"&original_title="+Uri.EscapeDataString(original_title??"")+"&year="+year+"&serial="+serial+"&id="+id+"&tmdb_id="+tmdb_id+"&source="+Uri.EscapeDataString(source??"");
        var candidates=eligible.Where(t=>selected.Length==0 || ReleaseVoices(t).Contains(selected,StringComparer.OrdinalIgnoreCase)).Take(serial==1?96:12).ToArray();
        if(candidates.Length==0) {
            var empty=new SimilarTpl();empty.Append("По выбранной озвучке раздач нет. Выберите другую озвучку или «Все» в фильтре.",null,null,search);
            return Content(SimilarReleaseHtml(empty,voices,selected,search,true),"text/html; charset=utf-8");
        }
        lock(pickCacheGate) {
        foreach(var expired in picks.Where(p=>p.Value.Expires<DateTime.UtcNow).ToArray())picks.TryRemove(expired.Key,out _);
        // Evict whole least-recently-used menus, keeping release chains intact.
        // Allocation and eviction share a lock so concurrent searches stay bounded.
        while(picks.Count+candidates.Length>512) {
            var oldest=picks.Values.OrderBy(p=>p.Group.LastUsed).First().Group;
            foreach(var entry in picks.Where(p=>p.Value.Group==oldest).ToArray())picks.TryRemove(entry.Key,out _);
        }
        var group=new PickGroup{KeyHash=SeriesKeyHash(key)};
        var tpl=new SimilarTpl();
        string previous=null;
        var seriesPicks=new List<Pick>();
        for(int i=0;i<candidates.Length;i++) {
            var t=candidates[i];string link=(string)t["MagnetUri"] ?? (string)t["Link"];
            if(string.IsNullOrWhiteSpace(link))continue;
            string hash=Regex.Match(Uri.UnescapeDataString(link),@"urn:btih:([a-z0-9]+)",RegexOptions.IgnoreCase).Groups[1].Value;
            if(string.IsNullOrEmpty(hash))hash=(string)t["InfoHash"]??(string)t["Hash"]??"";
            hash=NormalizeCacheHash(hash)??"";
            string ticket=Guid.NewGuid().ToString("N");
            var p=new Pick{Group=group,Owner=metadataOwner,Link=link,Hash=hash,Name=(string)t["Title"],Movie=title??original_title,Serial=serial==1,Quality=Resolution(t),Seeds=(int?)t["Seeders"]??0,
                Film=new FilmInput{tmdb_id=tmdb_id>0 ? tmdb_id : (source=="tmdb" || source=="cub" ? id : 0),title=string.IsNullOrWhiteSpace(title)?original_title:title,original_title=original_title,year=year,media_type=serial==1?"tv":"movie"}};
            picks[ticket]=p;seriesPicks.Add(p);
            if(previous!=null)picks[previous].Next=ticket;
            previous=ticket;
            tpl.Append(p.Name,year.ToString(),p.Quality+"p · Озвучка: "+ReleaseVoices(t)[0]+(ReleaseVoices(t).Length>1 ? " + "+(ReleaseVoices(t).Length-1) : ""),OnlineUrl("choose?pick="+ticket));
        }
        if(serial==1){var peers=seriesPicks.ToArray();foreach(var p in peers)p.SeriesPicks=peers;}
        return Content(SimilarReleaseHtml(tpl,voices,selected,search),"text/html; charset=utf-8");
        }
    });

    static string[] ReleaseVoices(JToken row) {
        var values=(row["info"] as JObject)?["voices"] as JArray;
        var names=(values??new JArray()).Where(v=>v.Type==JTokenType.String).Select(v=>((string)v).Trim()).Where(v=>v.Length>0 && v.Length<=160).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return names.Length>0 ? names : new[]{"Не указана"};
    }
    static string SimilarReleaseHtml(SimilarTpl tpl,string[] voices,string selected,string search,bool empty=false) {
        var meta=new JObject{["torbox_preview"]=!empty,["torbox_release"]=!empty,["torbox_voice_empty"]=empty,
            ["torbox_voice_options"]=new JArray(voices),["torbox_voice_selected"]=selected,["torbox_voice_search"]=search};
        string json=meta.ToString(Newtonsoft.Json.Formatting.None).Replace("'", "\\u0027").Replace("<", "\\u003c").Replace(">", "\\u003e").Replace("&", "\\u0026");
        string prefix=json.Substring(0,json.Length-1)+",";
        string html=tpl.ToHtml();
        if(string.IsNullOrEmpty(html))return html??"";
        bool first=true;
        return Regex.Replace(html,"data-json='\\{",m=>{
            string flags="data-json='{\"torbox_preview\":"+(!empty?"true":"false")+",\"torbox_release\":"+(!empty?"true":"false")+",";
            if(!first)return flags;
            first=false;return "data-json='"+prefix;
        });
    }

    // A menu-scoped read-only batch. Opening the release list never waits for TorBox.
    public class CacheCheckInput { public string[] hashes {get;set;} }
    [HttpPost, Route("torbox/cache-check"),RequestSizeLimit(16384)]
    public Task<ActionResult> CacheCheck([FromBody] CacheCheckInput input) => Guard(async(c,key)=>{
        if(input?.hashes==null || input.hashes.Length==0 || input.hashes.Length>100 || input.hashes.Any(h=>!ValidCacheHash(h)))
            return Error(400,"Укажите от 1 до 100 корректных хешей раздач");
        var hashes=input.hashes.Select(NormalizeCacheHash).Distinct().OrderBy(h=>h,StringComparer.Ordinal).ToArray();
        var result=await CachedBadgeBatch(key,hashes);
        return Payload(new{success=true,retryable=result.Cache==null,retry_after=result.Cache==null?10:0,
            items=hashes.Select(hash=>new{hash,cached=CacheBadge(result.Cache,hash)}).ToArray()});
    });

    class BadgeBatch {
        public DateTime Until=DateTime.MaxValue;
        public TaskCompletionSource<JObject> Ready=new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    static readonly object badgeBatchGate=new();
    static readonly Dictionary<string,BadgeBatch> badgeBatches=new();
    static readonly Dictionary<string,DateTime> badgeBackoff=new();
    async Task<(JObject Cache,bool Fresh)> CachedBadgeBatch(string key,string[] hashes) {
        string owner=SeriesKeyHash(key),id=owner+":"+string.Join(",",hashes.OrderBy(h=>h,StringComparer.Ordinal));
        BadgeBatch batch;bool fresh=false;
        lock(badgeBatchGate) {
            foreach(var expired in badgeBatches.Where(x=>x.Value.Until<DateTime.UtcNow).Select(x=>x.Key).ToArray())badgeBatches.Remove(expired);
            foreach(var expired in badgeBackoff.Where(x=>x.Value<DateTime.UtcNow).Select(x=>x.Key).ToArray())badgeBackoff.Remove(expired);
            if(badgeBackoff.ContainsKey(owner))return (null,false);
            if(!badgeBatches.TryGetValue(id,out batch)) {
                if(badgeBatches.Count>=256)return (null,false);
                badgeBatches[id]=batch=new BadgeBatch();fresh=true;
            }
        }
        if(fresh) {
            JObject cache=null;
            try {
                using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(8));
                cache=await Call("checkcached?hash="+Uri.EscapeDataString(string.Join(",",hashes))+"&format=object",key,cancellationToken:deadline.Token,shared:true) as JObject;
            } catch(TorBoxException) { } catch(HttpRequestException) { } catch(OperationCanceledException) { }
              catch(Newtonsoft.Json.JsonException) { }
            finally {
                lock(badgeBatchGate) {
                    batch.Until=DateTime.UtcNow.AddSeconds(cache==null?10:30);
                    if(cache==null && badgeBackoff.Count<256)badgeBackoff[owner]=batch.Until;
                    batch.Ready.TrySetResult(cache);
                }
            }
        }
        return (await batch.Ready.Task,fresh);
    }

    [HttpGet, Route("torbox/menu-cache")]
    public Task<ActionResult> MenuCache(string pick) => Guard(async(c,key)=>{
        var first=GetPick(pick);var group=first.Group;
        if(group.KeyHash!=SeriesKeyHash(key))return Error(410,"Настройки TorBox изменились. Откройте список заново");
        if(!await group.CacheGate.WaitAsync(TimeSpan.FromSeconds(10),HttpContext.RequestAborted))
            return Payload(new{success=true,available=false,retry_after=1,items=new object[0]});
        try {
            (string Ticket,Pick Pick)[] menu;
            lock(pickCacheGate)menu=picks.Where(x=>x.Value.Group==group).Select(x=>(x.Key,x.Value)).ToArray();
            if(group.Cache==null || group.CacheUntil<DateTime.UtcNow) {
                var hashes=menu.Select(x=>x.Pick.Hash).Where(ValidCacheHash).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                JObject cache=hashes.Length>0 ? (await CachedBadgeBatch(key,hashes)).Cache : new JObject();
                group.Cache=menu.ToDictionary(x=>x.Ticket,x=>CacheBadge(cache,x.Pick.Hash));
                group.CacheUntil=DateTime.UtcNow.AddSeconds(cache==null?10:30);
            }
            return Payload(new{success=true,available=group.Cache.Values.Any(x=>x.HasValue),
                items=menu.Select(x=>new{pick=x.Ticket,cached=group.Cache.TryGetValue(x.Ticket,out var state)?state:null}).ToArray()});
        } finally { group.CacheGate.Release(); }
    });
    static string NormalizeCacheHash(string hash) {
        if(!ValidCacheHash(hash))return null;
        hash=hash.ToLowerInvariant();
        if(hash.Length==40)return hash;
        int bits=0,buffer=0;var hex=new StringBuilder(40);
        foreach(char c in hash) {
            buffer=(buffer<<5)|"abcdefghijklmnopqrstuvwxyz234567".IndexOf(c);bits+=5;
            if(bits>=8){bits-=8;hex.Append(((buffer>>bits)&255).ToString("x2"));}
        }
        return hex.ToString();
    }
    static bool ValidCacheHash(string hash) => Regex.IsMatch(hash??"","^([a-f0-9]{40}|[a-z2-7]{32})$",RegexOptions.IgnoreCase);
    static bool? CacheBadge(JObject cache,string hash) {
        if(cache==null || !ValidCacheHash(hash))return null;
        var entry=cache.Properties().FirstOrDefault(x=>string.Equals(x.Name,hash,StringComparison.OrdinalIgnoreCase))?.Value;
        if(entry==null)return false;
        if(entry is not JObject obj)return null;
        if(!obj.HasValues)return false;
        if(obj["cached"]?.Type==JTokenType.Boolean)return (bool)obj["cached"];
        // The object response omits misses; populated file/name records denote hits.
        return obj.HasValues && (obj["files"] is JArray || obj["name"]?.Type==JTokenType.String) ? true : null;
    }

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
                    if(CacheBadge(cache,p.Hash)!=true)continue;
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
