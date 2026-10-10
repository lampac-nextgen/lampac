---
name: lampac-clients
description: >-
  Works on Lampac client surfaces: Lampa web UI, ForkPlayer XML, MSX, APK
  builder, Catalog, Kit, ExternalBind, PidTor, and Potok. Use for /personal.lampa,
  /fxml, LampaWeb, MsxNative, or LampacApk.
---

# Lampac clients

Read [lampac-repo](../lampac-repo/SKILL.md) first. Pair with [`.cursor/agents/lampac-clients.md`](../../../.cursor/agents/lampac-clients.md). Role card: [`.agents/agents/lampac-clients.md`](../../agents/lampac-clients.md).

These are client adapters. They call Core and Online. They do not reimplement proxy or parsers.

## Layout

| Path | Surface |
| ------ | --------- |
| `Modules/LampaWeb/` | Lampa UI, `/`, `/personal.lampa`, `/extensions` |
| `Modules/ForkPlayerXML/` | `/fxml`, `/fxml/cub`, `/fxml/tmdb` |
| `Modules/MsxNative/` | MSX player |
| `Modules/LampacApk/` | Android APK generation |
| `Modules/Catalog/` | Site catalog YAML |
| `Modules/Kit/` | Crypto helpers used by clients |
| `Modules/ExternalBind/` | URL binding |
| `Modules/PidTor/`, `Modules/Potok/` | Separate client/source modules |
| `Modules/OnlinePacks/` | Owned by `lampac-online` if the change is a VOD provider pack |

## Workflow

1. Stay in one row. A ForkPlayer XML bug is not a Lampa widget change.
2. Lampa static files live under `Modules/LampaWeb/widgets/`. Match the existing widget (`lg` or `samsung`) instead of adding a third shell.
3. Build the module csproj. Widget-only JS/CSS still needs a look at the controller that serves it.

## Current shape

Lampa pages are `Modules/LampaWeb/Controllers/ApiController.cs`: `/`, `/personal.lampa`, `/extensions`. Widgets are `widgets/lg` and `widgets/samsung`. ForkPlayer is `fxml`, `fxml/cub`, `fxml/tmdb` in `Modules/ForkPlayerXML/Controllers/`. Those controllers call existing host APIs. They do not parse VOD pages.

## Review pass

Read the controller that serves the client. Check it does not echo `init.conf` secrets, and that generated links use the existing host helper (`CoreInit.Host` / `BaseController` host) rather than a hardcoded public URL.

## Do not

- Copy `widgets/lg` and `widgets/samsung` into a new theme unless the user asked for one.
- Commit signing keys for APK builds.
