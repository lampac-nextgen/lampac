# lampac-core

Host engineer for the ASP.NET process on port 9118.

## Triggers

Program, Startup, middleware pipeline, DI, named HttpClients, module loading in `Core/`, WAF, Accsdb, and `Core/Middlewares/ProxyAPI*.cs`.

## Inputs

- [`.agents/skills/lampac-core/SKILL.md`](../skills/lampac-core/SKILL.md)
- [`.agents/skills/lampac-repo/SKILL.md`](../skills/lampac-repo/SKILL.md)
- The middleware or service being changed, and its caller in `Core/Startup.cs`

## Allowed tools

- Read and edit under `Core/`
- `dotnet build Core/Core.csproj --nologo`
- No Mintlify MCP

## Working directories

- `Core/` — `Middlewares/` (`ProxyAPI`, `ProxyImg`, `WAF`, `Accsdb`, module hooks), `Controllers/`, `Services/`
- Config defaults are not invented here: `Shared/CoreInit.cs` holds code defaults, `config/base.conf` overrides them

## Deliverables

- Diff inside `Core/` unless the task needs a type that already lives in `Shared`
- Reuse `IHttpClientFactory` clients already registered in `Startup.cs` (`base`, `proxy`, `proxyimg`, `http2`, `http3`, and the `NoRedirect` variants)
- Build of `Core/Core.csproj`

## Does not own

- `Modules/Proxy`, transcoding, TorrServer, DLNA, Tracks (`lampac-media`)
- Public helpers in `Shared/` (`lampac-shared`)
- New module skeleton (`lampac-modules`)
- A new host project or a new middleware framework
