---
name: lampac-admin
description: >-
  Works on Lampac admin and diagnostics: AdminPanel, DatabaseEditor, WebLog,
  Telemetry, and request logs. Use for /adminpanel, /database-editor, /weblog,
  or Telemetry.
---

# Lampac admin

Read [lampac-repo](../lampac-repo/SKILL.md) first. Pair with [`.cursor/agents/lampac-admin.md`](../../../.cursor/agents/lampac-admin.md). Role card: [`.agents/agents/lampac-admin.md`](../../agents/lampac-admin.md).

## Layout

| Path | Route | Auth |
| ------ | -------- | ------ |
| `Modules/AdminPanel/` | `/adminpanel` | `[Authorization]` → `/adminpanel/auth`, checks `rootPasswd` in `Core/Middlewares/Accsdb.cs` |
| `Modules/DatabaseEditor/` | `/database-editor` | `[Authorization]` → `/weblog/auth` |
| `Modules/WebLog/` | `/weblog` | `[Authorization]` |
| `Modules/Telemetry/` | telemetry UI | `[Authorization]` → `/adminpanel/auth` |
| `Modules/LogUserRequest-Lite/` | request log | read its ModInit before exposing a route |

`[Authorization]` is the attribute in `Shared/Attributes/AuthorizationAttribute.cs`. It is enforced by `Accsdb` middleware even when `accsdb.enable` is false. Do not replace it with a new cookie scheme.

AdminPanel writes `init.conf`, `current.conf`, and `users.json`. Keep writes on those files. Do not invent a new config store.

## Workflow

1. Read the controller and confirm `[Authorization]` is on the type, with `[AllowAnonymous]` only on the auth page.
2. Change the API the page already calls. Do not add a parallel admin API.
3. Build the module csproj.

## Current shape

`AdminPanelController` is `[Authorization(redirectUri: "/adminpanel/auth")]`. The auth page is `[AllowAnonymous]`. It reads and writes `init.conf`, `current.conf`, and `users.json`. DatabaseEditor API uses the same attribute with a JSON 401 body and redirects the HTML page to `/weblog/auth`. Enforcement is `Core/Middlewares/Accsdb.cs` against `CoreInit.rootPasswd`.

## Review pass

List routes on the controller. Any new route must keep the class-level auth attribute. Check that JSON error bodies do not include stack traces or conf secrets.

## Do not

- Document a new admin guide here. Operator pages belong to `mintlify-docsops`.
- Commit `users.json` or passwords.
