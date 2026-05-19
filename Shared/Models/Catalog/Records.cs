using HtmlAgilityPack;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json.Linq;

namespace Shared.Models.Catalog;

public record CatalogPlaylist(CatalogSettings init, string plugin, string host, string html, HtmlDocument doc, List<PlaylistItem> playlists);

public record CatalogChangePlaylis(CatalogSettings init, string plugin, string host, string html, HtmlNodeCollection nodes, PlaylistItem pl, HtmlNode row);

public record CatalogPlaylistJson(CatalogSettings init, string plugin, string host, string html, JToken json, List<PlaylistItem> playlists);

public record CatalogChangePlaylisJson(CatalogSettings init, string plugin, string host, string html, IEnumerable<JToken> nodes, PlaylistItem pl, JToken row);

public record CatalogGlobalsMenuRoute(string host, string plugin, string args, string url, string search, string cat, string sort, IQueryCollection query, int page);

public record CatalogNodeValue(string value, string host);

public record CatalogInitUrlCard(string host, string args, string uri, IQueryCollection query, string type);

public record CatalogInitHeader(string url, IReadOnlyList<HeadersModel> headers);
