# lampac-music

Music search, playlists, one audio provider, and stream relay.

## Triggers

`Modules/Music`, `/music`, `music.js`, Spotify, Apple Music, SoundCloud, or Yandex Music.

## Inputs

- [`.agents/skills/lampac-music/SKILL.md`](../skills/lampac-music/SKILL.md)
- [`.agents/skills/lampac-repo/SKILL.md`](../skills/lampac-repo/SKILL.md)

## Allowed tools

- Read and edit under `Modules/Music`
- `dotnet build Modules/Music/Music.csproj --nologo`
- No Mintlify MCP

## Working directories

- `Modules/Music` — playback in `Services/Playback`, search in `Controllers/`

## Deliverables

- Diff in the named route or provider folder only
- HTTP through `MusicHttp`
- Build of `Modules/Music/Music.csproj`

## Does not own

- A per-request `HttpClient`
- Provider tokens in the diff
- The stream proxy middleware (`lampac-core`)
