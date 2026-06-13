using HtmlAgilityPack;
using Microsoft.IO;
using Shared.Models.CSharpGlobals;
using Shared.Models.SISI.NextHUB;
using Shared.Services.Pools;
using System.Text;

namespace NextHUB;

public partial class ListController
{
    #region goPlaylist
    public static List<PlaylistItem> goPlaylist(RequestModel requestInfo, string host, ContentParseSettings parse, NxtSettings init, string html, RecyclableMemoryStream msm, string plugin)
    {
        if (parse == null)
            return null;

        #region HtmlDocument
        var doc = new HtmlDocument();

        if (msm != null && msm.Length > 0)
        {
            doc.Load(msm);

            if (init.debug)
                Console.WriteLine(Encoding.UTF8.GetString(msm.ToArray()));

            msm.Position = 0;
        }
        else
        {
            if (string.IsNullOrEmpty(html))
                return null;

            if (init.debug)
                Console.WriteLine(html);

            doc.LoadHtml(html);
        }
        #endregion

        #region eval
        string eval = parse.eval;
        if (!string.IsNullOrEmpty(eval) && eval.EndsWith(".ncs"))
            eval = FileCache.ReadAllText($"{ModInit.modpath}/sites/{eval}");

        if (string.IsNullOrEmpty(parse.nodes))
        {
            if (string.IsNullOrEmpty(eval))
                return null;

            if (msm != null && msm.Length > 0)
                html = OwnerTo.String(msm, Encoding.UTF8);

            return CSharpEval.Execute<List<PlaylistItem>>(eval, new NxtPlaylist(init, plugin, host, html, doc, new List<PlaylistItem>()), Root.playlistOptions);
        }
        #endregion

        var nodes = doc.DocumentNode.SelectNodes(parse.nodes);
        if (nodes == null || nodes.Count == 0)
            return null;

        var playlists = new List<PlaylistItem>(nodes.Count);

        foreach (var row in nodes)
        {
            #region nodeValue
            string nodeValue(SingleNodeSettings nd)
            {
                string value = null;

                if (nd != null)
                {
                    if (string.IsNullOrEmpty(nd.node) && (!string.IsNullOrEmpty(nd.attribute) || nd.attributes != null))
                    {
                        if (nd.attributes != null)
                        {
                            foreach (var attr in nd.attributes)
                            {
                                var attrValue = row.GetAttributeValue(attr, null);
                                if (!string.IsNullOrEmpty(attrValue))
                                {
                                    value = attrValue;
                                    break;
                                }
                            }
                        }
                        else
                        {
                            value = row.GetAttributeValue(nd.attribute, null);
                        }
                    }
                    else
                    {
                        var inNode = row.SelectSingleNode(nd.node);
                        if (inNode != null)
                        {
                            if (nd.attributes != null)
                            {
                                foreach (var attr in nd.attributes)
                                {
                                    var attrValue = inNode.GetAttributeValue(attr, null);
                                    if (!string.IsNullOrEmpty(attrValue))
                                    {
                                        value = attrValue;
                                        break;
                                    }
                                }
                            }
                            else
                            {
                                value = (!string.IsNullOrEmpty(nd.attribute) ? inNode.GetAttributeValue(nd.attribute, null) : inNode.InnerText)?.Trim();
                            }
                        }
                    }
                }

                if (string.IsNullOrEmpty(value))
                    return null;

                if (nd.format != null)
                    return CSharpEval.Execute<string>($"return $\"{nd.format}\";", new NxtNodeValue(value, host));

                return value;
            }
            #endregion

            string name = nodeValue(parse.name);
            string href = nodeValue(parse.href);
            string img = nodeValue(parse.img);
            string duration = nodeValue(parse.duration);
            string quality = nodeValue(parse.quality);
            string preview = nodeValue(parse.preview);

            #region model
            ModelItem model = null;
            if (parse.model != null)
            {
                string mname = nodeValue(parse.model.name);
                string mhref = nodeValue(parse.model.href);

                if (!string.IsNullOrEmpty(mname) && !string.IsNullOrEmpty(mhref))
                {
                    model = new ModelItem()
                    {
                        name = mname,
                        uri = $"nexthub?plugin={AesTo.Encrypt(plugin)}&model={AesTo.Encrypt(mhref)}"
                    };
                }
            }
            #endregion

            #region args
            string args = string.Empty;

            if (parse.args != null)
            {
                foreach (var a in parse.args)
                {
                    string arg = nodeValue(a);
                    if (!string.IsNullOrEmpty(arg))
                        args += $"&{a.name}={AesTo.Encrypt(arg)}";
                }
            }
            #endregion

            if (init.debug)
                Console.WriteLine($"\n\nname: {name}\nhref: {href}\nimg: {img}\nduration: {duration}\nquality: {quality}\nmyarg: {args}\n\n{row.OuterHtml}");

            if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(href))
            {
                #region href
                if (href.StartsWith("../"))
                    href = $"{init.host}/{href.Replace("../", "")}";
                else if (href.StartsWith("//"))
                    href = $"https:{href}";
                else if (href.StartsWith("/"))
                    href = init.host + href;
                else if (!href.StartsWith("http"))
                    href = $"{init.host}/{href}";
                #endregion

                #region img
                if (img != null)
                {
                    img = img.Replace("&amp;", "&").Replace("\\", "");

                    if (img.StartsWith("../"))
                        img = $"{init.host}/{img.Replace("../", "")}";
                    else if (img.StartsWith("//"))
                        img = $"https:{img}";
                    else if (img.StartsWith("/"))
                        img = init.host + img;
                    else if (!img.StartsWith("http"))
                        img = $"{init.host}/{img}";
                }
                #endregion

                if (!init.ignore_no_picture && string.IsNullOrEmpty(img))
                    continue;

                #region preview
                if (preview != null)
                {
                    if (preview.Contains("&amp;"))
                        preview = preview.Replace("&amp;", "&");

                    if (preview.Contains("\\"))
                        preview = preview.Replace("\\", "");

                    if (preview.StartsWith("../"))
                        preview = $"{init.host}/{preview.Replace("../", "")}";
                    else if (preview.StartsWith("//"))
                        preview = $"https:{preview}";
                    else if (preview.StartsWith("/"))
                        preview = init.host + preview;
                    else if (!preview.StartsWith("http"))
                        preview = $"{init.host}/{preview}";

                    if (init.streamproxy_preview)
                    {
                        preview = ProxyLink.Encrypt(
                            preview,
                            string.Empty,
                            verifyip: false,
                            ex: DateTime.Today.AddDays(2),
                            prefix: [host, "/proxy/"]
                        );
                    }
                }
                #endregion

                string clearText(string text)
                {
                    if (string.IsNullOrEmpty(text))
                        return text;

                    text = text.Replace("&nbsp;", "");
                    return Regex.Replace(text, "<[^>]+>", "");
                }

                var pl = new PlaylistItem()
                {
                    name = clearText(name),
                    video = $"nexthub/vidosik?uri={AesTo.Encrypt($"{plugin}_-:-_{href}")}" + args,
                    preview = preview,
                    picture = img,
                    time = clearText(duration),
                    quality = clearText(quality),
                    myarg = args,
                    json = parse.json,
                    related = init.view != null ? init.view.related : false,
                    model = model,
                    bookmark = new Bookmark()
                    {
                        site = "nexthub",
                        href = $"{plugin}_-:-_{href}",
                        image = img
                    }
                };

                #region eval
                if (eval != null)
                {
                    if (msm != null && msm.Length > 0)
                        html = OwnerTo.String(msm, Encoding.UTF8);

                    pl = CSharpEval.Execute<PlaylistItem>(eval, new NxtChangePlaylis(init, plugin, host, html, nodes, pl, row), Root.playlistOptions);
                }
                #endregion

                if (pl.json == false && (init.streamproxy || (init.geostreamproxy != null && requestInfo.Country != null && init.geostreamproxy.Contains(requestInfo.Country))))
                {
                    pl.video = ProxyLink.Encrypt(
                        pl.video,
                        requestInfo.IP,
                        init.headersList,
                        prefix: [host, "/proxy/"]
                    );
                }

                if (pl != null)
                    playlists.Add(pl);
            }
        }

        return playlists;
    }
    #endregion

}
