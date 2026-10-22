---
name: lampac-repo
description: >-
  Lampac NextGen repository map: solution layout, config layers, publish
  paths, and how to build one project. Use when working anywhere in
  lampac-nextgen, NextGen.slnx, CoreInit, base.conf, init.conf, or port 9118.
---

# Lampac repository

Shared facts for every Lampac agent. Area skills link here. Also read [ponytail](../ponytail/SKILL.md). Role cards are [`.agents/agents/`](../../agents/). Theses are [`.agents/references/theses.md`](../../references/theses.md). Do not edit `.cursor/plans/*.plan.md` unless asked. Do not commit unless asked.

## Layout

| Path | Role |
| ------ | ------ |
| `Core/` | ASP.NET host, middleware, `Startup.cs` |
| `Shared/` | Library every module references |
| `Online/` | VOD core, `/online.js`, `/lite/*` |
| `SISI/` | 18+ core, `/sisi.js` |
| `Modules/` | One csproj per feature or provider |
| `config/base.conf` | Shipped overrides |
| `config/example.init.*` | Starter files, not the running config |
| `NextGen.slnx` | Solution. New projects get a `<Project Path="..." />` next to their siblings |

Target framework is `net10.0`. Package versions live only in `Directory.Packages.props`.

## Config layers

Read the layer you are changing. Do not invent a default.

1. `Shared/CoreInit.cs` — value when the key is absent
2. `config/base.conf` — override shipped with the tree
3. Operator `init.conf` / `init.yaml` at runtime (CWD)
4. `manifest.json` `enable` — whether that module is compiled and loaded

`example.init.conf` is a template. Publishing copies module sources into `module/`; `TestModules` goes to `mods/`.

## Build

Build the csproj you edited, not the whole solution:

```bash
dotnet build path/to/Project.csproj --nologo
```

Ports: production `9118`, dev compose `29118`.

## Change rules

- Grep callers before changing a public signature in `Shared` or `Core`.
- One provider or feature is one existing project. Do not add a framework for a single caller.
- Never put live tokens, passwords, or private hosts into examples or docs.

## Review pass

The tree is already in this workspace. Do not ask the user to paste `tree`, csproj, or source files. Read 2–8 related files, then either report or patch.

When the user asks to analyze or improve a subsystem:

1. Stay inside the agent’s folder. Read the skill’s file map, then the implementation.
2. Check only these four points: async without `.Result` / `.Wait()` / `GetResult` on request paths; `HttpClient` reuse; URL and search-string validation; behavior when the upstream source fails.
3. Report each issue as severity (High / Medium / Low), `path:line`, and one sentence. Do not write a full replacement file full of comments.
4. Fix High findings with the smallest diff. Leave Low unless the user asked for a cleanup.
5. Name the next files in the repo to open. Do not request them from the user.

## Who owns what

| Area | Agent | Skill |
| ------ | -------- | -------- |
| Host, middleware, `Core/Middlewares/ProxyAPI*.cs` | `lampac-core` | `lampac-core` |
| Shared library | `lampac-shared` | `lampac-shared` |
| New module skeleton only | `lampac-modules` | `lampac-modules` |
| VOD providers, including `Modules/OnlinePacks` | `lampac-online` | `lampac-online` |
| SISI, Adult, NextHUB | `lampac-sisi` | `lampac-sisi` |
| `Modules/Proxy`, transcode, TorrServer, DLNA, Tracks | `lampac-media` | `lampac-media` |
| JacRed | `lampac-jacred` | `lampac-jacred` |
| Music | `lampac-music` | `lampac-music` |
| Bookmarks, timecode, watch together, SeriesNotify | `lampac-sync` | `lampac-sync` |
| Telegram, OIDC, QR, Tg-notify | `lampac-community` | `lampac-community` |
| Admin, editor, logs | `lampac-admin` | `lampac-admin` |
| Lampa, ForkPlayer, MSX, APK, Catalog, Kit, ExternalBind, PidTor, Potok | `lampac-clients` | `lampac-clients` |
| Install, Docker, config, `.github/workflows/release.yml`, `.github/workflows/pages.yml` | `lampac-deploy` | `lampac-deploy` |
| Mintlify docs | `mintlify-docsops` | `.agents/skills/mintlify` |

Security review stays with the agent in this table. There is no separate security agent. Full routing is [AGENTS.md](../../../AGENTS.md). Each row's triggers and deliverables are `.agents/agents/<agent>.md`.
