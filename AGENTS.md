# Lampac NextGen agents

Product behavior lives in code, `config/base.conf`, and each module `manifest.json`. This file only routes agents. Docs-only work also follows [docs/AGENTS.md](docs/AGENTS.md).

Portable source of truth for roles and contracts:

| Topic | File |
| --- | --- |
| Who takes a task | This file |
| Role card (triggers, tools, deliverables, non-overlap) | [`.agents/agents/<name>.md`](.agents/agents/) |
| Architecture theses | [`.agents/references/theses.md`](.agents/references/theses.md) |
| Product architecture pointer | [ARCHITECTURE.md](ARCHITECTURE.md) |
| How a Lampac area is edited | `.agents/skills/<name>/SKILL.md` |
| How little to build | [`.agents/skills/ponytail/SKILL.md`](.agents/skills/ponytail/SKILL.md) |
| Mintlify components | `.agents/skills/mintlify/SKILL.md` |
| Mintlify site voice and nav | `docs/AGENTS.md` and `.cursor/rules/mintlify-docs.mdc` |
| Docs MCP | `.cursor/mcp.json` |
| Agent metadata check | `node .agents/scripts/validate-agents.mjs` |

Cursor is the editor entrypoint: thin files in `.cursor/agents/*.md` point at the role cards. `.claude/`, `.github/agents`, `.github/prompts`, `.github/instructions`, and `.github/skills` are intentionally absent. There is no `wrangler.toml` and no Cloudflare Workers project. [CLAUDE.md](CLAUDE.md) points here and does not copy this table.

Project skills live in `.agents/skills/`. Cursor, Copilot, Codex, and Gemini read that path. There is no second copy under `.cursor/skills/`.

`.agents/skills/mintlify-docs/SKILL.md` is byte-identical to `.agents/skills/mintlify/SKILL.md` and both set `name: mintlify`. Read `mintlify` only. Leave the duplicate folder in place because [skills-lock.json](skills-lock.json) records that hash.

User-level skills stay outside this repository. Cloudflare skills and personal deploy skills are not project skills: this tree has no Workers project, and those notes name private hosts. `ponytail` is a project skill. Read it on every code change. The runnable check in this repository is the build in Quality Gates. Do not add a test csproj.

The repository is on disk. Agents read files here. They do not ask for a tree, a csproj, or pasted sources. Do not commit unless asked. Do not edit `.cursor/plans/*.plan.md` unless asked. Do not put tokens, passwords, or private hosts into examples.

Role boundaries (detail on each role card):

- `lampac-modules` creates the project skeleton. The area agent then edits behavior. They do not edit the same controller in one task.
- `lampac-core` owns `Core/`, including `Core/Middlewares/ProxyAPI*.cs`. `lampac-media` owns `Modules/Proxy`, transcoding, TorrServer, DLNA, and Tracks.
- `Modules/OnlinePacks` belongs to `lampac-online`, not `lampac-clients`.
- A security pass stays with the agent that owns the directory and uses the review pass in `lampac-repo`. There is no security agent.

## Interaction matrix

