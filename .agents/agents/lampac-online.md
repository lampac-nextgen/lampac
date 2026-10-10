# lampac-online

VOD core and one provider family per task.

## Triggers

`Online/`, `/online.js`, `/lite` routes, and providers under `Modules/OnlineRUS`, `OnlinePaid`, `OnlineENG`, `OnlineUKR`, `OnlineGEO`, `OnlineAnime`, and `OnlinePacks`.

## Inputs

- [`.agents/skills/lampac-online/SKILL.md`](../skills/lampac-online/SKILL.md)
- [`.agents/skills/lampac-online/reference.md`](../skills/lampac-online/reference.md)
- [`.agents/skills/lampac-repo/SKILL.md`](../skills/lampac-repo/SKILL.md)
- [`.agents/skills/lampac-modules/SKILL.md`](../skills/lampac-modules/SKILL.md) only when the csproj is missing
- A sibling in the same family (`ModInit.Invoke` and the Index action). HDVB is family A only.

## Allowed tools

- Read and edit under `Online/` and the one `Modules/Online*` project for this task
- `dotnet build` of that provider csproj
- No Mintlify MCP

## Working directories

- `Online/` for the aggregator and `/online.js`
- One provider project under `Modules/OnlineRUS`, `OnlinePaid`, `OnlineENG`, `OnlineUKR`, `OnlineGEO`, `OnlineAnime`, or `OnlinePacks`

## Deliverables

- Button logic in `ModInit.Invoke`, playback logic in the controller
- Title and search text encoded when concatenated into a URL
- HTTP through `Http` / `FriendlyHttp` and the hybrid cache
- Build of that provider csproj

## Does not own

- A missing button fixed by editing the lite route instead of `ModInit.Invoke`
- The Online aggregator when the bug is inside one provider
- Client adapters (`lampac-clients`), including a claim on `Modules/OnlinePacks`
- A new `HttpClient` per request, or a page per title
- HTML helpers that already live in `Shared/Services/HTML/`
