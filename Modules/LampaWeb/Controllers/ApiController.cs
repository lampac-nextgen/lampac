using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Shared;
using Shared.Attributes;
using Shared.Models.Events;
using Shared.Services;
using Shared.Services.Pools;
using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using IO = System.IO;

namespace LampaWeb;

public class ApiController : BaseController
{
    [HttpGet, AllowAnonymous]
    [Route("/personal.lampa")]
    [Route("/lampa-main/personal.lampa")]
    [Route("/{myfolder}/personal.lampa")]
    public ActionResult PersonalLampa(string myfolder)
        => StatusCode(200);


    [HttpGet, AllowAnonymous]
    [Route("/reqinfo")]
    public ActionResult Reqinfo() => Content(JsonConvert.SerializeObject(requestInfo, new JsonSerializerSettings()
    {
        NullValueHandling = NullValueHandling.Ignore,
        DefaultValueHandling = DefaultValueHandling.Ignore
    }), "application/json; charset=utf-8");


    #region Index
    [HttpGet, AllowAnonymous]
    [Staticache(5, always: true)]
    [Route("/")]
    public ActionResult Index()
    {
        if (string.IsNullOrEmpty(ModInit.conf.index))
            return Content("api work", "text/plain; charset=utf-8");

        if (ModInit.conf.basetag && Regex.IsMatch(ModInit.conf.index, "/[^\\./]+\\.html$"))
        {
            string html = IO.File.ReadAllText($"wwwroot/{ModInit.conf.index}");
            html = html.Replace("<head>", $"<head><base href=\"/{Regex.Match(ModInit.conf.index, "^([^/]+)/").Groups[1].Value}/\" />");

            return ContentTo(html, "text/html; charset=utf-8");
        }

        return LocalRedirect($"/{ModInit.conf.index}");
    }
    #endregion

    #region Extensions
    [HttpGet, AllowAnonymous]
    [Staticache(5, always: true, setHeadersNoCache: true)]
    [Route("/extensions")]
    public ActionResult Extensions()
    {
        string extensions = FileCache.ReadAllText($"{ModInit.modpath}/plugins/extensions.json", "extensions.json", saveCache: false)
            .Replace("{localhost}", host)
            .Replace("\n", "")
            .Replace("\r", "");

        return ContentTo(extensions);
    }
    #endregion

    #region testaccsdb
    [HttpGet, HttpPost]
    [Route("/testaccsdb")]
    public ActionResult TestAccsdb(string account_email, string uid)
    {
        // uid совпадает с shared_passwd, возвращаем команду изменить uid
        if (!string.IsNullOrEmpty(CoreInit.conf.accsdb.shared_passwd) && uid == CoreInit.conf.accsdb.shared_passwd)
            return Content("{\"accsdb\": true, \"newuid\": true}", "application/json; charset=utf-8");

        #region shared_passwd
        if (!string.IsNullOrEmpty(uid) && !string.IsNullOrEmpty(account_email) && account_email == CoreInit.conf.accsdb.shared_passwd)
        {
            try
            {
                string file = "users.json";

                JArray arr = new JArray();

                if (IO.File.Exists(file))
                {
                    var txt = IO.File.ReadAllText(file);
                    if (!string.IsNullOrWhiteSpace(txt))
                        try { arr = JArray.Parse(txt); } catch { arr = new JArray(); }
                }

                bool exists = arr.Children<JObject>().Any(o =>
                    (o.Value<string>("id") != null && o.Value<string>("id").Equals(uid, StringComparison.OrdinalIgnoreCase)) ||
                    (o["ids"] != null && o["ids"].Any(t => t.ToString().Equals(uid, StringComparison.OrdinalIgnoreCase)))
                );

                if (exists)
                    return Content("{\"accsdb\": false}", "application/json; charset=utf-8");

                var obj = new JObject();
                obj["id"] = uid;
                obj["expires"] = DateTime.Now.AddDays(Math.Max(1, CoreInit.conf.accsdb.shared_daytime));

                arr.Add(obj);

                IO.File.WriteAllText(file, arr.ToString(Formatting.Indented));

                return Content("{\"accsdb\": false, \"success\": true, \"uid\": \"" + uid + "\"}", "application/json; charset=utf-8");
            }
            catch
            {
                return Content("{\"accsdb\": true}", "application/json; charset=utf-8");
            }
        }
        #endregion

        return Content("{\"accsdb\": false, \"success\": true}", "application/json; charset=utf-8");
    }
    #endregion