| Agent | Role | Skills | Code zone | Instructions |
| --- | --- | --- | --- | --- |
| `lampac-core` | Host, middleware, named HTTP clients, module load | `lampac-core`, `lampac-repo` | `Core/` | [role card](.agents/agents/lampac-core.md) |
| `lampac-shared` | HTTP helpers, hybrid cache, base controllers, `CoreInit` | `lampac-shared`, `lampac-repo` | `Shared/` | [role card](.agents/agents/lampac-shared.md) |
| `lampac-modules` | New module skeleton: manifest, ModInit, csproj, solution entry | `lampac-modules`, `lampac-repo` | `Modules/<New>/`, `NextGen.slnx` | [role card](.agents/agents/lampac-modules.md) |
| `lampac-online` | VOD plugin and one provider family | `lampac-online`, `lampac-repo`, `reference.md`; `lampac-modules` if the csproj is missing | `Online/`, `Modules/OnlineRUS`, `OnlinePaid`, `OnlineENG`, `OnlineUKR`, `OnlineGEO`, `OnlineAnime`, `OnlinePacks` | [role card](.agents/agents/lampac-online.md) |
| `lampac-sisi` | 18+ plugin, one adult site, or one NextHUB yaml | `lampac-sisi`, `lampac-repo`, `lampac-modules` | `SISI/`, `Modules/Adult`, `Modules/NextHUB` | [role card](.agents/agents/lampac-sisi.md) |
| `lampac-media` | Playback modules outside Core middleware | `lampac-media`, `lampac-repo`, `reference.md` | `Modules/Proxy`, `Transcoding`, `GStreamer`, `TorrServer`, `DLNA`, `Tracks` | [role card](.agents/agents/lampac-media.md) |
| `lampac-jacred` | Torrent indexer API and one tracker | `lampac-jacred`, `lampac-repo`, `lampac-modules` | `Modules/JacRed` | [role card](.agents/agents/lampac-jacred.md) |
| `lampac-music` | Music search, playlists, one audio provider, stream relay | `lampac-music`, `lampac-repo` | `Modules/Music` | [role card](.agents/agents/lampac-music.md) |
| `lampac-sync` | Bookmarks, timecode, storage, joint watch, series notify | `lampac-sync`, `lampac-repo` | `Modules/Sync`, `WatchTogether`, `SeriesNotify` | [role card](.agents/agents/lampac-sync.md) |
| `lampac-community` | Telegram, OIDC, QR, notify bot | `lampac-community`, `lampac-repo` | `Modules/Community`, `Modules/Tg-notify.bot` | [role card](.agents/agents/lampac-community.md) |
| `lampac-admin` | Admin UI, database editor, weblog, telemetry | `lampac-admin`, `lampac-repo` | `Modules/AdminPanel`, `DatabaseEditor`, `WebLog`, `Telemetry`, `LogUserRequest-Lite` | [role card](.agents/agents/lampac-admin.md) |
| `lampac-clients` | Device and UI adapters | `lampac-clients`, `lampac-repo` | `Modules/LampaWeb`, `ForkPlayerXML`, `MsxNative`, `LampacApk`, `Catalog`, `Kit`, `ExternalBind`, `PidTor`, `Potok` | [role card](.agents/agents/lampac-clients.md) |
| `lampac-deploy` | Install, image, Helm, Ansible, shipped config, release workflows | `lampac-deploy`, `lampac-repo` | `install.sh`, `Dockerfile`, compose files, `charts/`, `ansible/`, `build.sh`, `Makefile`, `config/`, `.github/workflows/release.yml`, `.github/workflows/pages.yml` | [role card](.agents/agents/lampac-deploy.md) |
| `mintlify-docsops` | Operator documentation | `mintlify`; `mintlify-api` for API pages | `docs/` | [role card](.agents/agents/mintlify-docsops.md), [docs/AGENTS.md](docs/AGENTS.md) |

`lampac-repo` is a skill every Lampac agent reads. It is not an agent. Input for every Lampac agent is the skill with the same name, `lampac-repo`, and `ponytail`, then the role card. Output is a diff limited to that row, then the build command in Quality Gates.

Stack notes: ASP.NET Core on .NET 10; VOD uses `BaseOnlineController` / `BaseENGController`; SISI uses `BaseSisiController` and YAML; media uses HTTP proxy, ffmpeg, and GStreamer; JacRed uses BencodeNET and MonoTorrent; admin auth is `[Authorization]` in `Core/Middlewares/Accsdb.cs`. Docker port 9118, dev compose 29118.

## Task Routing Matrix

| Scenario | Starts | Then | Skills |
| --- | --- | --- | --- |
| New module or plugin project | `lampac-modules` | Area agent for behavior (`lampac-online`, `lampac-sisi`, `lampac-media`, `lampac-jacred`, `lampac-music`, `lampac-sync`, `lampac-community`, `lampac-admin`, or `lampac-clients`) | `lampac-modules`, `lampac-repo`, then that area skill |
| New VOD provider | `lampac-online` | none | `lampac-online`, `.agents/skills/lampac-online/reference.md`, `lampac-modules` only if the csproj is missing |
| Refactor an API | Agent whose directory contains the controller | `lampac-shared` only if the public helper signature changes | That area skill and `lampac-repo` |
| Security review of a diff | Agent that owns the touched directory | none | `lampac-repo` review pass. No exploit write-up |
| Bug in one file | Agent that owns that file's directory | none | That skill |
| Missing `/online.js` button | `lampac-online` | none | Fix `ModInit.Invoke`, not the lite route |
| `/proxy` middleware | `lampac-core` | none | `lampac-core` |
| Cub, TMDB, Corseu, CorsMedia, transcoding, TorrServer | `lampac-media` | none | `lampac-media`, `.agents/skills/lampac-media/reference.md` |
| Docker, install, `base.conf`, release tag, GitHub Pages | `lampac-deploy` | `mintlify-docsops` only if an operator page becomes wrong | `lampac-deploy` |
| MDX, `docs.json`, docs nav | `mintlify-docsops` | none | `docs/AGENTS.md`, `.agents/skills/mintlify/SKILL.md`. Add `.agents/skills/mintlify-api/SKILL.md` for API reference pages |

One task, one directory. Do not start a second agent on the same controller.

## Skills and Capabilities Map

No skill ships a `.py`, `.sh`, or `.cjs` helper. The metadata validator is `.agents/scripts/validate-agents.mjs`, not a skill. Reference files exist only where listed.

