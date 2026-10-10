---
name: lampac-online
description: >-
  Adds and fixes Lampac VOD providers and the Online core. Use for /online.js,
  /lite routes, BaseOnlineController, and Modules/OnlineRUS, OnlinePaid,
  OnlineENG, OnlineUKR, OnlineGEO, OnlineAnime, or OnlinePacks.
---

# Lampac Online

Read [lampac-repo](../lampac-repo/SKILL.md) and [lampac-modules](../lampac-modules/SKILL.md). Pair with [`.cursor/agents/lampac-online.md`](../../../.cursor/agents/lampac-online.md). Role card: [`.agents/agents/lampac-online.md`](../../agents/lampac-online.md). Region catalog: [reference.md](reference.md).

## Workflow

Read [reference.md](reference.md) and name the family before editing. HDVB is only family A.

```
Provider progress:
- [ ] 1. Family from reference.md (CDN, paid, ENG, anime, HTML)
- [ ] 2. Read that family's clone: ModInit.Invoke and the Index action
- [ ] 3. Change button logic in Invoke, playback logic in the controller
- [ ] 4. Encode title/search text that is concatenated into a URL
- [ ] 5. Build that provider csproj
```

`IModuleLoaded` without `IModuleOnline` does not add a source button. Spider search exists only when `IModuleOnlineSpider` is implemented.

## Review pass

One provider per pass. Read `ModInit.cs` and `Controller.cs`. Check search text is encoded, a failed upstream returns the existing empty/badInit path instead of throwing, and HTTP goes through `Http` / `FriendlyHttp`. A Cloudflare or HTML drift fix stays inside that provider’s parser.

## Do not

- Change `Online/` aggregator code for a bug inside one provider.
- Concatenate raw `title` into a query string.
- Add a provider outside `Modules/Online*` or `Online/`.
