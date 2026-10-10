# Agent architecture theses

Portable contracts for every host that reads this repository. Product behavior is not defined here. It lives in code, `config/base.conf`, and each module `manifest.json`. Routing lives in [AGENTS.md](../../AGENTS.md). Role cards live in [`.agents/agents/`](../agents/).

## Config single source of truth

**Context.** Lampac merges configuration from code, the shipped tree, the operator file, and module manifests. Agents that invent a default write the wrong layer.

**Problem.** Four files can look like “the config.” Editing the template, or documenting a value that only exists in code, ships a lie.

**Solution.** Read the layer you change. Do not invent a default.

1. `Shared/CoreInit.cs` — value when the key is absent
2. `config/base.conf` — override shipped with the tree (wins over CoreInit)
3. Operator `init.conf` / `init.yaml` in the process working directory
4. `manifest.json` `enable` — whether that module is compiled and loaded

`config/example.init.conf` and `config/example.init.yaml` are templates only.

**Acceptance criteria.**

- A config edit names which layer wins.
- Examples and docs do not copy tokens, passwords, cookies, or private hosts.
- `example.init.*` is not described as the running config.

## Least privilege and separation of roles

**Context.** Fourteen roles cover the host, shared library, modules, and operator docs. Cursor entrypoints are `.cursor/agents/*.md`. The role text is `.agents/agents/<name>.md`.

**Problem.** Overlapping owners edit the same controller, or a security pass grows a second agent that rewrites files outside the task.

**Solution.** One task, one directory. Do not start a second agent on the same controller.

- `lampac-modules` creates the project skeleton. The area agent then edits behavior.
- `lampac-core` owns `Core/`, including `Core/Middlewares/ProxyAPI*.cs`. `lampac-media` owns `Modules/Proxy`, transcoding, TorrServer, DLNA, and Tracks.
- `Modules/OnlinePacks` belongs to `lampac-online`, not `lampac-clients`.
- A security pass stays with the agent that owns the directory and uses the review pass in `lampac-repo`. There is no security agent.
- `lampac-repo` is a skill, not an agent. Every Lampac role reads it.

**Acceptance criteria.**

- The diff stays inside the working directories on the role card.
- A public signature change in `Shared` or `Core` is followed by a caller grep under `Core/`, `Online/`, `SISI/`, and `Modules/`.
- High findings are `path:line` plus a small patch. No exploit write-up. No full-file commented rewrite.

## Verification gates

**Context.** There is no test csproj in `NextGen.slnx`. `dotnet test` is not a gate. CI workflows are not a local substitute for the check that matches the edit.

**Problem.** A change can look done while the edited project does not build, a docs page fails Mintlify checks, or a config default was never read.

**Solution.** Before the change is done:

1. Stay inside the working directories of the role.
2. If a public signature in `Shared` or `Core` changes, grep callers under `Core/`, `Online/`, `SISI/`, and `Modules/`.
3. Build only the csproj that changed: `dotnet build path/to/Project.csproj --nologo`.
4. A change under `site/` is verified with `npm run build` in `site/`.
5. A change under `docs/` is verified from `docs/` with the Mintlify CLI when it is installed: `mint validate`, `mint broken-links --check-anchors`, and `mint a11y`.
6. A config edit names which layer wins (thesis 1).
7. The diff must not add tokens, passwords, cookies, or private hosts.
8. A security pass reports High / Medium / Low with `path:line` and patches High findings with a small diff.

Agent metadata itself is checked with `node .agents/scripts/validate-agents.mjs`.

**Acceptance criteria.**

- The verification command on the role card was run, or the report says why it could not run.
- No new CI system is added for these gates.
- OpenAPI is not added unless explicitly requested. API pages stay hand-written MDX.
