---
name: lampac-modules
description: >-
  Adds or wires a Lampac module: manifest.json, ModInit, csproj, and
  NextGen.slnx. Use when creating a module, changing LoadModules, SkipModules,
  IModuleLoaded, or dynamic compile.
---

# Lampac modules

Read [lampac-repo](../lampac-repo/SKILL.md) first. Pair with [`.cursor/agents/lampac-modules.md`](../../../.cursor/agents/lampac-modules.md). Role card: [`.agents/agents/lampac-modules.md`](../../agents/lampac-modules.md). Load order is documented in `docs/architecture/modules.mdx`.

## Copy this shape

Clone the nearest module. A small provider looks like `Modules/OnlineRUS/HDVB/`.

`manifest.json`:

```json
{
  "enable": true,
  "dynamic": true,
  "tree": ["Controller.cs", "Model.cs", "ModInit.cs"]
}
```

`*.csproj`: `Microsoft.NET.Sdk.Web`, `net10.0`, `OutputType` library, `ProjectReference` to `Shared/Shared.csproj` only.

Add the project to `NextGen.slnx` beside the same family's entries:

```xml
<Project Path="Modules/OnlineRUS/HDVB/HDVB.csproj" />
```

## ModInit

Implement the interfaces the sibling uses (`IModuleLoaded`, and for VOD `IModuleOnline` / `IModuleOnlineSpider`). Load conf with `ModuleInvoke.Init`. Subscribe to `EventListener.UpdateInitFile` in `Loaded` and unsubscribe in `Dispose`.

`manifest.enable: false` means the module is not loaded. `SkipModules` / `LoadModules` in `base.conf` and `init.conf` filter on top of that.

## Workflow

1. Pick the sibling. Do not design a new module layout.
2. Copy manifest, ModInit, csproj, and only the files the feature needs.
3. Register the csproj in `NextGen.slnx`.
4. Build that csproj.

## Current shape

Online buttons are not registered in `Startup`. `OnlineModuleEntry.EnsureCache` scans enabled assemblies for `IModuleOnline`, `IModuleOnlineAsync`, `IModuleOnlineSpider`, and the async spider interface. The type needs a public parameterless constructor. A module that only implements `IModuleLoaded` still gets conf, but it is absent from `/online.js`.

## Review pass

For one module, read `manifest.json`, `ModInit.cs`, and the csproj. Check `enable`, the `tree` list matches real files, and `Dispose` unsubscribes every event `Loaded` subscribed. Do not audit every module in one pass.

## Do not

- Set `dynamic: true` unless the module should rebuild when its `.cs` files change.
- Put secrets in `manifest.json`.
