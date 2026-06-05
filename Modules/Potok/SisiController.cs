using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Attributes;
using Shared.Services;

namespace Potok;

public class SisiController : BaseController
{
    [HttpGet, AllowAnonymous]
    [Staticache(5, always: true, setHeadersNoCache: true)]
    [Route("the-blue-oyster/manifest.json")]
    public ActionResult Manifest()
    {
        string manifest = @"{
  ""id"": ""the-blue-oyster"",
  ""name"": ""The Blue Oyster"",
  ""version"": ""1.0.2"",
  ""description"": """",
  ""author"": ""lampac"",
  ""entrypoint"": ""index.js"",
  ""permissions"": [
    ""storage"",
    ""ui-notifications""
  ],
  ""slots"": [
    {
      ""id"": ""the-blue-oyster"",
      ""slotName"": ""extension-page"",
      ""title"": ""The Blue Oyster""
    },
    {
      ""id"": ""the-blue-oyster-sidebar"",
      ""slotName"": ""sidebar-menu"",
      ""title"": ""The Blue Oyster""
    }
  ]
}";

        return ContentTo(manifest);
    }


    [HttpGet, AllowAnonymous]
    [Staticache(5, always: true, setHeadersNoCache: true)]
    [Route("the-blue-oyster/index.js")]
    public ActionResult Index()
    {
        string plugin = FileCache.ReadAllText($"{ModInit.modpath}/the-blue-oyster.js", "the-blue-oyster.js", saveCache: false)
            .Replace("{localhost}", host);

        return ContentTo(plugin, "application/javascript; charset=utf-8");
    }
}
