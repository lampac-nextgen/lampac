# Lampac NextGen architecture

Short map for agents and reviewers. The long reconnaissance index is [CONTEXT_INDEX.md](CONTEXT_INDEX.md). Operator pages are [docs/architecture/overview.mdx](docs/architecture/overview.mdx), [docs/architecture/modules.mdx](docs/architecture/modules.mdx), and [docs/architecture/middleware.mdx](docs/architecture/middleware.mdx). Agent contracts are [.agents/references/theses.md](.agents/references/theses.md). Who edits what is [AGENTS.md](AGENTS.md).

## Runtime

One ASP.NET Core process (`net10.0`) listens on port **9118** (dev compose **29118**). The host project is `Core/`. Features are separate projects that reference `Shared/` and load at startup from `module/` and `mods/`. Package versions live only in `Directory.Packages.props`. The solution file is `NextGen.slnx`.

There is no Cloudflare Workers project and no root Node app. The Astro site in `site/` is marketing, not the Lampa player. Operator docs are Mintlify MDX in `docs/`.

## Config layers

1. `Shared/CoreInit.cs` — value when the key is absent
2. `config/base.conf` — shipped override
3. Operator `init.conf` / `init.yaml` in the process working directory
4. Module `manifest.json` `enable` — compile and load

`config/example.init.*` are templates only.

## Request path that matters

Client playback does not call upstream CDNs directly on the common path. A provider returns an encrypted `ProxyLink`. The player fetches `/proxy/{token}` or `/proxy-dash/`.

| Path | Owner |
| --- | --- |
| `Core/Middlewares/ProxyAPI*.cs`, `ProxyImg` | `lampac-core` |
| `Modules/Proxy` (Cub, TMDB, Corseu, CorsMedia, CacheMedia, ProxyLimiter), Transcoding, GStreamer, TorrServer, DLNA, Tracks | `lampac-media` |
| `Shared/Services/ProxyLink.cs` and other shared helpers | `lampac-shared` |

`/proxy` is branched in `Core/Startup.cs` before Accsdb. Do not treat `Modules/Proxy/*` as that middleware.

Empty directories `Modules/CubProxy`, `Modules/TmdbProxy`, and `Modules/TimeCode` are not the projects. Edit `Modules/Proxy/CubProxy`, `Modules/Proxy/TmdbProxy`, and `Modules/Sync/TimeCode`.

## Module load

`Core/Startup` loads prebuilt `*.dll` files and compiles source folders that have `manifest.json` when `LoadModules` matches and the folder is not in `SkipModules`. Publishing copies module sources into `module/`. `TestModules` goes to `mods/`.

## Agent hosts

Role cards, theses, and the metadata validator live under `.agents/`. Cursor entrypoints are thin files in `.cursor/agents/` and skills in `.agents/skills/`. `.claude/`, `.github/agents`, `.github/instructions`, and `.github/skills` are intentionally absent. `CLAUDE.md` points at `AGENTS.md` and does not repeat the role table.