| Skill | Path | Input | Output | Depends on |
| --- | --- | --- | --- | --- |
| `ponytail` | `.agents/skills/ponytail/SKILL.md` | Any code change | Shortest diff that works | none |
| `lampac-repo` | `.agents/skills/lampac-repo/SKILL.md` | Any Lampac task | Layout, config-layer order, review pass | none |
| `lampac-core` | `.agents/skills/lampac-core/SKILL.md` | Host or middleware change | Edit under `Core/` | `lampac-repo` |
| `lampac-shared` | `.agents/skills/lampac-shared/SKILL.md` | Shared helper change | Edit under `Shared/` plus caller grep | `lampac-repo` |
| `lampac-modules` | `.agents/skills/lampac-modules/SKILL.md` | New module name and sibling to copy | manifest, ModInit, csproj, `NextGen.slnx` entry | `lampac-repo` |
| `lampac-online` | `.agents/skills/lampac-online/SKILL.md` | Provider name and family | One provider diff | `lampac-repo`, `reference.md` in the same folder |
| `lampac-sisi` | `.agents/skills/lampac-sisi/SKILL.md` | Adult module or NextHUB yaml | One site diff | `lampac-repo`, `lampac-modules` |
| `lampac-media` | `.agents/skills/lampac-media/SKILL.md` | Playback path name | One module diff outside Core middleware | `lampac-repo`, `reference.md` in the same folder |
| `lampac-jacred` | `.agents/skills/lampac-jacred/SKILL.md` | Tracker or indexer route | Diff in `Modules/JacRed` | `lampac-repo`, `lampac-modules` |
| `lampac-music` | `.agents/skills/lampac-music/SKILL.md` | `/music` route or provider folder | Diff in `Modules/Music` | `lampac-repo` |
| `lampac-sync` | `.agents/skills/lampac-sync/SKILL.md` | Bookmark, timecode, storage, watch, or notify | One of those modules | `lampac-repo` |
| `lampac-community` | `.agents/skills/lampac-community/SKILL.md` | Telegram, OIDC, QR, or notify bot | One auth module | `lampac-repo` |
| `lampac-admin` | `.agents/skills/lampac-admin/SKILL.md` | Admin, editor, weblog, or telemetry route | Diff that keeps `[Authorization]` | `lampac-repo` |
| `lampac-clients` | `.agents/skills/lampac-clients/SKILL.md` | Lampa, ForkPlayer, MSX, APK, Catalog, Kit, ExternalBind, PidTor, or Potok | One adapter | `lampac-repo` |
| `lampac-deploy` | `.agents/skills/lampac-deploy/SKILL.md` | Install, image, chart, ansible, config, release, or Pages workflow | One file from that skill's table | `lampac-repo` |
| `mintlify` | `.agents/skills/mintlify/SKILL.md` | Docs page or `docs.json` change | MDX that matches Mintlify | `docs/AGENTS.md` |
| `mintlify-api` | `.agents/skills/mintlify-api/SKILL.md` | API reference page | API MDX | `mintlify` |
| `mintlify-docs` | `.agents/skills/mintlify-docs/SKILL.md` | none; do not read | none | Duplicate of `mintlify`. Kept for `skills-lock.json` |

Online family notes live in `.agents/skills/lampac-online/reference.md` (CDN, paid, ENG, anime, HTML). Media paths live in `.agents/skills/lampac-media/reference.md`.

`.cursor/rules/mintlify-docs.mdc` applies only to `docs/**/*.mdx`, `docs/docs.json`, `docs/AGENTS.md`, and `.devin/wiki.json`. `.cursor/mcp.json` registers `https://mintlify.com/docs/mcp` and `https://mcp.mintlify.com` for that docs work. There is no `.cursor/commands` directory.

## Quality and Verification Gates

Restated as acceptance criteria in [`.agents/references/theses.md`](.agents/references/theses.md). Before the change is done:

1. Stay inside the working directories of the agent row.
2. If a public signature in `Shared` or `Core` changes, grep callers under `Core/`, `Online/`, `SISI/`, and `Modules/`.
3. Build only the csproj that changed: `dotnet build path/to/Project.csproj --nologo`. There is no test csproj in this repository. Do not run `dotnet test`.
4. A change under `site/` is verified with `npm run build` in `site/`. That is the script in `site/package.json`. `npm run dev` and `npm run preview` are local only.
5. A change under `docs/` is verified from `docs/` with the external Mintlify CLI, when it is installed: `mint validate`, `mint broken-links --check-anchors`, and `mint a11y`. Those commands are not npm scripts.
6. A config edit names which layer wins: `Shared/CoreInit.cs` when the key is absent, `config/base.conf` when the tree overrides it, `config/example.init.conf` or `config/example.init.yaml` as a template only.
7. Diff must not add tokens, passwords, cookies, or private hosts.
8. A security pass reports High / Medium / Low with `path:line` and patches High findings with a small diff. It does not replace a file with a commented rewrite and does not include an exploit.

`.github/workflows/pages.yml` runs `npm ci` and `npm run build` in `site/` and publishes `site/dist`. `.github/workflows/release.yml` creates a tag from a manual `tag` input. Neither workflow is a local test gate.
