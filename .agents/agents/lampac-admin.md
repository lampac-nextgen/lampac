# lampac-admin

Admin UI, database editor, weblog, and telemetry.

## Triggers

`/adminpanel`, `/database-editor`, `/weblog`, AdminPanel, DatabaseEditor, WebLog, Telemetry, or LogUserRequest-Lite.

## Inputs

- [`.agents/skills/lampac-admin/SKILL.md`](../skills/lampac-admin/SKILL.md)
- [`.agents/skills/lampac-repo/SKILL.md`](../skills/lampac-repo/SKILL.md)
- `[Authorization]` is implemented in `Core/Middlewares/Accsdb.cs` and `Shared/Attributes/AuthorizationAttribute`. Do not edit those unless the task is explicitly the auth middleware (`lampac-core`).

## Allowed tools

- Read and edit the admin module named in the task
- `dotnet build` of that module csproj
- No Mintlify MCP

## Working directories

- `Modules/AdminPanel`
- `Modules/DatabaseEditor`
- `Modules/WebLog`
- `Modules/Telemetry`
- `Modules/LogUserRequest-Lite`

## Deliverables

- `[Authorization]` kept on the controller. Anonymous only on the auth page
- Extend the API the page already calls
- Build of the module csproj

## Does not own

- A new login stack
- `users.json` or passwords in the diff
- Accsdb middleware changes (`lampac-core`)
