# Media map

| Path | Role |
| ------ | ------ |
| `Core/Middlewares/ProxyAPI.cs` (+ `.M3u8`, `.Dash`, `.Utilities`) | `/proxy/`, `/proxy-dash/` |
| `Core/Middlewares/ProxyImg.cs` | `/proxyimg` |
| `Shared/Services/ProxyLink.cs` | Encrypt and decrypt proxy URLs |
| `Modules/Proxy/CubProxy` | Cub API reverse proxy |
| `Modules/Proxy/TmdbProxy` | TMDB proxy |
| `Modules/Proxy/CorsMedia` | Tokenized media URL to HostStreamProxy |
| `Modules/Proxy/Corseu` | Token-gated HTTP fetch |
| `Modules/Proxy/CacheMedia` | HLS disk-cache keys |
| `Modules/Proxy/ProxyLimiter` | Proxy rate limit hook |
| `Modules/Transcoding/` | FFmpeg |
| `Modules/GStreamer/` | `/gst/*` |
| `Modules/TorrServer/` | Local TorrServer on `listen.localhost` |
| `Modules/DLNA/` | DLNA / UPnP |
| `Modules/Tracks/` | Subtitles and audio tracks |

`lampac-media` edits rows under `Modules/`. `Core/Middlewares/*` is `lampac-core`. `ProxyLink` is `lampac-shared`.
