# mintlify-docsops

Operator documentation under `docs/`.

## Triggers

MDX pages, `docs.json` navigation, redirects, frontmatter, Mintlify components, API MDX, broken links, a11y, or alignment of docs with `manifest.json`, `base.conf`, `example.init`, and `[Route]` attributes.

## Inputs

- [docs/AGENTS.md](../../docs/AGENTS.md)
- [`.cursor/rules/mintlify-docs.mdc`](../../.cursor/rules/mintlify-docs.mdc)
- [`.agents/skills/mintlify/SKILL.md`](../skills/mintlify/SKILL.md) — read this. Do not follow `mintlify-docs` separately
- [`.agents/skills/mintlify-api/SKILL.md`](../skills/mintlify-api/SKILL.md) only for API reference pages
- Facts from `[Route]`, `manifest.json`, `config/base.conf`, `config/example.init.*`. Do not invent defaults

## Allowed tools

- Read product code to verify facts
- Edit under `docs/` (MDX, `docs.json`, `.mintignore`, assets)
- Mintlify MCP from `.cursor/mcp.json` (`https://mintlify.com/docs/mcp`, `https://mcp.mintlify.com`)
- From `docs/`: `mint validate`, `mint broken-links --check-anchors`, `mint a11y` when the CLI is installed
- Do not push via Mintlify Admin MCP unless the user asks

## Working directories

- `docs/`

## Deliverables

- Russian pages, sentence-case headings, required frontmatter, root-relative links without `/docs/` or `.mdx`
- Every new page added to `docs/docs.json`, with old slugs kept or `/docs/...` redirects
- One page per functional module and per provider group, not per VOD provider
- API stays hand-written MDX. No OpenAPI unless explicitly requested
- Report of pages touched, nav or redirect changes, code sources checked, and CLI results

## Does not own

- Application code, unless the user asked
- A second copy of Mintlify skill instructions (`mintlify-docs` is kept only for `skills-lock.json`)
- Tokens, passwords, provider credentials, private hosts, cookies, or user data
- Copying MDX into `.devin/wiki.json`
