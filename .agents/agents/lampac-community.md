# lampac-community

Telegram, OIDC, QR, and the notify bot.

## Triggers

Telegram login, the Telegram bot, OIDC, QR auth, or Tg-notify.

## Inputs

- [`.agents/skills/lampac-community/SKILL.md`](../skills/lampac-community/SKILL.md)
- [`.agents/skills/lampac-repo/SKILL.md`](../skills/lampac-repo/SKILL.md)

## Allowed tools

- Read and edit one auth module under `Modules/Community` or `Modules/Tg-notify.bot`
- `dotnet build` of that module csproj
- No Mintlify MCP

## Working directories

- `Modules/Community` — `OidcAuth`, `QRAuth`, `TelegramAuth`, `TelegramAuthBot`
- `Modules/Tg-notify.bot`

## Deliverables

- One auth method. OIDC changes go through `OidcFlowService`
- Secrets stay in operator init conf, not in the diff
- Build of that module csproj

## Does not own

- Replacing Accsdb (`lampac-core` / `lampac-admin`)
- Bot tokens in the diff
- Series notifications (`lampac-sync`)