    #region app.min.js
    [HttpGet, AllowAnonymous]
    [Staticache(20, always: true)]
    [Route("/app.min.js")]
    [Route("{type}/app.min.js")]
    public ActionResult LampaApp(string type)
    {
        if (string.IsNullOrEmpty(type))
        {
            if (ModInit.conf.path != null)
            {
                type = ModInit.conf.path;
            }
            else
            {
                if (ModInit.conf.index == null || !ModInit.conf.index.Contains("/"))
                    return Content(string.Empty, "application/javascript; charset=utf-8");

                type = ModInit.conf.index.Split("/")[0];
            }
        }
        else
        {
            type = Regex.Replace(type, "[^a-z0-9\\-]", "");
        }

        string file = IO.File.ReadAllText($"wwwroot/{type}/app.min.js");

        #region appReplace
        if (ModInit.conf.appReplace != null)
        {
            string _host = Regex.Replace(host, "^https?://", "");

            foreach (var r in ModInit.conf.appReplace)
            {
                string val = r.Value;
                if (val.StartsWith("file:"))
                    val = IO.File.ReadAllText(val.Substring(5));

                val = val.Replace("{localhost}", host).Replace("{host}", _host);
                file = Regex.Replace(file, r.Key, val, RegexOptions.IgnoreCase);
            }
        }
        #endregion

        var bulder = new StringBuilder();
        bulder = bulder.Append(file);

        if (ModInit.conf.initPlugins.cubProxy)
        {
            bulder = bulder.Replace("protocol + mirror + '/api/checker'", $"'{host}/cub/api/checker'");

            string utlprotocol = file.Contains("Utils$1.protocol()") ?
                "Utils$1.protocol()" :
                "Utils$2.protocol()";

            bulder = bulder.Replace($"{utlprotocol} + 'tmdb.' + object$2.cub_domain + '/' + u,", $"'{host}/cub/tmdb./' + u,");
            bulder = bulder.Replace($"{utlprotocol} + object$2.cub_domain", $"'{host}/cub/red'");
            bulder = bulder.Replace("object$2.cub_domain", $"'{CoreInit.conf.cub.mirror}'");
        }

        bulder = bulder.Replace("http://lite.lampa.mx", $"{host}/{type}");
        bulder = bulder.Replace("https://yumata.github.io/lampa-lite", $"{host}/{type}");

        bulder = bulder.Replace("http://lampa.mx", $"{host}/{type}");
        bulder = bulder.Replace("https://yumata.github.io/lampa", $"{host}/{type}");

        bulder = bulder.Replace("window.lampa_settings.dcma = dcma;", "window.lampa_settings.fixdcma = true;");
        bulder = bulder.Replace("Storage.get('vpn_checked_ready', 'false')", "true");

        bulder = bulder.Replace("status$1 = false;", "status$1 = true;"); // local apk to personal.lampa
        bulder = bulder.Replace("return status$1;", "return true;"); // отключение рекламы
        bulder = bulder.Replace("if (!Storage.get('metric_uid', ''))", "return;"); // metric
        bulder = bulder.Replace("function log(data) {", "function log(data) { return;");
        bulder = bulder.Replace("function stat$1(method, name) {", "function stat$1(method, name) { return;");
        bulder = bulder.Replace("if (domain) {", "if (false) {");

        bulder = bulder.Replace("{localhost}", host);

        if (EventListener.AppReplace != null)
        {
            foreach (Func<string, EventAppReplace, StringBuilder> handler in EventListener.AppReplace.GetInvocationList())
                bulder = handler.Invoke("appjs", new EventAppReplace(bulder, null, type, host, requestInfo, HttpContext.Request));
        }

        return ContentTo(bulder, "application/javascript; charset=utf-8");
    }
    #endregion

    #region app.css
    [HttpGet, AllowAnonymous]
    [Staticache(20, always: true)]
    [Route("/css/app.css")]
    [Route("{type}/css/app.css")]
    public ActionResult LampaAppCss(string type)
    {
        if (string.IsNullOrEmpty(type))
        {
            if (ModInit.conf.path != null)
            {
                type = ModInit.conf.path;
            }
            else
            {
                if (ModInit.conf.index == null || !ModInit.conf.index.Contains("/"))
                    return Content(string.Empty, "text/css; charset=utf-8");

                type = ModInit.conf.index.Split("/")[0];
            }
        }
        else
        {
            type = Regex.Replace(type, "[^a-z0-9\\-]", "");
        }

        if (ModInit.conf.cssReplace == null && EventListener.AppReplace == null)
        {
            return File($"/{type}/css/app.css", "text/css; charset=utf-8");
        }
        else
        {
            string css = IO.File.ReadAllText($"wwwroot/{type}/css/app.css");

            if (ModInit.conf.cssReplace != null)
            {
                string _host = Regex.Replace(host, "^https?://", "");
                foreach (var r in ModInit.conf.cssReplace)
                {
                    string val = r.Value;
                    if (val.StartsWith("file:"))
                        val = IO.File.ReadAllText(val.Substring(5));

                    val = val.Replace("{localhost}", host).Replace("{host}", _host);
                    css = Regex.Replace(css, r.Key, val, RegexOptions.IgnoreCase);
                }
            }

            if (EventListener.AppReplace == null)
                return ContentTo(css, "text/css; charset=utf-8");

            var bulder = new StringBuilder();
            bulder = bulder.Append(css);

            if (EventListener.AppReplace != null)
            {
                foreach (Func<string, EventAppReplace, StringBuilder> handler in EventListener.AppReplace.GetInvocationList())
                    bulder = handler.Invoke("appcss", new EventAppReplace(bulder, null, type, host, requestInfo, HttpContext.Request));
            }

            return ContentTo(bulder, "text/css; charset=utf-8");
        }
    }
    #endregion


