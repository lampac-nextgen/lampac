# lampac-jacred

Torrent indexer API and one tracker.

## Triggers

`Modules/JacRed`: `ApiController`, tracker controllers, RedApi, Jackett-style indexers, and the JacRed database.

## Inputs

- [`.agents/skills/lampac-jacred/SKILL.md`](../skills/lampac-jacred/SKILL.md)
- [`.agents/skills/lampac-repo/SKILL.md`](../skills/lampac-repo/SKILL.md)
- [`.agents/skills/lampac-modules/SKILL.md`](../skills/lampac-modules/SKILL.md)
- `ApiController.cs` and one existing tracker (`Rutor` or `Rutracker` style) before adding a source

## Allowed tools

- Read and edit under `Modules/JacRed`
- `dotnet build Modules/JacRed/JacRed.csproj --nologo`
- No Mintlify MCP

## Working directories

- `Modules/JacRed`

## Deliverables

- One tracker: one controller plus that tracker's `enable` / cookie / login conf
- BencodeNET and MonoTorrent only, via `Directory.Packages.props`
- Build of `Modules/JacRed/JacRed.csproj`

## Does not own

- A new torrent engine or a second library
- Another tracker's auth copied onto a public indexer
- TorrServer playback (`lampac-media`)
