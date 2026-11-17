using Microsoft.AspNetCore.Http;
using Shared;
using Shared.Models.Base;
using System;
using System.Linq;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;

namespace TorrServer;

// Native accsdb identities, not TorrServer's private service password.
public static class MediaAccess
{
    public static AccsUser User(HttpContext context)
    {
        var user = CoreInit.conf.accsdb.findUser(context, out _);
        if (user == null && AuthenticationHeaderValue.TryParse(context.Request.Headers.Authorization, out var auth)
            && auth.Scheme.Equals("Basic", StringComparison.OrdinalIgnoreCase) && auth.Parameter != null)
        {
            try
            {
                string credentials = new UTF8Encoding(false, true).GetString(Convert.FromBase64String(auth.Parameter));
                int separator = credentials.IndexOf(':');
                if (separator > 0 && credentials[(separator + 1)..] == ModInit.conf.defaultPasswd)
                    user = CoreInit.conf.accsdb.findUser(credentials[..separator]);
            }
            catch (FormatException) { }
            catch (DecoderFallbackException) { }
        }

        // Native configs and Telegram store local wall time; Unspecified therefore means local, not UTC.
        return user != null && !user.ban && user.expires.ToUniversalTime() > DateTime.UtcNow && user.group >= ModInit.conf.group
            ? user : null;
    }

    static string Identity(AccsUser user)
    {
        bool Matches(string id) => !string.IsNullOrWhiteSpace(id) && ReferenceEquals(CoreInit.conf.accsdb.findUser(id), user);
        return Matches(user.id) ? user.id : user.ids?.FirstOrDefault(Matches);
    }

    static bool IsMedia(string path)
        => Regex.IsMatch(path, "^/(stream|playlist|play|download|gst)(/|$)", RegexOptions.IgnoreCase);

    public static string Url(string value, Uri upstream, Uri publicBase, AccsUser user)
    {
        if (!Uri.TryCreate(upstream, value, out var uri) || !string.IsNullOrEmpty(uri.UserInfo))
            return value;

        bool ownUpstream = uri.Scheme == upstream.Scheme && uri.Authority == upstream.Authority;
        bool ownPublic = uri.Scheme == publicBase.Scheme && uri.Authority == publicBase.Authority;
        string path = uri.AbsolutePath;
        if ((ownPublic || ownUpstream) && path.StartsWith("/ts/", StringComparison.OrdinalIgnoreCase))
            path = path[3..];
        else if (!ownUpstream && !ownPublic)
            return value;

        if (!IsMedia(path))
            return value;

        string query = uri.Query.TrimStart('?');
        if (user != null)
        {
            // Replace all aliases: findUser checks token before uid, including stale playlist credentials.
            query = string.Join("&", query.Split('&', StringSplitOptions.RemoveEmptyEntries).Where(part =>
                !Regex.IsMatch(Uri.UnescapeDataString(part.Split('=')[0]), "^(uid|token|account_email|box_mac|lcrqpasswd)$", RegexOptions.IgnoreCase)));
            string identity = Identity(user);
            if (!string.IsNullOrEmpty(identity))
                query += (query.Length > 0 ? "&" : "") + "uid=" + Uri.EscapeDataString(identity);
        }
        return "/ts" + path + (query.Length > 0 ? "?" + query : "") + uri.Fragment;
    }

    public static string Playlist(string body, Uri upstream, Uri publicBase, AccsUser user)
        => Regex.Replace(body ?? string.Empty, @"[^\r\n]+", line =>
        {
            if (line.Value.StartsWith('#'))
                return Regex.Replace(line.Value, "(?<=URI=\")[^\"]*", uri => Url(uri.Value, upstream, publicBase, user));

            string value = line.Value.Trim();
            return value.Length == 0 ? line.Value : line.Value.Replace(value, Url(value, upstream, publicBase, user));
        });
}
