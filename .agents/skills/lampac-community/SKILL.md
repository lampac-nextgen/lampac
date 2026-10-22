---
name: lampac-community
description: >-
  Works on Lampac community auth: Telegram, OIDC, QR, and notify bots. Use
  for Modules/Community, TelegramAuth, OidcAuth, QRAuth, or Tg-notify.
---

# Lampac community auth

Read [lampac-repo](../lampac-repo/SKILL.md) first. Pair with [`.cursor/agents/lampac-community.md`](../../../.cursor/agents/lampac-community.md). Role card: [`.agents/agents/lampac-community.md`](../../agents/lampac-community.md).

## Layout

| Path | Role |
| ------ | ------ |
| `Modules/Community/TelegramAuth/` | Telegram login |
| `Modules/Community/TelegramAuthBot/` | Bot process and Lampac HTTP client |
| `Modules/Community/OidcAuth/` | OIDC authorize and code exchange |
| `Modules/Community/QRAuth/` | QR login |
| `Modules/Tg-notify.bot/` | Notify bot |

OIDC redirect and token exchange already live in `OidcAuth/Services/OidcFlowService.cs`. Extend that type. Do not add a second OIDC client.

## Workflow

1. Pick one auth method. Read its `ModInit.cs` and the service it calls.
2. Secrets stay in init conf. Source gets empty placeholders only.
3. Build that module’s csproj.

## Current shape

OIDC URL building and code exchange are `OidcAuth/Services/OidcFlowService.cs` (`BuildAuthorizeUrl`, `ExchangeCodeAsync`). Telegram login and the bot are separate projects. The bot’s HTTP back to Lampac is `LampacTelegramAuthHttpClient`. QR is `QRAuth`. Do not fold these into one controller.

## Review pass

Read the login flow end to end (start, callback, session). Check the state/nonce on OIDC, that bot tokens are not logged, and that a failed provider does not mark the user logged in.

## Do not

- Replace accsdb. These modules plug into the existing user model.
- Commit bot tokens.
