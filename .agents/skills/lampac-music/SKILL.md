---
name: lampac-music
description: >-
  Works on the Lampac Music module: search, playlists, provider auth, and
  stream relay. Use for /music, music.js, Spotify, Apple Music, SoundCloud,
  Yandex, or Modules/Music.
---

# Lampac Music

Read [lampac-repo](../lampac-repo/SKILL.md) first. Pair with [`.cursor/agents/lampac-music.md`](../../../.cursor/agents/lampac-music.md). Role card: [`.agents/agents/lampac-music.md`](../../agents/lampac-music.md).

## Layout

| Path | Role |
| ------ | ------ |
| `Modules/Music/Controllers/` | `music.js`, `/music/search`, playlists, track |
| `Modules/Music/Providers/` | Spotify, Apple Music, SoundCloud, Yandex, audio sources |
| `Modules/Music/Services/Http/MusicHttp.cs` | Provider HTTP clients |
| `Modules/Music/Services/Playback/` | Stream relay, including Range |
| `Modules/Music/Services/Cache/` | Metadata cache |
| `Modules/Music/SQL/` | Local music DB |

## Workflow

1. Name the provider or the controller route. Do not edit every provider for a bug in one.
2. Playback changes stay in `Services/Playback`. Search and playlist changes stay in `Controllers/`.
3. Reuse `MusicHttp`. Do not add a new `HttpClient` per search request.
4. `dotnet build Modules/Music/Music.csproj --nologo`

## Current shape

Public routes start at `music.js` and `music/js/{token}` (`ApiController`). Search, artist, album, and track are `SearchController` under `music/search`, `music/artist`, `music/album`, `music/track`. Playlists are on `ApiController`. Provider HTTP is `Services/Http/MusicHttp.cs`. Range relay is `Services/Playback/`. One provider folder per source (Spotify, Apple Music, SoundCloud, Yandex).

## Review pass

One provider folder or one controller. Check Range forwarding on the relay, that provider tokens are not written into logs or cache keys, and that a failed provider returns an empty list instead of failing the whole search.

## Do not

- Pull music providers into `Modules/Online*`.
- Commit OAuth secrets.
