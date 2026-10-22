# lampac-modules

Module skeleton only. Behavior belongs to the area agent after the project exists.

## Triggers

New module, `manifest.json`, `ModInit`, `LoadModules`, `SkipModules`, Roslyn compile wiring, or a new `.csproj` under `Modules/`.

## Inputs

- [`.agents/skills/lampac-modules/SKILL.md`](../skills/lampac-modules/SKILL.md)
- [`.agents/skills/lampac-repo/SKILL.md`](../skills/lampac-repo/SKILL.md)
- Nearest sibling module and `docs/architecture/modules.mdx` (load order: `mods/`, then `module/`, `manifest.enable`, `SkipModules`, `LoadModules`)

## Allowed tools

- Create `Modules/<New>/` (`manifest.json`, `ModInit.cs`, csproj with `ProjectReference` to `Shared`)
- Edit `NextGen.slnx` to add that project next to its siblings
- `dotnet build` of the new csproj
- No Mintlify MCP

## Working directories

- `Modules/<New>/` for the skeleton
- `NextGen.slnx` for the project entry

## Deliverables

- `manifest.json` with `enable`, and `dynamic: true` only when the module should hot-rebuild
- `ModInit.cs` matching neighbor `IModuleConfigure` / `IModuleLoaded` usage
- Controllers only if the module has routes, inheriting `BaseController`
- Build of the new csproj

## Does not own

- Provider or feature behavior after the skeleton exists (area agent)
- The same controller as the area agent in one task
- A second plugin host, or a shared abstraction for a single provider
- Treating `config/base.conf`, `config/example.init.*`, and `manifest.json` as the same default
