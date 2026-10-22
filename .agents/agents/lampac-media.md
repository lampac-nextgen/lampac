# lampac-media

Playback modules outside Core middleware.

## Triggers

Cub, TMDB, Corseu, CorsMedia, CacheMedia, ProxyLimiter, transcoding, GStreamer, TorrServer, DLNA, and Tracks.

## Inputs

- [`.agents/skills/lampac-media/SKILL.md`](../skills/lampac-media/SKILL.md)
- [`.agents/skills/lampac-media/reference.md`](../skills/lampac-media/reference.md)
- [`.agents/skills/lampac-repo/SKILL.md`](../skills/lampac-repo/SKILL.md)

## Allowed tools

- Read and edit under the playback module named in the task
- `dotnet build` of that csproj
- No Mintlify MCP

## Working directories

- `Modules/Proxy/` — CubProxy, TmdbProxy, CorsMedia, Corseu, CacheMedia, ProxyLimiter
- `Modules/Transcoding/`, `Modules/GStreamer/`
- `Modules/TorrServer/`
- `Modules/DLNA/`, `Modules/Tracks/`

Real Cub and TMDB projects are `Modules/Proxy/CubProxy` and `Modules/Proxy/TmdbProxy`, not the empty top-level folders.

## Deliverables

- Diff limited to the path the user named
- Stream copies keep honoring `HttpContext.RequestAborted`. Range responses stay 206 with `Content-Range`
- Outbound calls go through named HTTP clients in `Core/Startup.cs` or `FriendlyHttp.MessageClient`
- Build of the project edited

## Does not own

- `Core/Middlewares/ProxyAPI*.cs` and `ProxyImg.cs` (`lampac-core`)
- `Shared/Services/ProxyLink.cs` (`lampac-shared`)
- A second proxy stack, or a GStreamer change for a Cub bug
