---
name: lampac-core
description: >-
  Works on the Lampac ASP.NET host: Program, Startup, middleware, named
  HttpClients, and module load from mods/ and module/. Use when editing
  Core/, the request pipeline, WAF, Accsdb, ProxyAPI host wiring, or port 9118.
---

# Lampac Core

Read [lampac-repo](../lampac-repo/SKILL.md) first. Pair with [`.cursor/agents/lampac-core.md`](../../../.cursor/agents/lampac-core.md). Role card: [`.agents/agents/lampac-core.md`](../../agents/lampac-core.md).

## Files

- `Core/Program.cs` — host entry
- `Core/Startup.cs` — DI, HTTP clients, module load
- `Core/Middlewares/` — pipeline partials
- `Core/Core.csproj` — ProjectReference to Shared only. Do not add PackageReferences here

## Named HTTP clients

Registered in `Startup.cs`: `proxy`, `proxyRedirect`, `proxyimg`, `http2proxyimg`, `base`, `baseNoRedirect`, `http2`, `http2NoRedirect`, `http3`, `http3NoRedirect`.

Reuse one of these. A new name needs a real handler difference (redirect policy or HTTP version), not a one-off call.

## Module load

`Startup.cs` walks `mods/` then `module/`. Source folders respect `LoadModules` and `SkipModules`. DLLs in those folders are loaded separately from Roslyn compilation. Details: `docs/architecture/modules.mdx`.

## Workflow

1. Find the middleware or service and the line in `Startup.cs` that registers it.
2. Match the partial-class style already in `Core/Middlewares/`.
3. Keep stream copies tied to `HttpContext.RequestAborted`.
4. `dotnet build Core/Core.csproj --nologo`

## Current shape

`Startup.cs` registers the named clients listed above and loads `mods/` then `module/`. Source folders honor `LoadModules`. `Online/ModInit` inserts WAF limits for `^/lite/` at startup. Proxy streaming is `Core/Middlewares/ProxyAPI` plus partials, not a controller.

## Review pass

Read `Startup.cs` client registration and the middleware you were asked about (`ProxyAPI`, `WAF`, `Accsdb`). Flag sync-over-async, a `new HttpClient` on the request path, and a stream copy that ignores `RequestAborted`. Fix only that path.

## Do not

- Add a second host or a new middleware library.
- Move shared types out of `Shared` into `Core` when modules need them.