    #region MSX
    [HttpGet, AllowAnonymous]
    [Staticache(20, always: true, setHeadersNoCache: true)]
    [Route("msx/start.json")]
    public ActionResult MSX()
    {
        string msx = FileCache.ReadAllText($"{ModInit.modpath}/msx.json", "msx.json", saveCache: false)
            .Replace("{localhost}", host);

        return ContentTo(msx);
    }
    #endregion

    #region Widgets
    static readonly System.Threading.Lock WidgeLock = new();
    static readonly System.Threading.Lock IpkLock = new();

    string WidgetHost(string overwritehost)
    {
        if (string.IsNullOrWhiteSpace(overwritehost))
            return host;

        string value = overwritehost.Trim();

        if (!Regex.IsMatch(value, "^https?://", RegexOptions.IgnoreCase))
            value = "http://" + value;

        if (value.Length > 2048 || !Uri.TryCreate(value, UriKind.Absolute, out Uri uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrWhiteSpace(uri.Host))
            throw new ArgumentException("overwritehost: invalid URL");

        return uri.GetLeftPart(UriPartial.Authority);
    }

    [HttpGet, AllowAnonymous]
    [Route("samsung.wgt")]
    public ActionResult SamsWgt(string overwritehost, string tizen)
    {
        if (!ModInit.conf.widgets.samsung)
            return NotFound();

        SetHeadersNoCache();

        try
        {
            string replaceHost = WidgetHost(overwritehost);
            bool legacy = double.TryParse(tizen, System.Globalization.CultureInfo.InvariantCulture, out double tizenver) && tizenver <= 3;

            string cache = $"cache/widgets/samsung/{Shared.Services.Utilities.CrypTo.md5($"{replaceHost}|v5|{legacy}")}";
            string wgt = $"{cache}.wgt";

            lock (WidgeLock)
            {
                if (!IO.File.Exists(wgt))
                    BuildSamsWgt(cache, wgt, replaceHost, legacy);
            }

            Response.Headers["Content-Disposition"] = "attachment; filename=\"samsung.wgt\"";
            return File(IO.File.OpenRead(wgt), "application/octet-stream");
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "{Class} {CatchId}", nameof(ApiController), "id_samsung_wgt");
            return Content($"samsung.wgt build failed: {ex.Message}", "text/plain; charset=utf-8");
        }
    }

