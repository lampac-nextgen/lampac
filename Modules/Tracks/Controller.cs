using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Attributes;
using Shared.Services;
using Shared.Services.Utilities;
using System;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace Tracks;

public class TracksController : BaseController
{
    [HttpGet, AllowAnonymous]
    [Staticache(cacheMinutes: 10, always: true, setHeadersNoCache: true)]
    [Route("tracks.js")]
    [Route("tracks/js/{token}")]
    public ActionResult Tracks(string token)
    {
        string plugin = FileCache.ReadAllText($"{ModInit.modpath}/plugin.js", "tracks.js")
            .Replace("{localhost}", host)
            .Replace("{token}", HttpUtility.UrlEncode(token));

        return ContentTo(plugin, "application/javascript; charset=utf-8");
    }


    [HttpGet]
    [Route("ffprobe")]
    async public Task<ActionResult> Ffprobe(string media, bool showerror)
    {
        if (string.IsNullOrWhiteSpace(media) || !media.StartsWith("http") || media.Contains("/transcoding/"))
            return ContentTo("{}");

        string magnethash = null;

        if (media.Contains("/dlna/stream"))
        {
            string path = Regex.Match(media, "\\?path=([^&]+)").Groups[1].Value;
            if (string.IsNullOrWhiteSpace(path))
                return ContentTo(showerror ? "path" : "{}");

            magnethash = HttpUtility.UrlDecode(path);
        }
        else if (media.Contains("/stream/") || media.Contains("/lite/pidtor/"))
        {
            media = Regex.Replace(media, "[^a-z0-9_:\\-\\/\\.\\=\\?\\&\\%\\@]+", "", RegexOptions.IgnoreCase);

            if (media.Contains("/stream/") && ModInit.conf.tsuri != null)
                media = Regex.Replace(media, "^https?://[^/]+", ModInit.conf.tsuri, RegexOptions.IgnoreCase);
        }
        else if (media.Contains("/proxy/") && media.Contains(".mkv"))
        {
            string hash = Regex.Match(media, "/proxy/([^\n\r]+\\.mkv)").Groups[1].Value;
            media = ProxyLink.Decrypt(hash.AsSpan(), null)?.uri;
            if (string.IsNullOrWhiteSpace(media))
                return ContentTo(showerror ? "media" : "{}");
        }

        string argumentList = string.Empty;

        string memKey = $"tracks:ffprobe:{media}";
        if (!hybridCache.TryGetValue(memKey, out string outPut))
        {
            #region getFolder
            static string getFolder(string magnethash)
            {
                return $"database/tracks/{magnethash}";
            }
            #endregion

            if (media.Contains("/stream/"))
            {
                magnethash = Regex.Match(media, "link=([a-z0-9]+)").Groups[1].Value;
                string index = Regex.Match(media, @"index=([0-9]+)", RegexOptions.IgnoreCase).Groups[1].Value;

                if (!string.IsNullOrWhiteSpace(magnethash))
                    magnethash = $"{magnethash}_{index}";
            }
            else if (media.Contains("/lite/pidtor/"))
            {
                magnethash = Regex.Match(media, "/lite/pidtor/s([a-z0-9]+)").Groups[1].Value;
                string index = Regex.Match(media, @"tsid=([0-9]+)", RegexOptions.IgnoreCase).Groups[1].Value;

                if (!string.IsNullOrWhiteSpace(magnethash))
                    magnethash = $"{magnethash}_{index}";
            }

            if (string.IsNullOrEmpty(magnethash))
                magnethash = media;

            // A cache key must never remain a user-controlled filesystem path.
            magnethash = CrypTo.md5(magnethash);

            if (System.IO.File.Exists(getFolder(magnethash)))
                outPut = await BrotliTo.DecompressAsync(getFolder(magnethash));

            if (string.IsNullOrWhiteSpace(outPut))
            {
                if (!Uri.TryCreate(media, UriKind.Absolute, out var uri) ||
                    (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                    return ContentTo(showerror ? "uri" : "{}");

                var process = new System.Diagnostics.Process();
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.StandardOutputEncoding = Encoding.UTF8;
                process.StartInfo.FileName = CoreInit.Win32NT ? "data/ffprobe.exe" : System.IO.File.Exists("data/ffprobe") ? "data/ffprobe" : "ffprobe";

                process.StartInfo.ArgumentList.Add("-v");
                process.StartInfo.ArgumentList.Add("quiet");
                process.StartInfo.ArgumentList.Add("-print_format");
                process.StartInfo.ArgumentList.Add("json");
                process.StartInfo.ArgumentList.Add("-show_format");
                process.StartInfo.ArgumentList.Add("-show_streams");
                process.StartInfo.ArgumentList.Add(AccsDbInvk.Args(uri.AbsoluteUri, HttpContext));

                argumentList = process.StartInfo.FileName + " " + string.Join(" ", process.StartInfo.ArgumentList);

                process.Start();

                outPut = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (outPut == null)
                    outPut = string.Empty;

                if (Regex.Replace(outPut, "[\n\r\t ]+", "") == "{}")
                    outPut = string.Empty;

                if (!string.IsNullOrWhiteSpace(outPut) && !string.IsNullOrWhiteSpace(magnethash))
                {
                    await BrotliTo.CompressAsync(getFolder(magnethash), outPut);
                }
                else
                {
                    // заглушка
                    hybridCache.Set(memKey, outPut, DateTime.Now.AddMinutes(5), inmemory: true);
                }
            }
        }

        return ContentTo(string.IsNullOrEmpty(outPut) ? (showerror ? argumentList : "{}") : outPut);
    }
}
