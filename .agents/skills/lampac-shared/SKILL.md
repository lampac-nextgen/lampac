---
name: lampac-shared
description: >-
  Works on the Lampac Shared library: HTTP helpers, hybrid cache, ProxyLink,
  BaseController, CoreInit, pools, and Playwright. Use when editing Shared/,
  BaseOnlineController, BaseSisiController, Http.cs, or FriendlyHttp.
---

# Lampac Shared

Read [lampac-repo](../lampac-repo/SKILL.md) first. Pair with [`.cursor/agents/lampac-shared.md`](../../../.cursor/agents/lampac-shared.md). Role card: [`.agents/agents/lampac-shared.md`](../../agents/lampac-shared.md).

## Where to extend

| Need | Existing type |
| ------ | ---------------- |
| Outbound HTTP | `Shared/Services/HTTP/Http.cs`, `FriendlyHttp.cs` |
| Controller base | `Shared/Controllers/BaseController.cs` |
| VOD controller | `Shared/Controllers/BaseOnlineController.cs` |
| 18+ controller | `Shared/Controllers/BaseSisiController.cs` |
| Cache | `Shared/Services/Hybrid/HybridFileCache.cs` (`IMemoryCache` + disk). No Redis |
| Proxy URLs | `Shared/Services/ProxyLink.cs` |
| Config defaults | `Shared/CoreInit.cs` |
| Module conf merge | `Shared/Services/Module/ModuleInvoke.cs` |

## Workflow

1. Search `Shared/Services` before adding a helper.
2. If the public signature changes, grep `Core/`, `Online/`, `SISI/`, and `Modules/`.
3. `dotnet build Shared/Shared.csproj --nologo`

## Current shape

`BaseOnlineController` is what VOD modules call: `IsRequestBlocked`, `OnError`, `InvokeCacheResult`, `ContentTpl`, `HostStreamProxy`, `InvkSemaphore`. English embeds use `BaseENGController.ViewTmdb`, which loads seasons from the Cub TMDB mirror. Adult modules use `BaseSisiController`. Do not add a fourth base.

## Review pass

Grep `Shared` for `GetAwaiter().GetResult()`, `.Result`, and `new HttpClient`. Fix the helper once, then confirm callers in `Core` and `Modules` still compile. Cache work stays on `HybridFileCache`; do not add another cache type.

## Do not

- Add a package that is already in `Directory.Packages.props` under another name.
- Allocate `new HttpClient` per request. Factory clients are created in `Core/Startup.cs`. Cookie or custom-handler cases go through `FriendlyHttp.MessageClient`.
