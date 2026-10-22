---
name: lampac-jacred
description: >-
  Works on the JacRed module inside Lampac: torrent indexer API, tracker
  controllers, RedApi, and FileDB. Use when editing Modules/JacRed, /api/v2.0
  indexers, or a Jackett-style tracker.
---

# Lampac JacRed

Read [lampac-repo](../lampac-repo/SKILL.md) and [lampac-modules](../lampac-modules/SKILL.md). Pair with [`.cursor/agents/lampac-jacred.md`](../../../.cursor/agents/lampac-jacred.md). Role card: [`.agents/agents/lampac-jacred.md`](../../agents/lampac-jacred.md).

This is `Modules/JacRed` in lampac-nextgen, not a standalone JacRed tree.

## Layout

| Path | Role |
| ------ | ------ |
| `ApiController.cs` | `/api/v1.0/conf`, `/api/v2.0/indexers/{status}/results`, `/api/v1.0/torrents` |
| `Controllers/` | One tracker per file |
| `Engine/RedApi.cs`, `JackettApi.cs`, `WebApi.cs`, `SyncCron.cs` | Shared indexer engine |
| `Engine/FileDB/` | On-disk torrent index |
| `Engine/JacBaseController.cs` | Base type for JacRed controllers |
| `ModInit.cs` | Module conf, including `apikey` |

Packages already referenced: BencodeNET, MonoTorrent. Do not add another torrent library.

## Workflow

1. Read `ApiController.cs` and one controller in `Controllers/` (Rutor for a public HTML tracker, Rutracker for a cookie tracker).
2. A new tracker is a controller plus conf fields on the existing init object. No new engine type.
3. `dotnet build Modules/JacRed/JacRed.csproj --nologo`

## Current shape

`ApiController` serves `/api/v1.0/conf`, `/api/v2.0/indexers/{status}/results`, and `/api/v1.0/torrents`. The apikey check runs only when `ModInit.conf.apikey` is non-empty. Trackers live one class per file in `Controllers/` and share `Engine/RedApi`, `JackettApi`, `SyncCron`, and `FileDB`. JacRed controllers inherit `JacBaseController`.

## Review pass

Read `ApiController.cs` plus one tracker controller. Check the apikey guard, that a down tracker does not throw out of the indexer loop, and that cookies stay in conf rather than source. One tracker per pass.

## Do not

- Copy auth from a cookie tracker onto a public indexer.
- Commit tracker cookies or passwords.
