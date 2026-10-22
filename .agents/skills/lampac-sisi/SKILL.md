---
name: lampac-sisi
description: >-
  Works on Lampac SISI, adult site modules, and NextHUB YAML catalogs. Use
  for /sisi.js, SISI/, Modules/Adult, BaseSisiController, or NextHUB sites.
---

# Lampac SISI

Read [lampac-repo](../lampac-repo/SKILL.md) and [lampac-modules](../lampac-modules/SKILL.md). Pair with [`.cursor/agents/lampac-sisi.md`](../../../.cursor/agents/lampac-sisi.md). Role card: [`.agents/agents/lampac-sisi.md`](../../agents/lampac-sisi.md).

## Three places

| Task | Where |
| ------ | -------- |
| Plugin, bookmarks, SQLite | `SISI/` (`SisiApi.cs`, `ModInit.cs`) |
| One site scraper | `Modules/Adult/{Site}/` — copy `Xvideos` (`Controller.cs`, `Service.cs`, `ModInit.cs`) |
| YAML catalog site | `Modules/NextHUB/sites/*.yaml` — copy an existing yaml, do not add a C# module |

Adult controllers use `BaseSisiController` in `Shared/Controllers/BaseSisiController.cs`. Player chrome lives in `Core/wwwroot/sisi` and is not part of a scraper change.

## Workflow

1. Decide which of the three rows the task is. Stay in that row.
2. Copy the sibling. Encode search text that goes into a URL.
3. Build the csproj you touched. A YAML-only edit does not need a build.

## Current shape

`SISI/SisiApi.cs` is the `/sisi.js` plugin. Adult sites are separate projects; `Xvideos` is the usual Controller + Service + ModInit split. NextHUB sites are YAML under `Modules/NextHUB/sites/`, not new C# projects. Controllers use `BaseSisiController`.

## Review pass

One adult module or one NextHUB yaml per pass. Same checks as Online: encoded search, shared HTTP, failure returns an empty result. Do not review the whole `Modules/Adult` tree at once.

## Do not

- Invent a new catalog format next to NextHUB YAML.
- Change SISI core when the bug is one adult module.
