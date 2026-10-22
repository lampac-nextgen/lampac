---
name: lampac-deploy
description: >-
  Changes Lampac install, Docker, Helm, Ansible, shipped config, and the
  release and GitHub Pages workflows. Use for install.sh, Dockerfile,
  docker-compose, charts/, ansible/, build.sh, base.conf, example.init,
  .github/workflows/release.yml, or .github/workflows/pages.yml.
---

# Lampac deploy

Read [lampac-repo](../lampac-repo/SKILL.md) first. Pair with [`.cursor/agents/lampac-deploy.md`](../../../.cursor/agents/lampac-deploy.md). Role card: [`.agents/agents/lampac-deploy.md`](../../agents/lampac-deploy.md).

## Which file

| Change | File |
| -------- | ------ |
| Code default when a key is missing | `Shared/CoreInit.cs` |
| Override shipped in git | `config/base.conf` |
| Template for a new operator | `config/example.init.conf` or `example.init.yaml` |
| Native Linux install | `install.sh` |
| Image | `Dockerfile`, `docker-compose.yaml` (9118), `docker-compose.dev.yaml` (29118) |
| Kubernetes | `charts/` |
| Ansible | `ansible/` |
| Local publish | `build.sh`, `Makefile` |
| Release tag | `.github/workflows/release.yml` (manual `tag` input) |
| GitHub Pages | `.github/workflows/pages.yml` (`npm ci` and `npm run build` in `site/`, publishes `site/dist`) |

Edit one row unless the user asked to keep two of them in sync. `example.init.*` is not what a running process loads.

## Workflow

1. Read the current value in the file from the table before writing a new one.
2. Keep examples free of real tokens and private hosts.
3. If `docs/configuration/` would become wrong, say which page. MDX edits belong to `mintlify-docsops` unless the user asked for the doc change.

## Current shape

`Shared/CoreInit.cs` is the default when a key is missing. `config/base.conf` overrides a few keys (it currently sets `accsdb.enable`, `WAF.enable`, and `serverproxy.verifyip`). `example.init.*` is not loaded by the process. Docker exposes `9118`. Dev compose uses `29118`.

## Review pass

Compare the key you were asked about across `CoreInit.cs`, `config/base.conf`, and `config/example.init.*`. Report which file wins at runtime. Update only the file from the table above. Docker and `install.sh` changes get a dry read of the script section you touch, not a full installer rewrite.

## Do not

- Bind the container to loopback unless the user asked. The image is meant to be reached on 9118.
- Change application logic in `Core` or `Modules` from a deploy task.
