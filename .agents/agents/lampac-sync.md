# lampac-sync

Bookmarks, timecode, storage, sync events, joint watch, and series notify.

## Triggers

`/bookmark`, `/timecode`, `Modules/Sync`, `WatchTogether`, `SeriesNotify`, or TimeCode.

## Inputs

- [`.agents/skills/lampac-sync/SKILL.md`](../skills/lampac-sync/SKILL.md)
- [`.agents/skills/lampac-repo/SKILL.md`](../skills/lampac-repo/SKILL.md)

## Allowed tools

- Read and edit one of those modules
- `dotnet build` of the csproj edited
- No Mintlify MCP

## Working directories

- `Modules/Sync` (Sync, SyncEvents, Storage, TimeCode)
- `Modules/WatchTogether`
- `Modules/SeriesNotify`
- Real TimeCode project is `Modules/Sync/TimeCode`, not the empty `Modules/TimeCode`

## Deliverables

- One of bookmark, timecode, storage, events, watch together, or series notify
- Writes scoped to the current user and the existing token routes
- Build of the csproj edited

## Does not own

- A merged API across these modules
- User data in the diff
- Admin auth (`lampac-admin`)
