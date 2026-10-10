---
name: lampac-sync
description: >-
  Works on Lampac user sync: bookmarks, timecode, storage, sync events, and
  watch together. Use for /bookmark, /timecode, Modules/Sync, WatchTogether,
  or SeriesNotify.
---

# Lampac sync

Read [lampac-repo](../lampac-repo/SKILL.md) first. Pair with [`.cursor/agents/lampac-sync.md`](../../../.cursor/agents/lampac-sync.md). Role card: [`.agents/agents/lampac-sync.md`](../../agents/lampac-sync.md).

## Layout

| Path | Routes / role |
| ------ | ---------------- |
| `Modules/Sync/Sync/` | `bookmark.js`, `/bookmark/list`, `/bookmark/sync` |
| `Modules/Sync/TimeCode/` | `timecode.js`, `/timecode/add` |
| `Modules/Sync/Storage/` | User storage |
| `Modules/Sync/SyncEvents/` | Sync event stream |
| `Modules/WatchTogether/` | Joint playback |
| `Modules/SeriesNotify/` | Series and voice-over notifications |
| `Modules/TimeCode/` | Only if the route is not already in `Modules/Sync/TimeCode` |

Plugin URLs use `{token}` the same way as `bookmark/js/{token}` and `timecode/js/{token}`. Keep that shape.

## Workflow

1. Stay in one of the rows. Bookmark bugs are not timecode bugs.
2. User ids and tokens come from the existing request model. Do not add a second account store.
3. Build the csproj you edited.

## Current shape

Bookmarks: `bookmark.js`, `bookmark/js/{token}`, then `/bookmark/list|set|add|remove|sync` in `Modules/Sync/Sync/Controllers/BookmarkController.cs`. Timecode: `timecode.js` and `/timecode/add` in `Modules/Sync/TimeCode/Controller.cs`. Storage and SyncEvents are sibling projects. WatchTogether and SeriesNotify are separate modules, not actions on the bookmark controller.

## Review pass

Read the controller and its store. Check that a sync write is scoped to the current user, that a failed DB write does not wipe the other user’s rows, and that token routes do not log the token.

## Do not

- Merge bookmark and timecode into one controller.
- Commit user databases or tokens.
