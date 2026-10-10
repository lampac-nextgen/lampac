---
name: lampac-media
description: >-
  Works on Lampac playback modules outside Core middleware: Modules/Proxy,
  transcoding, GStreamer, TorrServer, DLNA, and tracks. Use for Cub, TMDB,
  Corseu, CorsMedia, CacheMedia, ProxyLimiter, Transcoding, or GStreamer.
  Core /proxy and ProxyImg are lampac-core. ProxyLink is lampac-shared.
---

# Lampac media

Read [lampac-repo](../lampac-repo/SKILL.md) first. Pair with [`.cursor/agents/lampac-media.md`](../../../.cursor/agents/lampac-media.md). Role card: [`.agents/agents/lampac-media.md`](../../agents/lampac-media.md). File map: [reference.md](reference.md).

## Workflow

1. Name the module (`Modules/Proxy/*`, transcoder, TorrServer, DLNA, Tracks). Edit only that path. `Core/Middlewares/ProxyAPI*.cs` and `ProxyImg` belong to `lampac-core`.
2. Keep `HttpContext.RequestAborted` on stream copies. Range hits return 206 with `Content-Range`.
3. Outbound HTTP uses the named clients from `Core/Startup.cs` or `FriendlyHttp.MessageClient`.
4. Build the module csproj you edited.

## Current shape

`/proxy/` decrypts a `ProxyLink` payload, checks the URL, then streams with `RequestAborted`. Playlist rewrites are `ProxyAPI.M3u8.cs` and `ProxyAPI.Dash.cs`. File-cache Range is 206. Cub, TMDB, Corseu, and CorsMedia are separate controllers under `Modules/Proxy/` and are not the same code path.

## Review pass

Read the module under review (one `Modules/Proxy` controller, transcoder, or TorrServer). Check Range/206, client disconnect via `RequestAborted`, and which request headers are copied upstream. A bug in `ProxyAPI` partials is `lampac-core`, not this skill. Fix the one path. Header allow/deny lists stay in that file.

## Do not

- Start a second proxy implementation beside `ProxyAPI` and `ProxyLink`.
- Change GStreamer while fixing CubProxy, or the reverse.
