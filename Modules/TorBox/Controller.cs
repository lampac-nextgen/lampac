using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using Shared;
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace TorBox;

public partial class TorBoxController : BaseController
{
    static readonly HttpClient client = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(22) };
    static readonly SemaphoreSlim addGate = new(1, 1);
    const string api = "https://api.torbox.app/v1/api/torrents/";
    const string configPath = "torbox-private.json";

    const string settingsPath = "torbox-settings.json";
    bool serverCredentials;
    string metadataOwner;
    bool clientCredentialsAllowed;
    JObject Config(string mode = null) {
        // Malformed/unreadable files never enable client credentials.
        JObject policy=null;
        try { policy=JObject.Parse(System.IO.File.ReadAllText(configPath)); }
        catch(FileNotFoundException) { }
        bool hasPrivate=policy!=null;
        policy ??= JObject.Parse(System.IO.File.ReadAllText(settingsPath));
        var permission=policy["allow_client_credentials"];
        if(permission!=null && permission.Type!=JTokenType.Boolean)throw new TorBoxException(503,"allow_client_credentials должен быть true или false");
        clientCredentialsAllowed=(bool?)permission==true;
        serverCredentials=!clientCredentialsAllowed;
        if(serverCredentials && !hasPrivate)throw new TorBoxException(503,"Файл torbox-private.json отсутствует");
        var modes=Request.Headers["X-TorBox-Mode"];
        if(mode==null && modes.Count>1)throw new TorBoxException(400,"Некорректный источник настроек TorBox");
        mode ??= modes.Count==1 ? modes[0] : serverCredentials ? "file" : "client";
        if(mode!="file" && mode!="client")throw new TorBoxException(400,"Некорректный источник настроек TorBox");
        if((mode=="client")!=clientCredentialsAllowed)throw new TorBoxException(403,"Источник настроек TorBox задан администратором сервера");
        // In client mode only the permission flag is consumed from this file.
        // Platform authentication still runs before any TorBox operation.
        return serverCredentials ? policy : new JObject {
            ["allowed_uids"]=new JArray(requestInfo.user_uid ?? ""), ["torrent_origins"]=new JArray()
        };
    }
    JObject ClientConfig(JObject policy) {
        var values=Request.Headers["X-TorBox-Config"];
        if(values.Count==0)return policy;
        if(values.Count!=1 || values[0].Length>8192)throw new TorBoxException(400,"Некорректные настройки TorBox");
        JObject input;
        try { input=JObject.Parse(System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(values[0]))); }
        catch { throw new TorBoxException(400,"Некорректные настройки TorBox"); }
        var c=(JObject)policy.DeepClone();
        foreach(var property in input.Properties())
            if(property.Name!="allowed_uids" && property.Name!="torrent_origins" && property.Name!="jacred")throw new TorBoxException(400,"Неизвестное поле настроек TorBox");
        foreach(string name in new[]{"allowed_uids","torrent_origins"}) {
            if(input[name]==null)continue;
            if(input[name] is not JArray list || list.Count>100 || list.Any(x=>x.Type!=JTokenType.String || ((string)x).Length>256))throw new TorBoxException(400,"Некорректный список "+name);
            if(name=="torrent_origins" && list.Any(x=>!Uri.TryCreate((string)x,UriKind.Absolute,out var u) || !ClientAddress(u) ||
                u.AbsolutePath!="/" || !string.IsNullOrEmpty(u.Query) || !string.IsNullOrEmpty(u.Fragment)))throw new TorBoxException(400,"Укажите публичные HTTP/HTTPS адреса torrent-файлов");
            if(name=="allowed_uids" && list.Count==0)continue;
            c[name]=list.DeepClone();
        }
        if(input["jacred"]!=null) {
            if(input["jacred"] is not JObject parser || parser.Properties().Any(x=>x.Name!="base_url" && x.Name!="api_key") ||
                parser["base_url"]?.Type!=JTokenType.String || parser["api_key"]?.Type!=JTokenType.String ||
                ((string)parser["api_key"]).Length>256)throw new TorBoxException(400,"Некорректные настройки JacRed");
            if(!Uri.TryCreate((string)parser["base_url"],UriKind.Absolute,out var address) || !ClientAddress(address) ||
                !string.IsNullOrEmpty(address.Query) || !string.IsNullOrEmpty(address.Fragment))throw new TorBoxException(400,"Укажите публичный HTTP/HTTPS адрес JacRed");
            c["jacred"]=parser.DeepClone();
        }
        return c;
    }
    // Pin user-selected parser connections to validated public addresses, including
    // DNS results. The shared server client keeps its existing network behavior.
    static readonly HttpClient publicClient = new(new SocketsHttpHandler {
        AllowAutoRedirect=false, UseProxy=false, ConnectCallback=ConnectPublic
    }) { Timeout=TimeSpan.FromSeconds(22) };
    HttpClient ParserClient => serverCredentials ? client : publicClient;
    static bool PublicAddress(System.Net.IPAddress address) {
        if(address.IsIPv4MappedToIPv6)address=address.MapToIPv4();
        var b=address.GetAddressBytes();
        if(b.Length==16)return (b[0]&0xe0)==0x20 &&
            !(b[0]==0x20 && b[1]==0x01 && (b[2]<2 || (b[2]==0x0d && b[3]==0xb8))) &&
            !(b[0]==0x20 && b[1]==0x02) && !(b[0]==0x3f && b[1]==0xff);
        return b[0]!=0 && b[0]!=10 && b[0]!=127 && b[0]<224 &&
            !(b[0]==100 && b[1]>=64 && b[1]<=127) && !(b[0]==169 && b[1]==254) &&
            !(b[0]==172 && b[1]>=16 && b[1]<=31) && !(b[0]==192 && (b[1]==168 ||
                (b[1]==0 && (b[2]==0 || b[2]==2)) || (b[1]==88 && b[2]==99))) &&
            !(b[0]==198 && (b[1]==18 || b[1]==19 || (b[1]==51 && b[2]==100))) &&
            !(b[0]==203 && b[1]==0 && b[2]==113);
    }
    static bool ClientAddress(Uri u) => (u.Scheme=="https" || u.Scheme=="http") && string.IsNullOrEmpty(u.UserInfo) &&
        !u.IsLoopback && (!System.Net.IPAddress.TryParse(u.Host.Trim('[',']'),out var ip) || PublicAddress(ip));
    static async System.Threading.Tasks.ValueTask<System.IO.Stream> ConnectPublic(SocketsHttpConnectionContext context,CancellationToken token) {
        var addresses=await System.Net.Dns.GetHostAddressesAsync(context.DnsEndPoint.Host,token);
        if(addresses.Length==0 || addresses.Any(a=>!PublicAddress(a)))throw new HttpRequestException("TorBox parser address is not public");
        foreach(var address in addresses) {
            var socket=new System.Net.Sockets.Socket(address.AddressFamily,System.Net.Sockets.SocketType.Stream,System.Net.Sockets.ProtocolType.Tcp);
            try { await socket.ConnectAsync(new System.Net.IPEndPoint(address,context.DnsEndPoint.Port),token);return new System.Net.Sockets.NetworkStream(socket,true); }
            catch(System.Net.Sockets.SocketException) { socket.Dispose(); }
            catch { socket.Dispose();throw; }
        }
        throw new HttpRequestException("TorBox parser unavailable");
    }
    string ClientKey() {
        var values=Request.Headers["X-TorBox-Key"];
        if(values.Count!=1)return "";
        string key=values[0]??"";
        return Regex.IsMatch(key,@"^[a-zA-Z0-9_.-]{16,256}$") ? key : "";
    }
    string CredentialOwner(string key) => serverCredentials ? requestInfo.user_uid :
        requestInfo.user_uid+":torbox:"+Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key))).ToLowerInvariant();

    bool Allowed(JObject c) => !requestInfo.IsAnonymousRequest &&
        !string.IsNullOrEmpty(requestInfo.user_uid) &&
        (c["allowed_uids"] as JArray)?.Any(x => string.Equals((string)x, requestInfo.user_uid, StringComparison.OrdinalIgnoreCase)) == true;

    ActionResult Payload(object value) => Content(Newtonsoft.Json.JsonConvert.SerializeObject(value), "application/json; charset=utf-8");
    ActionResult Error(int status, string message) => new ContentResult { StatusCode = status, ContentType = "application/json; charset=utf-8", Content = Newtonsoft.Json.JsonConvert.SerializeObject(new { success = false, detail = message }) };

    async Task<ActionResult> Guard(Func<JObject, string, Task<ActionResult>> work)
    {
        Response.Headers["Cache-Control"] = "no-store";
        try
        {
            var c = Config();
            if (!Allowed(c)) return Error(403, "Нет доступа к TorBox для этого аккаунта Lampac");
            if(!serverCredentials) {
                c=ClientConfig(c);
                if(!Allowed(c))return Error(403,"Аккаунт отсутствует в клиентском списке allowed_uids");
            }
            string key = serverCredentials ? ((string)c["api_key"] ?? "").Trim() : ClientKey();
            if (key.Length == 0) return Error(503, serverCredentials ? "TorBox пока не подключён: требуется API-ключ на сервере" : "Укажите API-ключ в настройках TorBox на этом устройстве");
            metadataOwner=CredentialOwner(key);
            return await work(c, key);
        }
        catch (TorBoxException ex) { return Error(ex.Status, ex.Message); }
        catch (TaskCanceledException) { return Error(504, "TorBox не ответил вовремя. Обновите список перед повторным добавлением"); }
        catch (HttpRequestException) { return Error(502, "Не удалось связаться с TorBox"); }
        catch { return Error(502, "Не удалось обработать ответ TorBox"); }
    }

    async Task<JToken> Call(string path, string key, HttpContent body = null, CancellationToken cancellationToken = default, bool allowMissing = false, HttpMethod method = null)
    {
        using var req = new HttpRequestMessage(method ?? (body == null ? HttpMethod.Get : HttpMethod.Post), api + path);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        req.Headers.UserAgent.ParseAdd("Lampac-TorBox/1.0");
        req.Content = body;
        using var linked=cancellationToken.CanBeCanceled?CancellationTokenSource.CreateLinkedTokenSource(cancellationToken,HttpContext.RequestAborted):null;
        using var resp = await client.SendAsync(req, linked?.Token??HttpContext.RequestAborted);
        if (resp.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            throw new TorBoxException(429, "TorBox ограничил частоту запросов. Попробуйте позже");
        if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized || resp.StatusCode == System.Net.HttpStatusCode.Forbidden)
            throw new TorBoxException(502, "TorBox отклонил запрос: проверьте API-ключ и доступность сервиса");
        if (!resp.IsSuccessStatusCode && !(allowMissing && resp.StatusCode == System.Net.HttpStatusCode.NotFound))
            throw new TorBoxException(502, "Ошибка сервиса TorBox (HTTP " + (int)resp.StatusCode + ")");
        var j = JObject.Parse(await resp.Content.ReadAsStringAsync());
        // TorBox reports an absent torrent as HTTP404 + ITEM_NOT_FOUND, even with success:true.
        if (allowMissing && path.StartsWith("mylist?id=", StringComparison.Ordinal) && (string)j["error"] == "ITEM_NOT_FOUND")return new JArray();
        if (!resp.IsSuccessStatusCode)throw new TorBoxException(502,"Не удалось проверить наличие раздачи TorBox");
        if ((bool?)j["success"] != true)
        {
            string code = (string)j["error"] ?? "UNKNOWN";
            // Never expose upstream detail/URLs: requestdl URLs contain the account API key.
            string message = code == "DOWNLOAD_NOT_FINISHED" ? "Загрузка в TorBox ещё не завершена" :
                code == "ACTIVE_LIMIT" ? "Достигнут лимит активных загрузок TorBox" :
                code == "ITEM_NOT_FOUND" ? "Раздача или файл отсутствует в TorBox" :
                "TorBox не выполнил запрос. Проверьте состояние загрузки и лимиты аккаунта";
            throw new TorBoxException(502, message);
        }
        return j["data"] ?? JValue.CreateNull();
    }

    JArray Rows(JToken data) => data is JArray a ? a : data is JObject o ? new JArray(o) : new JArray();
    object FileRow(JToken f) => new { id = (long?)f["id"] ?? -1, name = (string)f["short_name"] ?? (string)f["name"] ?? "Файл", size = (long?)f["size"] ?? 0 };
    object TorrentRow(JToken t) => new {
        id = (long?)t["id"] ?? -1,
        airlocked = t["airlocked"]?.Type == JTokenType.Boolean ? (bool?)t["airlocked"] : null,
        name = (string)t["name"] ?? "Раздача",
        hash = (string)t["hash"],
        film = ReadFilm(t),
        ready = (bool?)t["download_present"] == true || (bool?)t["download_finished"] == true,
        state = (string)t["download_state"] ?? "unknown",
        progress = (double?)t["progress"] ?? 0,
        size = (long?)t["size"] ?? 0,
        speed = (long?)t["download_speed"] ?? 0,
        files = (t["files"] as JArray ?? new JArray()).Select(FileRow).ToArray()
    };

    [HttpGet, AllowAnonymous, Route("torbox.js")]
    public ActionResult Plugin()
    {
        Response.Headers["Cache-Control"] = "no-cache";
        return Content(System.IO.File.ReadAllText("mods/TorBox/plugin.js").Replace("{localhost}", host), "application/javascript; charset=utf-8");
    }

    [HttpGet, Route("torbox/status")]
    public ActionResult Status()
    {
        Response.Headers["Cache-Control"] = "no-store";
        try {
            var c = Config();
            if (!Allowed(c)) return Error(403, "Нет доступа к TorBox для этого аккаунта Lampac");
            return Payload(new { success = true, file_available = !clientCredentialsAllowed, client_available = clientCredentialsAllowed,
                configured = serverCredentials ? !string.IsNullOrWhiteSpace((string)c["api_key"]) : ClientKey().Length>0,
                server_key = serverCredentials, key_editable = !serverCredentials });
        } catch { return Error(503, "TorBox пока не настроен на сервере"); }
    }

    [HttpGet, Route("torbox/list")]
    public Task<ActionResult> List(int offset = 0) => Guard(async (c, key) => {
        if (offset < 0 || offset > 100000) return Error(400, "Некорректная страница");
        var rows = Rows(await Call("mylist?limit=100&offset=" + offset, key));
        return Payload(new { success = true, data = rows.Select(TorrentRow).ToArray(), more = rows.Count == 100, deletion_scope = DeletionScope(key) });
    });

    [HttpGet, Route("torbox/item")]
    public Task<ActionResult> Item(long id) => Guard(async (c, key) => {
        if (id < 0) return Error(400, "Некорректная раздача");
        var rows = Rows(await Call("mylist?id=" + id + "&bypass_cache=true", key));
        var t = rows.FirstOrDefault(x => (long?)x["id"] == id);
        return t == null ? Error(404, "Раздача отсутствует в TorBox") : Payload(new { success = true, data = TorrentRow(t) });
    });

    const string libraryPath = "database/torbox-library.json";
    static readonly object libraryGate = new();
    static JObject libraryData;
    public class FilmInput {
        public long tmdb_id { get; set; }
        public string title { get; set; }
        public string original_title { get; set; }
        public int year { get; set; }
        public string poster_path { get; set; }
        public string media_type { get; set; }
    }
    public class FilmLinkInput { public long id { get; set; } public FilmInput film { get; set; } public bool automatic { get; set; } }
    static JObject CleanFilm(FilmInput film) {
        if (film == null || string.IsNullOrWhiteSpace(film.title) || film.title.Length > 400 ||
            (film.original_title?.Length ?? 0) > 400 || film.tmdb_id < 0 || film.tmdb_id > int.MaxValue ||
            (film.year != 0 && (film.year < 1800 || film.year > 2200)) ||
            (film.media_type != "movie" && film.media_type != "tv") ||
            (!string.IsNullOrEmpty(film.poster_path) && !Regex.IsMatch(film.poster_path, @"^/[a-zA-Z0-9_.-]{1,160}$"))) return null;
        return JObject.FromObject(new { film.tmdb_id, title=film.title.Trim(), original_title=(film.original_title??"").Trim(), film.year, poster_path=film.poster_path??"", film.media_type });
    }
    static string FilmKey(JToken torrent) {
        string hash=((string)torrent["hash"]??"").ToLowerInvariant();
        return Regex.IsMatch(hash,"^([a-f0-9]{40}|[a-z2-7]{32})$") ? "hash:"+hash : "id:"+(long?)torrent["id"];
    }
    static readonly System.Collections.Generic.Dictionary<string,DateTime> metadataWarningAfter = new();
    static void MetadataWarning(string operation, Exception ex) {
        // Optional metadata/logging must never change playback or delete outcomes.
        try {
            lock(libraryGate) {
                if(metadataWarningAfter.TryGetValue(operation,out var after) && DateTime.UtcNow < after)return;
                metadataWarningAfter[operation]=DateTime.UtcNow.AddMinutes(1);
                if(Serilog.Log.IsEnabled(Serilog.Events.LogEventLevel.Warning))
                    Serilog.Log.Warning("TorBox metadata {Operation}: {ExceptionType}; path={MetadataPath}",
                        operation,ex.GetType().Name,libraryPath);
                else
                    Console.Error.WriteLine("TorBox metadata "+operation+": "+ex.GetType().Name+"; path="+libraryPath);
            }
        } catch { }
    }
    static JObject LoadLibraryForWrite() {
        if(libraryData!=null)return libraryData;
        JObject loaded;
        try { loaded=JObject.Parse(System.IO.File.ReadAllText(libraryPath)); }
        catch(FileNotFoundException) { loaded=new JObject(); }
        catch(DirectoryNotFoundException) { loaded=new JObject(); }
        catch(Newtonsoft.Json.JsonException ex) {
            // Only a writer recovers corruption. A failed move must abort the write.
            string quarantine=libraryPath+".corrupt-"+DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffffZ")+"-"+Guid.NewGuid().ToString("N");
            System.IO.File.Move(libraryPath,quarantine);
            MetadataWarning("quarantine",ex);
            loaded=new JObject();
        }
        return loaded;
    }
    static JObject LoadLibraryForRead() {
        if(libraryData!=null)return libraryData;
        try {
            // Do not publish an empty fallback, including a missing-file result.
            var loaded=JObject.Parse(System.IO.File.ReadAllText(libraryPath));
            return libraryData=loaded;
        }
        catch(FileNotFoundException) { return new JObject(); }
        catch(DirectoryNotFoundException) { return new JObject(); }
        catch(Exception ex) { MetadataWarning("read",ex);return new JObject(); }
    }
    JObject ReadFilm(JToken torrent) {
        lock(libraryGate) {
            var owner=LoadLibraryForRead()[metadataOwner] as JObject;
            return (owner?[FilmKey(torrent)] ?? owner?["id:"+(long?)torrent["id"]])?.DeepClone() as JObject;
        }
    }
    JObject SaveFilm(JToken torrent, FilmInput film, bool manual=false) {
        var clean=CleanFilm(film);
        if(clean==null)return null;
        lock(libraryGate) {
            // Work on a copy; a failed disk write must not change the in-memory state.
            var updated=(JObject)LoadLibraryForWrite().DeepClone();
            var owner=updated[metadataOwner] as JObject;
            if(owner==null){owner=new JObject();updated[metadataOwner]=owner;}
            string key=FilmKey(torrent);
            var existing=(owner[key] ?? owner["id:"+(long?)torrent["id"]]) as JObject;
            if(!manual && (bool?)existing?["manual"]==true)return (JObject)existing.DeepClone();
            clean["manual"]=manual;
            clean["updated"]=DateTime.UtcNow;
            owner[key]=clean;
            if(owner.Count>5000)throw new TorBoxException(409,"Слишком много сохранённых соответствий фильмов");
            string temp=libraryPath+".tmp";
            Directory.CreateDirectory("database");
            try {
                System.IO.File.WriteAllText(temp,updated.ToString(Newtonsoft.Json.Formatting.None));
                System.IO.File.SetUnixFileMode(temp,UnixFileMode.UserRead|UnixFileMode.UserWrite);
                System.IO.File.Move(temp,libraryPath,true);
                libraryData=updated;
            } finally { if(System.IO.File.Exists(temp))System.IO.File.Delete(temp); }
            return (JObject)clean.DeepClone();
        }
    }
    void RemoveFilm(JToken torrent){
        lock(libraryGate){
            var updated=(JObject)LoadLibraryForWrite().DeepClone();
            var owner=updated[metadataOwner] as JObject;
            if(owner==null)return;
            owner.Remove(FilmKey(torrent));owner.Remove("id:"+(long?)torrent["id"]);
            string temp=libraryPath+".tmp";
            Directory.CreateDirectory("database");
            try{
                System.IO.File.WriteAllText(temp,updated.ToString(Newtonsoft.Json.Formatting.None));
                System.IO.File.SetUnixFileMode(temp,UnixFileMode.UserRead|UnixFileMode.UserWrite);
                System.IO.File.Move(temp,libraryPath,true);libraryData=updated;
            }finally{if(System.IO.File.Exists(temp))System.IO.File.Delete(temp);}
        }
    }
    bool RememberFilm(JToken torrent,FilmInput film) {
        // Optional enrichment must never prevent playback or change torrent creation.
        try { return SaveFilm(torrent,film)!=null; } catch(Exception ex) { MetadataWarning("remember",ex);return false; }
    }
    [HttpPost, Route("torbox/metadata")]
    public Task<ActionResult> Metadata([FromBody] FilmLinkInput input) => Guard(async(c,key)=>{
        if(input==null || input.id<0 || CleanFilm(input.film)==null)return Error(400,"Некорректная карточка фильма");
        var torrent=Rows(await Call("mylist?id="+input.id+"&bypass_cache=true",key)).FirstOrDefault(t=>(long?)t["id"]==input.id);
        if(torrent==null)return Error(404,"Раздача отсутствует в вашем TorBox");
        return Payload(new { success=true, film=SaveFilm(torrent,input.film,!input.automatic) });
    });
    public class AddInput { public string link { get; set; } public string hash { get; set; } public FilmInput film { get; set; } public bool allow_download { get; set; } public bool cache_only { get; set; } }

    [HttpPost, Route("torbox/add"), RequestSizeLimit(32768)]
    public Task<ActionResult> Add([FromBody] AddInput input) => Guard(async (c, key) => {
        if (input == null) return Error(400, "Нет ссылки на раздачу");
        if(input.cache_only && input.allow_download)return Error(400,"Просмотр из кэша не разрешает загрузку раздачи");
        string link = (input.link ?? "").Trim();
        string hash = (input.hash ?? "").Trim().ToLowerInvariant();
        if (!Regex.IsMatch(hash, "^([a-f0-9]{40}|[a-z2-7]{32})$")) hash = null;
        if (link.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase)) {
            var match = Regex.Match(Uri.UnescapeDataString(link), "(?:[?&])xt=urn:btih:([a-f0-9]{40}|[a-z2-7]{32})(?:&|$)", RegexOptions.IgnoreCase);
            if (!match.Success) return Error(400, "Magnet не содержит корректного btih");
            hash = match.Groups[1].Value.ToLowerInvariant();
        } else if (hash != null) link = "magnet:?xt=urn:btih:" + hash;
        else if (!Uri.TryCreate(link, UriKind.Absolute, out var testUri) ||
                 !(testUri.Scheme == "http" || testUri.Scheme == "https") ||
                 !HostAllowed(c, testUri)) return Error(400, "Нет magnet или хеша. Адрес torrent-файла не входит в список доверенных парсеров");
        hash=NormalizeCacheHash(hash);
        if (link.Length > 16000) return Error(400, "Слишком длинная ссылка");
        if (!await addGate.WaitAsync(TimeSpan.FromSeconds(3), HttpContext.RequestAborted)) return Error(429, "Добавляется другая раздача. Подождите несколько секунд");
        try {
            if (hash != null) {
                var existing = (await OwnedTorrents(key)).FirstOrDefault(t => string.Equals((string)t["hash"], hash, StringComparison.OrdinalIgnoreCase));
                if (existing != null && (!input.cache_only || CachedReady(existing))) {
                    RememberFilm(existing,input.film);
                    return Payload(new { success = true, id = (long?)existing["id"], existing = true });
                }
            }
            bool cached = false;
            if (hash != null) {
                var cache = await Call("checkcached?hash=" + Uri.EscapeDataString(hash) + "&format=object", key);
                var entry=(cache as JObject)?.Properties().FirstOrDefault(p=>string.Equals(p.Name,hash,StringComparison.OrdinalIgnoreCase))?.Value as JObject;
                cached = CacheBadge(cache as JObject,hash)==true;
            }
            if(!input.allow_download) {
                if(hash!=null && !cached)return Payload(new {success=true,confirmation_required=true});
                // Enforce cache-only on TorBox too, closing the checkcached/create race.
                var ready=await AttachCachedOnly(new Pick {Hash=hash,Link=link},c,key);
                if(ready==null)return Payload(new {success=true,confirmation_required=true});
                RememberFilm(ready,input.film);
                return Payload(new {success=true,id=(long?)ready["id"],existing=false,cached=true});
            }
            using var form = new MultipartFormDataContent();
            form.Add(new StringContent("false"), "add_only_if_cached");
            form.Add(new StringContent("1"), "seed"); // Follow the account's configured seeding preference.
            form.Add(new StringContent("false"), "allow_zip");
            if (link.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase)) form.Add(new StringContent(link), "magnet");
            else {
                var bytes = await FetchTorrent(c, new Uri(link));
                var file = new ByteArrayContent(bytes);
                file.Headers.ContentType = new MediaTypeHeaderValue("application/x-bittorrent");
                form.Add(file, "file", "upload.torrent");
            }
            var added = await Call("createtorrent", key, form);
            long? id = (long?)added["torrent_id"] ?? (long?)added["id"];
            if (id != null) InvalidateOwned(key,id);
            if (id == null) throw new TorBoxException(502, "TorBox принял запрос без номера раздачи. Обновите «Мой TorBox»");
            RememberFilm(JObject.FromObject(new {id,hash}),input.film);
            return Payload(new { success = true, id, existing = false, cached });
        } finally { addGate.Release(); }
    });

    bool HostAllowed(JObject c, Uri u) => string.IsNullOrEmpty(u.UserInfo) &&
        (c["torrent_origins"] as JArray)?.Any(x => string.Equals((string)x, u.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase)) == true;

    async Task<byte[]> FetchTorrent(JObject c, Uri u)
    {
        for (int redirects = 0; redirects < 4; redirects++) {
            if (!HostAllowed(c, u)) throw new TorBoxException(400, "Парсер перенаправил запрос на недоверенный адрес");
            using var r = await ParserClient.GetAsync(u, HttpCompletionOption.ResponseHeadersRead, HttpContext.RequestAborted);
            if ((int)r.StatusCode >= 300 && (int)r.StatusCode < 400 && r.Headers.Location != null) { u = new Uri(u, r.Headers.Location); continue; }
            if (!r.IsSuccessStatusCode) throw new TorBoxException(502, "Не удалось получить torrent-файл у парсера");
            if (r.Content.Headers.ContentLength > 8 * 1024 * 1024) throw new TorBoxException(400, "Torrent-файл слишком большой");
            using var stream = await r.Content.ReadAsStreamAsync();
            using var data = new MemoryStream();
            byte[] buf = new byte[16384];
            int n;
            while ((n = await stream.ReadAsync(buf, 0, buf.Length, HttpContext.RequestAborted)) > 0) {
                if (data.Length + n > 8 * 1024 * 1024) throw new TorBoxException(400, "Torrent-файл слишком большой");
                data.Write(buf, 0, n);
            }
            var bytes = data.ToArray();
            if (bytes.Length < 2 || bytes[0] != (byte)'d' || bytes[bytes.Length - 1] != (byte)'e')
                throw new TorBoxException(400, "Парсер вернул не torrent-файл");
            return bytes;
        }
        throw new TorBoxException(502, "Слишком много перенаправлений парсера");
    }

    public class PlayInput { public long id { get; set; } public long file { get; set; } }

    [HttpPost, Route("torbox/play"), RequestSizeLimit(4096)]
    public Task<ActionResult> Play([FromBody] PlayInput input) => Guard(async (c, key) => {
        if (input == null || input.id < 0 || input.file < 0) return Error(400, "Некорректный файл");
        var rows = Rows(await Call("mylist?id=" + input.id, key));
        var t = rows.FirstOrDefault(x => (long?)x["id"] == input.id);
        if (t == null) return Error(404, "Раздача отсутствует в TorBox");
        if (!((bool?)t["download_present"] == true || (bool?)t["download_finished"] == true)) return Error(409, "Раздача ещё загружается в TorBox");
        var files = t["files"] as JArray ?? new JArray();
        if (!files.Any(f => (long?)f["id"] == input.file)) return Error(404, "Файл отсутствует в раздаче");
        string path = "requestdl?token=" + Uri.EscapeDataString(key) + "&torrent_id=" + input.id + "&file_id=" + input.file + "&redirect=false";
        if (!requestInfo.IsLocalIp && !string.IsNullOrEmpty(requestInfo.IP)) path += "&user_ip=" + Uri.EscapeDataString(requestInfo.IP);
        string url = (string)await Call(path, key);
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https")
            throw new TorBoxException(502, "TorBox вернул некорректную ссылку воспроизведения");
        return Payload(new { success = true, url });
    });

    class TorBoxException : Exception {
        public int Status;
        public TorBoxException(int status, string message) : base(message) { Status = status; }
    }
}
