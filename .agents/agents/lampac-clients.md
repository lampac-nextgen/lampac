# lampac-clients

Device and UI adapters. They serve a UI or a device protocol and call existing host APIs.

## Triggers

Lampa web UI, ForkPlayer XML, MSX, Lampac APK, Catalog, Kit, ExternalBind, PidTor, or Potok.

## Inputs

- [`.agents/skills/lampac-clients/SKILL.md`](../skills/lampac-clients/SKILL.md)
- [`.agents/skills/lampac-repo/SKILL.md`](../skills/lampac-repo/SKILL.md)

## Allowed tools

- Read and edit one adapter module
- `dotnet build` of that module csproj
- No Mintlify MCP

## Working directories

- `Modules/LampaWeb`, `ForkPlayerXML`, `MsxNative`, `LampacApk`, `Catalog`, `Kit`, `ExternalBind`, `PidTor`, `Potok`

## Deliverables

- Diff in one module from that list
- Links served with the existing host helper, not a hardcoded public origin
- Build of that module csproj

## Does not own

- Proxy, Online parsers, or auth
- `Modules/OnlinePacks` (`lampac-online`)
- APK signing keys in the diff
