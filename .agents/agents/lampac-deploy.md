# lampac-deploy

Install, image, Helm, Ansible, shipped config, and the release and GitHub Pages workflows.

## Triggers

`install.sh`, Docker, compose, Helm, Ansible, `build.sh`, `Makefile`, `config/base.conf`, `config/example.init.*`, `.github/workflows/release.yml`, or `.github/workflows/pages.yml`.

## Inputs

- [`.agents/skills/lampac-deploy/SKILL.md`](../skills/lampac-deploy/SKILL.md)
- [`.agents/skills/lampac-repo/SKILL.md`](../skills/lampac-repo/SKILL.md)
- The one config layer the task names: `Shared/CoreInit.cs` (read-only here unless the task is a code default, which is `lampac-shared`), `config/base.conf`, or `config/example.init.*`

## Allowed tools

- Edit one file from the skill table (install, image, chart, ansible, config, release, or Pages workflow)
- No Mintlify MCP. If an operator page becomes wrong, hand that edit to `mintlify-docsops`

## Working directories

- `install.sh`, `Dockerfile`, `docker-compose.yaml`, `docker-compose.dev.yaml`
- `charts/`, `ansible/`, `build.sh`, `Makefile`, `config/`
- `.github/workflows/release.yml`, `.github/workflows/pages.yml`

Docker and compose listen on port 9118. Dev compose uses 29118.

## Deliverables

- Edit only the source the task named, unless the user asked to keep layers aligned
- The report names which config layer wins
- No tokens, passwords, or private hosts in examples

## Does not own

- Application middleware or providers
- MDX under `docs/` (`mintlify-docsops`), unless the user asked this role to edit it
- Inventing a default that was not read in CoreInit, `base.conf`, or the template