    void BuildSamsWgt(string cache, string wgt, string replaceHost, bool legacy)
    {
        var widgetDirectory = $"{ModInit.modpath}/widgets/samsung";
        var publishDirectory = $"{cache}/publish";

        try
        {
            IO.Directory.CreateDirectory(publishDirectory);

            foreach (string inFilePath in IO.Directory.GetFiles(widgetDirectory, "*", IO.SearchOption.AllDirectories))
            {
                string outFile = inFilePath.Replace(widgetDirectory, publishDirectory);
                IO.Directory.CreateDirectory(IO.Path.GetDirectoryName(outFile));
                IO.File.Copy(inFilePath, outFile, true);
            }

            string index = IO.File.ReadAllText($"{publishDirectory}/index.html");
            IO.File.WriteAllText($"{publishDirectory}/index.html", index.Replace("{localhost}", replaceHost));

            string loader = IO.File.ReadAllText($"{publishDirectory}/loader.js");
            IO.File.WriteAllText($"{publishDirectory}/loader.js", loader.Replace("{localhost}", replaceHost));

            string app = IO.File.ReadAllText($"{publishDirectory}/app.js");
            IO.File.WriteAllText($"{publishDirectory}/app.js", app.Replace("{localhost}", replaceHost));

            if (legacy)
            {
                string config = IO.File.ReadAllText($"{publishDirectory}/config.xml");


                config = Regex.Replace(config, @"[ \t]*<tizen:service\b[\s\S]*?</tizen:service>\r?\n", string.Empty);
                config = Regex.Replace(config, @"[ \t]*<tizen:metadata key=""http://samsung\.com/tv/metadata/use\.preview""[^>]*>\r?\n", string.Empty);

                if (config.Contains("tizen:service") || config.Contains("use.preview"))
                    throw new InvalidOperationException("legacy tizen: не удалось вырезать tizen:service из config.xml");

                IO.File.WriteAllText($"{publishDirectory}/config.xml", config);
            }

            string gethash(string file)
            {
                using (var sha = System.Security.Cryptography.SHA512.Create())
                {
                    return Convert.ToBase64String(sha.ComputeHash(IO.File.ReadAllBytes(file)));
                }
            }

            string indexhashsha512 = gethash($"{publishDirectory}/index.html");
            string loaderhashsha512 = gethash($"{publishDirectory}/loader.js");
            string apphashsha512 = gethash($"{publishDirectory}/app.js");
            string confighashsha512 = gethash($"{publishDirectory}/config.xml");
            string iconhashsha512 = gethash($"{publishDirectory}/icon.png");
            string logohashsha512 = gethash($"{publishDirectory}/logo_appname_fg.png");

            string author_sigxml = IO.File.ReadAllText($"{widgetDirectory}/author-signature.xml");
            author_sigxml = author_sigxml
                .Replace("loaderhashsha512", loaderhashsha512)
                .Replace("apphashsha512", apphashsha512)
                .Replace("iconhashsha512", iconhashsha512)
                .Replace("logohashsha512", logohashsha512)
                .Replace("confighashsha512", confighashsha512)
                .Replace("indexhashsha512", indexhashsha512);

            IO.File.WriteAllText($"{publishDirectory}/author-signature.xml", author_sigxml);

            string authorsignaturehashsha512 = gethash($"{publishDirectory}/author-signature.xml");
            string sigxml1 = IO.File.ReadAllText($"{publishDirectory}/signature1.xml");
            sigxml1 = sigxml1
                .Replace("loaderhashsha512", loaderhashsha512)
                .Replace("apphashsha512", apphashsha512)
                .Replace("confighashsha512", confighashsha512)
                .Replace("authorsignaturehashsha512", authorsignaturehashsha512)
                .Replace("iconhashsha512", iconhashsha512)
                .Replace("logohashsha512", logohashsha512)
                .Replace("indexhashsha512", indexhashsha512);

            IO.File.WriteAllText($"{publishDirectory}/signature1.xml", sigxml1);

            IO.Compression.ZipFile.CreateFromDirectory(publishDirectory, $"{wgt}.tmp");
            IO.File.Move($"{wgt}.tmp", wgt, true);
        }
        finally
        {
            if (IO.Directory.Exists(cache))
                IO.Directory.Delete(cache, true);

            if (IO.File.Exists($"{wgt}.tmp"))
                IO.File.Delete($"{wgt}.tmp");
        }
    }

