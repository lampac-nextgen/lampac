# Online families

Read this before touching a provider. One task is one family and one module. Do not copy HDVB into an ENG or HTML module.

## How a button appears

1. `Online/OnlineApi.cs` builds `/online.js` and calls every loaded module.
2. `OnlineModuleEntry.EnsureCache` reflects `IModuleOnline` / `IModuleOnlineAsync` / spider interfaces from enabled assemblies. Public parameterless ctor required.
3. `ModInit.Invoke` returns `ModuleOnlineItem` (conf + optional plugin slug). `null` or empty means this title does not show the source.
4. The button URL is `{host}/lite/{plugin}` plus ids from `OnlineEventsModel` (`kinopoisk_id`, `imdb_id`, `title`, `original_title`, `year`, `serial`, `isanime`).
5. The controller action is a separate route, usually `lite/{plugin}`. `[Staticache(manually: true)]` is the usual cache attribute.

`IModuleLoaded` alone loads conf and does **not** add a button. Ashdi, Tortuga, and HdvbUA are loaded that way: `/lite/...` still works if something links it, but `Invoke` never runs.

WAF for `/lite/` is inserted by `Online/ModInit.cs` (10 requests / second).

## Family A — Kinopoisk CDN

Clone: `Modules/OnlineRUS/HDVB`.

- `BaseOnlineController`, conf `OnlinesSettings` via `ModuleInvoke.Init`.
- Index: `kinopoisk_id`, `title`, `original_title`, season `s`, translation `t`.
- `kinopoisk_id == 0` goes to `RouteSpiderSearch` when the module implements `IModuleOnlineSpider`.
- Titles on follow-up URLs use `HttpUtility.UrlEncode`.
- Stream links go through `HostStreamProxy` when `streamproxy` is set.
- Same shape: Vibix, Collaps, Videoseed, Alloha (Alloha uses `ModuleConf`).

## Family B — paid account

Clone: `Modules/OnlinePaid/Rezka` or `Filmix`.

- Rezka: `BaseOnlineController<RezkaSettings>`, `loadKitInitialization` copies premium/uacdn/reserve/ajax from kit JSON, `requestInitializationAsync` picks headers by country.
- Routes: `lite/rezka`, `lite/rezka/serial`, `lite/rezka/movie` and `.m3u8`.
- Filmix `Invoke` can return three items (`Filmix`, `filmixtv`, `fxapi`). Token may be empty; free catalog hides by Kyiv hour window (`hidefreeStart` / `hidefreeEnd`).
- VoKino is `IModuleOnlineAsync`: `InvokeAsync` checks `requestInfo.user` and memory cache before adding the button.
- Do not log the token. Do not share one user's cookie jar across requests.

## Family C — English embed

Clone: `Modules/OnlineENG/PlayEmbed`. Every ENG controller extends `BaseENGController`.

- Index calls `ViewTmdb(...)`. Seasons come from the Cub TMDB mirror (`cub.scheme://tmdb.{cub.mirror}/3/tv/{id}`), cached 4 hours under `tmdb:seasons:{id}`.
- `checksearch` returns `data-json=` and does not hit the embed.
- `tmdb_id > 0` replaces `id`. `IsRequestBlocked(rch: false)`.
- PlayEmbed video route builds `{host}/movie/{id}` or `/tv/{id}/{s}/{e}` and uses Playwright. If `PlaywrightBrowser.Status` is disabled, return `OnError()`.
- Do not reimplement season lists. Change `ViewTmdb` only when every ENG source needs the same fix.

## Family D — anime gate

Clone: `Modules/OnlineAnime/Kodik`.

- `Invoke` returns the item only when `args.isanime` or `original_language` starts with ja/ko/zh/cn/th/vi/tl. Otherwise `null`.
- Spider also returns null when `!args.isanime`.
- Kodik signs requests with HMAC-SHA256 (`HMAC` in the controller) and whitelists query key `pick` on `CoreInit.BaseModValidQueryValueWhiteList`.
- AniLibria and AnimeLib follow the same interfaces. Do not show Kodik on a non-anime title.

## Family E — HTML / browser

Clone: `Modules/OnlineRUS/Kinobase`.

- No kinopoisk id. Index takes `title`, `year`, `href`, `source`, `id`.
- First lines bail out when Playwright is disabled.
- Search URL is detected inside `KinobaseInvoke` (`/search?query=`). Encode the query there.
- `rch: false` on `IsRequestBlocked`.

## Shared controller tools

`BaseOnlineController` (`Shared/Controllers/BaseOnlineController.cs`):

- `IsRequestBlocked` / `OnError` / `badInitMsg` — blocked or dead upstream
- `InvokeCache` / `InvokeCacheResult` — hybrid cache, fail with `e.Fail`
- `ContentTpl` + `MovieTpl` / `SeasonTpl` / `EpisodeTpl` / `VoiceTpl` — Lampa JSON/HTML
- `HostStreamProxy` — wrap a file URL
- `InvkSemaphore` — one flight per key

`OnlinesSettings` fields that change behavior: `displayindex`, `streamproxy`, `rch_access`, `stream_access`, `geo_hide`, `rip`, `overridehost`.

## What to do

1. Pick the family from the route and the base class. Read that clone's `ModInit.cs` and the Index action only.
2. Match its id type (kp, tmdb, title search, token). Do not add `kinopoisk_id` to an ENG or Kinobase action.
3. Button visibility is `Invoke`, not the controller. If the source is missing in `/online.js`, fix `ModInit`, not the route.
4. Build only that provider csproj.
