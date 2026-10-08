# Contributing

Thanks for helping improve Lampac NextGen.

## Issues

- Use the **Bug report** or **Feature request** forms.
- Questions and setup help belong in [Discussions](https://github.com/lampac-nextgen/lampac/discussions), not issues.
- Pick a **Component / Area** that matches the filesystem label (`core:*`, `module:*`, `meta:*`). Put the specific balancer or site in the **title** (e.g. `Kinogo: …`).
- Security vulnerabilities: do **not** open a public issue — see [SECURITY.md](SECURITY.md).

## Pull requests

### Title

Use [Conventional Commits](https://www.conventionalcommits.org/):

```text
type(scope): short summary
```

- **Types:** `feat`, `fix`, `docs`, `chore`, `ci`, `deps`, `security`, `revert` (also `style`, `test` when needed)
- **Scope:** module or area, e.g. `Kinogo`, `GStreamer`, `install`, `Sync`
- **Breaking:** add `!` after type/scope → `fix(QRAuth)!: …`
- Prefer **English** titles (repo convention). Body may be Russian or English.

### Body

The PR template asks for Summary, optional Context (`Closes #…`), Breaking changes, and a Test plan. Fill what applies; delete empty sections.

### Checks

- CI builds must pass. A **PR title** check fails on non-conventional titles (Dependabot / Actions bots are exempt).
- Path labels (`module:*`, `core:*`, `meta:*`) are applied automatically from changed files.
- An auto-format job may rewrite style on `main`; do not fight that.

## Docs and local context

- Product docs: [docs.lampac.dev](https://docs.lampac.dev)
- High-level layout: [README.md](README.md)