    [HttpGet]
    [AllowAnonymous]
    [Route("lg.ipk")]
    public ActionResult LgIpk(string overwritehost)
    {
        if (!ModInit.conf.widgets.lg)
            return NotFound();

        SetHeadersNoCache();

        try
        {
            string replaceHost = WidgetHost(overwritehost);

            string cache = $"cache/widgets/lg/{Shared.Services.Utilities.CrypTo.md5($"{replaceHost}|v02")}";
            string ipk = $"{cache}.ipk";

            if (!IO.File.Exists(ipk))
            {
                var widgetDirectory = $"{ModInit.modpath}/widgets/lg";

                if (!IO.Directory.Exists(widgetDirectory) ||
                    !IO.Directory.Exists($"{widgetDirectory}/app") ||
                    !IO.File.Exists($"{widgetDirectory}/service.tar.gz") ||
                    !IO.File.Exists($"{widgetDirectory}/control/control"))
                    return NotFound();

                lock (IpkLock)
                {
                    if (!IO.File.Exists(ipk))
                        BuildLgIpk(ipk, replaceHost);
                }
            }

            Response.Headers["Content-Disposition"] = "attachment; filename=\"lg.ipk\"";
            return File(IO.File.OpenRead(ipk), "application/octet-stream");
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "{Class} {CatchId}", nameof(ApiController), "id_lg_ipk");
            return Content($"lg.ipk build failed: {ex.Message}", "text/plain; charset=utf-8");
        }
    }

    void BuildLgIpk(string ipk, string replaceHost)
    {
        var widgetDirectory = $"{ModInit.modpath}/widgets/lg";

        const string appId = "com.lampac.tv";
        const string serviceId = appId + ".worker";

        string appSource = $"{widgetDirectory}/app";
        string serviceArchive = $"{widgetDirectory}/service.tar.gz";
        string controlSource = $"{widgetDirectory}/control";

        #region packageinfo.json (mandatory for webOS)
        string appVersion = "1.0.0";
        try
        {
            var appInfo = JObject.Parse(IO.File.ReadAllText($"{appSource}/appinfo.json"));
            appVersion = appInfo.Value<string>("version") ?? appVersion;
        }
        catch { }

        byte[] packageInfoData = Encoding.UTF8.GetBytes(new JObject
        {
            ["id"] = appId,
            ["version"] = appVersion,
            ["app"] = appId,
            ["services"] = new JArray(serviceId)
        }.ToString(Formatting.Indented));
        #endregion

        // app files patched with the host (root level only, like the legacy build)
        var hostPatchedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "index.html", "loader.js", "app.js" };

        long installedSize = 0;

        // data.tar.gz — packed straight from the source folders, no intermediate copy
        byte[] dataTarGz = BuildTarGz(tar =>
        {
            void AddTree(string source, string prefix, bool patch)
            {
                foreach (string file in IO.Directory.GetFiles(source, "*", IO.SearchOption.AllDirectories))
                {
                    string rel = IO.Path.GetRelativePath(source, file).Replace('\\', '/');
                    string entryName = $"{prefix}/{rel}";

                    if (patch && !rel.Contains('/') && hostPatchedFiles.Contains(rel))
                    {
                        byte[] data = Encoding.UTF8.GetBytes(IO.File.ReadAllText(file).Replace("{localhost}", replaceHost));
                        WriteTarBytes(tar, entryName, data);
                        installedSize += data.Length;
                    }
                    else
                    {
                        tar.WriteEntry(file, entryName);
                        installedSize += new IO.FileInfo(file).Length;
                    }
                }
            }

            // The worker service ships as a single archive (1 file instead of ~1900 loose
            // files) — this keeps build-copy and antivirus on-access scanning cheap.
            void AddArchive(string archive, string prefix)
            {
                using var fs = IO.File.OpenRead(archive);
                using var gzip = new GZipStream(fs, CompressionMode.Decompress);
                using var reader = new TarReader(gzip);

                while (reader.GetNextEntry() is TarEntry entry)
                {
                    if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile))
                        continue;

                    var data = new IO.MemoryStream();
                    entry.DataStream?.CopyTo(data); // null for empty files (e.g. 0-byte .gitmodules)
                    data.Position = 0;

                    WriteTarStream(tar, $"{prefix}/{entry.Name}", data);
                    installedSize += data.Length;
                }
            }

            AddTree(appSource, $"usr/palm/applications/{appId}", patch: true);
            AddArchive(serviceArchive, $"usr/palm/services/{serviceId}");

            WriteTarBytes(tar, $"usr/palm/packages/{appId}/packageinfo.json", packageInfoData);
            installedSize += packageInfoData.Length;
        });

        // control.tar.gz — Installed-Size patched in memory
        byte[] controlTarGz = BuildTarGz(tar =>
        {
            foreach (string file in IO.Directory.GetFiles(controlSource, "*", IO.SearchOption.AllDirectories))
            {
                string rel = IO.Path.GetRelativePath(controlSource, file).Replace('\\', '/');

                if (rel.Equals("control", StringComparison.OrdinalIgnoreCase))
                {
                    string text = Regex.Replace(IO.File.ReadAllText(file), "(?m)^Installed-Size:.*$", $"Installed-Size: {installedSize}");
                    WriteTarBytes(tar, rel, Encoding.UTF8.GetBytes(text));
                }
                else
                {
                    tar.WriteEntry(file, rel);
                }
            }
        });

        // debian-binary must be exactly "2.0\n" (LF) — normalize in case the on-disk file has CRLF
        byte[] debianBinary = IO.File.Exists($"{widgetDirectory}/debian-binary")
            ? Encoding.ASCII.GetBytes(IO.File.ReadAllText($"{widgetDirectory}/debian-binary").Replace("\r\n", "\n").Replace("\r", "\n"))
            : Encoding.ASCII.GetBytes("2.0\n");

        IO.Directory.CreateDirectory(IO.Path.GetDirectoryName(ipk));

        try
        {
            using (var stream = IO.File.Create($"{ipk}.tmp"))
            using (var writer = new IO.BinaryWriter(stream, Encoding.ASCII, leaveOpen: false))
            {
                writer.Write(Encoding.ASCII.GetBytes("!<arch>\n"));
                WriteArFile(writer, "debian-binary", debianBinary);
                WriteArFile(writer, "control.tar.gz", controlTarGz);
                WriteArFile(writer, "data.tar.gz", dataTarGz);
            }

            IO.File.Move($"{ipk}.tmp", ipk, true);
        }
        finally
        {
            if (IO.File.Exists($"{ipk}.tmp"))
                IO.File.Delete($"{ipk}.tmp");
        }

        return;

        static byte[] BuildTarGz(Action<TarWriter> write)
        {
            using var ms = new IO.MemoryStream();
            using (var gzip = new GZipStream(ms, CompressionLevel.Optimal, leaveOpen: true))
            using (var tar = new TarWriter(gzip, TarEntryFormat.Ustar, leaveOpen: true))
            {
                write(tar);
            }

            return ms.ToArray();
        }

        static void WriteTarBytes(TarWriter tar, string entryName, byte[] data)
            => WriteTarStream(tar, entryName, new IO.MemoryStream(data));

        static void WriteTarStream(TarWriter tar, string entryName, IO.Stream data)
        {
            var entry = new UstarTarEntry(TarEntryType.RegularFile, entryName)
            {
                Mode = IO.UnixFileMode.UserRead | IO.UnixFileMode.UserWrite |
                       IO.UnixFileMode.GroupRead | IO.UnixFileMode.OtherRead,
                DataStream = data
            };

            tar.WriteEntry(entry);
        }

        static void WriteArFile(IO.BinaryWriter writer, string name, byte[] data)
        {
            string fileName = name.EndsWith("/") ? name : $"{name}/";

            string header =
                PadRight(fileName, 16) +
                PadRight("0", 12) +
                PadRight("0", 6) +
                PadRight("0", 6) +
                PadRight("100644", 8) +
                PadRight(data.Length.ToString(), 10) +
                "`\n";

            writer.Write(Encoding.ASCII.GetBytes(header));
            writer.Write(data);

            if (data.Length % 2 != 0)
                writer.Write((byte)'\n');

            static string PadRight(string value, int len)
            {
                if (value.Length > len)
                    return value.Substring(0, len);

                return value.PadRight(len, ' ');
            }
        }
    }

    #endregion

    #region lampainit.js
    [HttpGet, AllowAnonymous]
    [Staticache(20, always: true, setHeadersNoCache: true)]
    [Route("lampainit.js")]
    public ActionResult LamInit()
    {
        var sb = StringBuilderPool.Rent();

        try
        {
            string lampainitjs = FileCache.ReadAllText($"{ModInit.modpath}/plugins/lampainit.js", "lampainit.js");
            if (lampainitjs.Contains("{country}"))
                StatiCacheDisabled = true;

            sb = sb.Append(lampainitjs);

            #region plugins
            var plugins = LampaPluginBuilder.BuildInitPlugins(
                ModInit.conf.initPlugins,
                ModInit.conf.customPlugins);

            sb = sb.Replace("{initiale}", JsonConvert.SerializeObject(plugins));
            #endregion

            if (ModInit.conf.initPlugins.pirate_store)
            {
                string storejs = FileCache.ReadAllText($"{ModInit.modpath}/plugins/pirate_store.js", "pirate_store.js");
                if (storejs.Contains("{country}"))
                    StatiCacheDisabled = true;

                sb = sb.Replace("{pirate_store}", storejs);
            }

            if (CoreInit.conf.accsdb.enable)
            {
                string script;
                var gate = ModInit.conf.telegramAuthGate;
                if (gate != null && gate.enabled && !string.IsNullOrWhiteSpace(gate.botUsername))
                {
                    script = TelegramGateJs();
                }
                else
                {
                    if (gate != null && gate.enabled && string.IsNullOrWhiteSpace(gate.botUsername))
                        Console.WriteLine("LampaWeb.telegramAuthGate: enabled=true, но botUsername пустой — откат на deny.js (гейт без имени бота неавторизуем).");

                    script = FileCache.ReadAllText($"{ModInit.modpath}/plugins/deny.js", "deny.js");
                    if (script.Contains("{country}"))
                        StatiCacheDisabled = true;

                    if (script.Contains("{cubMesage}"))
                        script = script.Replace("{cubMesage}", CoreInit.conf.accsdb.authMesage);
                }

                sb = sb.Replace("{deny}", script);
            }

            string initinvcjs = FileCache.ReadAllText($"{ModInit.modpath}/plugins/lampainit-invc.js", "lampainit-invc.js");
            if (initinvcjs.Contains("{country}"))
                StatiCacheDisabled = true;

            sb = sb.Replace("{lampainit-invc}", initinvcjs);
            sb = sb.Replace("{country}", requestInfo.Country ?? string.Empty);
            sb = sb.Replace("{localhost}", host);
            sb = sb.Replace("{deny}", string.Empty);
            sb = sb.Replace("{pirate_store}", string.Empty);

            sb = sb.Replace("{ major: 0, minor: 0 }", $"{{major: 2, minor: 1}}");

            if (ModInit.conf.initPlugins.jacred)
                sb = sb.Replace("{jachost}", Regex.Replace(host, "^https?://", ""));
            else
                sb = sb.Replace("{jachost}", "jac.red");

            #region full_btn_priority_hash
            var onlineModule = CoreInit.modules?.FirstOrDefault(m => m != null && string.Equals(m.name, "Online", StringComparison.OrdinalIgnoreCase));
            string online_plugin_path = string.IsNullOrWhiteSpace(onlineModule?.path)
                ? null
                : IO.Path.Combine(onlineModule.path, "plugin.js");

            string online_version = string.IsNullOrEmpty(online_plugin_path)
                ? string.Empty
                : Regex.Match(FileCache.ReadAllText(online_plugin_path, "online.js"), "version: '([^']+)'").Groups[1].Value;

            string LampaUtilshash(string input)
            {
                if (!CoreInit.conf.online.version)
                    input = input.Replace($"v{online_version}", "");

                string str = input ?? string.Empty;
                int hash = 0;

                if (str.Length == 0)
                    return hash.ToString();

                for (int i = 0; i < str.Length; i++)
                {
                    int _char = str[i];

                    hash = (hash << 5) - hash + _char;
                    hash = hash & hash; // Преобразование в 32-битное целое число
                }

                return Math.Abs(hash).ToString();
            }

            string full_btn_priority_hash = LampaUtilshash($"<div class=\"full-start__button selector view--online lampac--button\" data-subtitle=\"{CoreInit.conf.online.name} v{online_version}\">\n        <svg xmlns=\"http://www.w3.org/2000/svg\" version=\"1.1\" xmlns:xlink=\"http://www.w3.org/1999/xlink\" viewBox=\"0 0 392.697 392.697\" xml:space=\"preserve\">\n            <path d=\"M21.837,83.419l36.496,16.678L227.72,19.886c1.229-0.592,2.002-1.846,1.98-3.209c-0.021-1.365-0.834-2.592-2.082-3.145\n                L197.766,0.3c-0.903-0.4-1.933-0.4-2.837,0L21.873,77.036c-1.259,0.559-2.073,1.803-2.081,3.18\n                C19.784,81.593,20.584,82.847,21.837,83.419z\" fill=\"currentColor\"></path>\n            <path d=\"M185.689,177.261l-64.988-30.01v91.617c0,0.856-0.44,1.655-1.167,2.114c-0.406,0.257-0.869,0.386-1.333,0.386\n                c-0.368,0-0.736-0.082-1.079-0.244l-68.874-32.625c-0.869-0.416-1.421-1.293-1.421-2.256v-92.229L6.804,95.5\n                c-1.083-0.496-2.344-0.406-3.347,0.238c-1.002,0.645-1.608,1.754-1.608,2.944v208.744c0,1.371,0.799,2.615,2.045,3.185\n                l178.886,81.768c0.464,0.211,0.96,0.315,1.455,0.315c0.661,0,1.318-0.188,1.892-0.555c1.002-0.645,1.608-1.754,1.608-2.945\n                V180.445C187.735,179.076,186.936,177.831,185.689,177.261z\" fill=\"currentColor\"></path>\n            <path d=\"M389.24,95.74c-1.002-0.644-2.264-0.732-3.347-0.238l-178.876,81.76c-1.246,0.57-2.045,1.814-2.045,3.185v208.751\n                c0,1.191,0.606,2.302,1.608,2.945c0.572,0.367,1.23,0.555,1.892,0.555c0.495,0,0.991-0.104,1.455-0.315l178.876-81.768\n                c1.246-0.568,2.045-1.813,2.045-3.185V98.685C390.849,97.494,390.242,96.384,389.24,95.74z\" fill=\"currentColor\"></path>\n            <path d=\"M372.915,80.216c-0.009-1.377-0.823-2.621-2.082-3.18l-60.182-26.681c-0.938-0.418-2.013-0.399-2.938,0.045\n                l-173.755,82.992l60.933,29.117c0.462,0.211,0.958,0.316,1.455,0.316s0.993-0.105,1.455-0.316l173.066-79.092\n                C372.122,82.847,372.923,81.593,372.915,80.216z\" fill=\"currentColor\"></path>\n        </svg>\n\n        <span>Онлайн</span>\n    </div>");

            sb = sb.Replace("{full_btn_priority_hash}", full_btn_priority_hash)
                   .Replace("{btn_priority_forced}", CoreInit.conf.online.btn_priority_forced ? "true" : "false");
            #endregion

            #region domain token
            if (!string.IsNullOrEmpty(CoreInit.conf.accsdb.domainId_pattern))
            {
                string token = Regex.Match(HttpContext.Request.Host.Host, CoreInit.conf.accsdb.domainId_pattern).Groups[1].Value;
                sb = sb.Replace("{token}", token);
            }
            else
            {
                sb = sb.Replace("{token}", string.Empty);
            }
            #endregion

            return ContentTo(sb, "application/javascript; charset=utf-8");
        }
        finally
        {
            StringBuilderPool.Return(sb);
        }
    }
    #endregion

    #region on.js
    [HttpGet, AllowAnonymous]
    [Staticache(20, always: true, setHeadersNoCache: true)]
    [Route("on.js")]
    [Route("on/js/{token}")]
    [Route("on/h/{token}")]
    [Route("on/{token}")]
    public ActionResult LamOnInit(string token, bool adult = true)
    {
        var sb = StringBuilderPool.Rent();

        try
        {
            #region plugins
            if (adult && HttpContext.Request.Path.Value.StartsWith("/on/h/"))
                adult = false;

            var plugins = LampaPluginBuilder.BuildOnPluginUrls(
                ModInit.conf.initPlugins,
                ModInit.conf.customPlugins,
                token,
                adult);
            #endregion

            string onjs = FileCache.ReadAllText($"{ModInit.modpath}/plugins/on.js", "on.js");
            if (string.IsNullOrEmpty(onjs))
                return BadRequest();

            if (onjs.Contains("{country}"))
                StatiCacheDisabled = true;

            sb = sb.Append(onjs)
                .Replace("{plugins}", plugins.Count == 0 ? string.Empty : string.Join(",", plugins))
                .Replace("{country}", requestInfo.Country ?? string.Empty)
                .Replace("{localhost}", host);

            return ContentTo(sb, "application/javascript; charset=utf-8");
        }
        finally
        {
            StringBuilderPool.Return(sb);
        }
    }
    #endregion

    #region privateinit.js
    [HttpGet]
    [Route("privateinit.js")]
    public ActionResult PrivateInit()
    {
        SetHeadersNoCache();

        var user = requestInfo.user;
        if (user == null || user.ban || DateTime.UtcNow > user.expires)
            return Content(string.Empty, "application/javascript; charset=utf-8");

        var sb = StringBuilderPool.Rent();

        try
        {
            string privateinit = FileCache.ReadAllText($"{ModInit.modpath}/plugins/privateinit.js", "privateinit.js");

            sb.Append(privateinit)
              .Replace("{country}", requestInfo.Country ?? string.Empty)
              .Replace("{localhost}", host)
              .Replace("{jachost}", ModInit.conf.initPlugins.jacred ? Regex.Replace(host, "^https?://", "") : "jac.red");

            return ContentTo(sb, "application/javascript; charset=utf-8");
        }
        finally
        {
            StringBuilderPool.Return(sb);
        }
    }
    #endregion

    private string TelegramGateJs()
    {
        var g = ModInit.conf.telegramAuthGate;
        string raw = FileCache.ReadAllText($"{ModInit.modpath}/plugins/telegram_auth_gate.js", "telegram_auth_gate.js");
        if (raw.Contains("{country}"))
            StatiCacheDisabled = true; // defensive parity с deny.js; в файле гейта {country} нет

        string bot = HttpUtility.JavaScriptStringEncode(g?.botUsername ?? string.Empty);
        string svc = HttpUtility.JavaScriptStringEncode(g?.serviceName ?? string.Empty);

        // {localhost}, {country}, {token} подставляются глобально (688-689, 738-748) или в маршруте
        return raw
            .Replace("{botUsername}", bot)
            .Replace("{serviceName}", svc);
    }

    #region telegram_auth_gate.js
    [HttpGet, AllowAnonymous, Staticache(manually: true)]
    [Route("telegram_auth_gate.js")]
    public ActionResult TelegramAuthGate()
    {
        SetHeadersNoCache();

        string token = string.IsNullOrEmpty(CoreInit.conf.accsdb.domainId_pattern)
            ? string.Empty
            : Regex.Match(HttpContext.Request.Host.Host, CoreInit.conf.accsdb.domainId_pattern).Groups[1].Value;

        string gate = TelegramGateJs()
            .Replace("{country}", requestInfo.Country ?? string.Empty)
            .Replace("{localhost}", host)
            .Replace("{token}", HttpUtility.UrlEncode(token));

        return ContentTo(gate, "application/javascript; charset=utf-8");
    }
    #endregion

    #region dorama.js
    [HttpGet, AllowAnonymous, Staticache(manually: true)]
    [Route("dorama.js")]
    [Route("dorama/js/{token}")]
    public ActionResult Dorama(string token)
    {
        SetHeadersNoCache();

        string plugin = FileCache.ReadAllText($"{ModInit.modpath}/plugins/dorama.js", "dorama.js")
            .Replace("{localhost}", host)
            .Replace("{token}", HttpUtility.UrlEncode(token ?? string.Empty));

        return ContentTo(plugin, "application/javascript; charset=utf-8");
    }
    #endregion
}
