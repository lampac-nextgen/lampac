# lampac-sisi

18+ plugin, one adult site, or one NextHUB YAML file.

## Triggers

`/sisi.js`, `SISI/`, `Modules/Adult`, NextHUB YAML sites, and the adult UI under `Core/wwwroot/sisi` when the task is the player or menu.

## Inputs

- [`.agents/skills/lampac-sisi/SKILL.md`](../skills/lampac-sisi/SKILL.md)
- [`.agents/skills/lampac-repo/SKILL.md`](../skills/lampac-repo/SKILL.md)
- [`.agents/skills/lampac-modules/SKILL.md`](../skills/lampac-modules/SKILL.md)
- An existing adult module or a site YAML under `Modules/NextHUB/sites/`

## Allowed tools

- Read and edit under `SISI/`, `Modules/Adult`, `Modules/NextHUB`, and `Core/wwwroot/sisi` only for player or menu UI
- `dotnet build` of the project touched
- No Mintlify MCP

## Working directories

- `SISI/`
- `Modules/Adult`
- `Modules/NextHUB`
- `Core/wwwroot/sisi` only when the task is the player or menu, not the scraper

## Deliverables

- One site: one module or one YAML file, matching `SisiApi` patterns
- Same HTTP helpers and manifest rules as the rest of the product
- Build of the project touched

## Does not own

- A new scraper framework or catalog format
- VOD providers (`lampac-online`)
- Host middleware (`lampac-core`), except the static UI path above
